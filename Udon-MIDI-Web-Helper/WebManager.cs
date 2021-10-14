using System;
using System.Threading;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Collections.Generic;

namespace Udon_MIDI_Web_Helper
{
    class WebManager
    {
        public const int WEBSOCKET_BUFFER_SIZE = 10000;

        HttpClient httpClient;
        MIDIManager midiManager;
        ClientWebSocket[] webSockets;
        CancellationTokenSource ctSource;
        ArraySegment<byte>[] wsBuffers;
        bool[] wsAutoConvertMessages;

        public CancellationTokenSource CTSource
        {
            get
            {
                return ctSource;
            }
        }

        public WebManager(MIDIManager m)
        {
            midiManager = m;
            Reset();
        }

        public async void GetWebRequest(int connectionID, Uri webUri, bool autoConvertResponse)
        {
            HttpResponseMessage response;
            try
            {
                response = await httpClient.GetAsync(webUri, ctSource.Token);
                
            }
            catch (Exception e)
            {
                Console.WriteLine("HTTP request failed: " + e.Message);
                midiManager.SendWebRequestFailedResponse(connectionID);
                return;
            }

            AddWebResponse(response, connectionID, autoConvertResponse);
        }

        public async void PostWebRequest(int connectionID, Uri webUri, bool autoConvertResponse, Dictionary<string, string> args)
        {
            HttpResponseMessage response;
            try
            {
                response = await httpClient.PostAsync(webUri, new FormUrlEncodedContent(args), ctSource.Token);
            }
            catch (Exception e)
            {
                Console.WriteLine("HTTP POST request failed: " + e.Message);
                midiManager.SendWebRequestFailedResponse(connectionID);
                return;
            }

            AddWebResponse(response, connectionID, autoConvertResponse);
        }

        async void AddWebResponse(HttpResponseMessage response, int connectionID, bool autoConvertResponse)
        {
            // Create byte array for HTTP response
            int statusCode = (int)response.StatusCode;
            byte[] data = await response.Content.ReadAsByteArrayAsync();
            // Since System.Text.Encoding isn't whitelisted in Udon,
            // Unity represents strings internally as UTF16, and almost
            // all web content is encoded in UTF8, the helper has an
            // option when making web requests to convert the response
            // from UTF8 to UTF16 before sending data through MIDI.
            if (autoConvertResponse)
                data = Encoding.Convert(Encoding.UTF8, Encoding.Unicode, data);
            AddGenericResponse(statusCode, data, connectionID);
        }

        public void AddGenericResponse(int statusCode, byte[] data, int connectionID)
        {
            // Send 4 bytes for response length, 4 bytes for response code, and then response data
            byte[] responseData = new byte[sizeof(int) * 2 + data.Length];
            Array.Copy(BitConverter.GetBytes(data.Length + sizeof(int)), 0, responseData, 0, sizeof(int)); // data length + response code
            Array.Copy(BitConverter.GetBytes(statusCode), 0, responseData, sizeof(int), sizeof(int));
            Array.Copy(data, 0, responseData, sizeof(int) * 2, data.Length);
            midiManager.AddConnectionResponse((byte)connectionID, responseData);
        }

