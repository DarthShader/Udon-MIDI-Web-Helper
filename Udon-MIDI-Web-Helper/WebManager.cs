using System;
using System.Threading;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;

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

        public async void GetWebRequest(int connectionID, string uri, bool autoConvertResponse)
        {
            Uri webUri;
            try
            {
                webUri = new Uri(uri);
            }
            catch (UriFormatException e)
            {
                Console.WriteLine("URI incorrectly formatted: " + e.Message);
                midiManager.SendWebRequestFailedResponse(connectionID);
                return;
            }

            HttpResponseMessage response;
            try
            {
                response = await httpClient.GetAsync(uri, ctSource.Token);
            }
            catch (Exception e)
            {
                Console.WriteLine("HTTP request failed: " + e.Message);
                midiManager.SendWebRequestFailedResponse(connectionID);
                return;
            }

            // Create byte array for HTTP response
            int responseCode = (int)response.StatusCode;
            byte[] data = await response.Content.ReadAsByteArrayAsync();
            // Since System.Text.Encoding isn't whitelisted in Udon,
            // Unity represents strings internally as UTF16, and almost
            // all web content is encoded in UTF8, the helper has an
            // option when making web requests to convert the response
            // from UTF8 to UTF16 before sending data through MIDI.
            if (autoConvertResponse)
                data = Encoding.Convert(Encoding.UTF8, Encoding.Unicode, data);

            // Send 4 bytes for response length, 4 bytes for response code, and then response data
            byte[] responseData = new byte[sizeof(int) * 2 + data.Length];
            Array.Copy(BitConverter.GetBytes(data.Length + sizeof(int)), 0, responseData, 0, sizeof(int)); // data length + response code
            Array.Copy(BitConverter.GetBytes(responseCode), 0, responseData, sizeof(int), sizeof(int));
            Array.Copy(data, 0, responseData, sizeof(int) * 2, data.Length);
            midiManager.AddConnectionResponse((byte)connectionID, responseData);
        }

        public async void OpenWebSocketConnection(int connectionID, string uri, bool autoConvertResponses)
        {
            Uri webUri;
            try
            {
                webUri = new Uri(uri);
            }
            catch (UriFormatException e)
            {
                Console.WriteLine("URI incorrectly formatted: " + e.Message);
                midiManager.SendWebSocketClosedResponse(connectionID);
                return;
            }

            webSockets[connectionID] = new ClientWebSocket();
            ClientWebSocket cws = webSockets[connectionID];
            await cws.ConnectAsync(webUri, ctSource.Token);
            wsBuffers[connectionID] = new ArraySegment<byte>(new byte[WEBSOCKET_BUFFER_SIZE]);
            wsAutoConvertMessages[connectionID] = autoConvertResponses;
            while (cws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult wssr = await cws.ReceiveAsync(wsBuffers[connectionID], ctSource.Token);

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
            await webSockets[connectionID].CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ctSource.Token);
            midiManager.SendWebSocketClosedResponse(connectionID);
        }

        public void SendWebSocketMessage(int connectionID, byte[] data, bool text, bool autoConvertMessage)
        {
            // Allow both text and binary modes to be auto-converted
            if (autoConvertMessage)
                data = Encoding.Convert(Encoding.Unicode, Encoding.UTF8, data);

            if (text)
                webSockets[connectionID].SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, false, ctSource.Token);
            else
                webSockets[connectionID].SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, false, ctSource.Token);
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