        public async void OpenWebSocketConnection(int connectionID, Uri webUri, bool autoConvertResponses)
        {
            webSockets[connectionID] = new ClientWebSocket();
            ClientWebSocket cws = webSockets[connectionID];
            try
            {
                await cws.ConnectAsync(webUri, ctSource.Token);
            }
            catch (WebSocketException e)
            {
                Console.WriteLine("Failed to open websocket: " + e.Message);
                Console.WriteLine("Closing websocket connection " + connectionID);
                midiManager.SendWebSocketClosedResponse(connectionID);
                webSockets[connectionID] = null;
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // Small arbitrary delay to make sure handshake goes thorugh;
            // for some reason an "Open" websocket will abort if the first message
            // is sent too soon.
            //Thread.Sleep(100);
            while (cws.State == WebSocketState.Connecting) ;
            if (cws.State == WebSocketState.Open)
                midiManager.SendWebSocketOpenedResponse(connectionID);

            wsBuffers[connectionID] = new ArraySegment<byte>(new byte[WEBSOCKET_BUFFER_SIZE]);
            wsAutoConvertMessages[connectionID] = autoConvertResponses;
            while (cws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult wssr = null;
                try
                {
                    wssr = await cws.ReceiveAsync(wsBuffers[connectionID], ctSource.Token);
                }
                catch (WebSocketException e)
                {
                    Console.WriteLine("WebSocketException: " + e.Message);
                    Console.WriteLine("Closing websocket connection " + connectionID);
                    midiManager.SendWebSocketClosedResponse(connectionID);
                    webSockets[connectionID] = null;
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    Console.WriteLine("WebSocket exception: " + e.Message);
                    Console.WriteLine("Closing websocket connection " + connectionID);
                    midiManager.SendWebSocketClosedResponse(connectionID);
                    webSockets[connectionID] = null;
                    break;
                }

                // Copy data to an intermediate array in case it needs to be converted from UTF8 to UTF16
                byte[] message = new byte[wssr.Count];
                Array.Copy(wsBuffers[connectionID].Array, 0, message, 0, wssr.Count);
                if (wsAutoConvertMessages[connectionID] && (wssr.MessageType == WebSocketMessageType.Text))
                    message = Encoding.Convert(Encoding.UTF8, Encoding.Unicode, message);

                // Send 4 bytes for response length, 1 bytes for txt/bin flag, and then response data
                byte[] responseData = new byte[4 + 1 + message.Length];
                Array.Copy(BitConverter.GetBytes(message.Length + 1), 0, responseData, 0, 4);
                responseData[4] = wssr.MessageType == WebSocketMessageType.Text ? (byte)0x0 :(byte)0x1;
                Array.Copy(message, 0, responseData, 5, message.Length);
                midiManager.AddConnectionResponse((byte)connectionID, responseData);
            }
        }

        public async void CloseWebSocketConnection(int connectionID)
        {
            try
            {
                // This method will most likely be called redundantly if the associated connection hasn't been
                // following correct (read: .NET compatible - looking at you, twitch.tv) websocket protocols, and broken connections in Udon
                // will want to force close a broken connection to be sure everything is flushed.
                // This CloseAsync call is expected to fail regularly.
                await webSockets[connectionID].CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ctSource.Token);
            }
            catch (Exception e)
            {
                Console.WriteLine("Could not close websocket: " + e.Message);
            }
        }

        public void SendWebSocketMessage(int connectionID, byte[] data, bool text, bool endOfMessage, bool autoConvertMessage)
        {
            // Allow both text and binary modes to be auto-converted
            if (autoConvertMessage)
                data = Encoding.Convert(Encoding.Unicode, Encoding.UTF8, data);

            try
            {
                if (webSockets[connectionID] != null)
                    _ = webSockets[connectionID].SendAsync(new ArraySegment<byte>(data), text ? WebSocketMessageType.Text : WebSocketMessageType.Binary, endOfMessage, ctSource.Token);
                else
                {
                    Console.WriteLine("WebSocket message could not be sent, connection was unexpectedly closed.");
                    Console.WriteLine("Closing websocket connection " + connectionID);
                    midiManager.SendWebSocketClosedResponse(connectionID); // This may be a duplicate close message depending on how things were closed
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error sending WebSocket message: " + e.Message);
            }
        }

        public void Reset()
        {
            if (ctSource != null)
                ctSource.Cancel();
            HttpClientHandler handler = new HttpClientHandler();
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            httpClient = new HttpClient(handler);
            webSockets = new ClientWebSocket[MIDIManager.MAX_ACTIVE_CONNECTIONS];
            wsBuffers = new ArraySegment<byte>[MIDIManager.MAX_ACTIVE_CONNECTIONS];
            ctSource = new CancellationTokenSource();
            wsAutoConvertMessages = new bool[MIDIManager.MAX_ACTIVE_CONNECTIONS];
        }
    }
}
