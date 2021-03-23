using System;
using System.Threading;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;

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
            HttpClientHandler handler = new HttpClientHandler();
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            httpClient = new HttpClient(handler);
            webSockets = new ClientWebSocket[MIDIManager.MAX_ACTIVE_CONNECTIONS];
            wsBuffers = new ArraySegment<byte>[MIDIManager.MAX_ACTIVE_CONNECTIONS];
            ctSource = new CancellationTokenSource();
        }

        public async void GetWebRequest(int connectionID, string uri)
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
                response = await httpClient.GetAsync(uri);
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

            // Send 4 bytes for response length, 4 bytes for response code, and then response data
            byte[] responseData = new byte[sizeof(int) * 2 + data.Length];
            Array.Copy(BitConverter.GetBytes(data.Length + sizeof(int)), 0, responseData, 0, sizeof(int)); // data length + response code
            Array.Copy(BitConverter.GetBytes(responseCode), 0, responseData, sizeof(int), sizeof(int));
            Array.Copy(data, 0, responseData, sizeof(int) * 2, data.Length);
            midiManager.AddConnectionResponse((byte)connectionID, responseData);
        }

        public async void OpenWebSocketConnection(int connectionID, string uri)
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
            CancellationToken token = ctSource.Token;
            await cws.ConnectAsync(webUri, token);
            wsBuffers[connectionID] = new ArraySegment<byte>(new byte[WEBSOCKET_BUFFER_SIZE]);
            while (cws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult wssr = await cws.ReceiveAsync(wsBuffers[connectionID], token);
                byte[] data = new byte[4 + 1 + wssr.Count];
                Array.Copy(BitConverter.GetBytes(wssr.Count+1), 0, data, 0, 4);
                data[4] = wssr.MessageType == WebSocketMessageType.Text ? (byte)0x0 :(byte)0x1;
                Array.Copy(wsBuffers[connectionID].Array, 0, data, 5, wssr.Count);
                midiManager.AddConnectionResponse((byte)connectionID, data);
            }
        }

        public async void CloseWebSocketConnection(int connectionID)
        {
            await webSockets[connectionID].CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ctSource.Token);
            midiManager.SendWebSocketClosedResponse(connectionID);
        }

        public void SendWebSocketMessage(int connectionID, byte[] data, bool text)
        {
            if (text)
                webSockets[connectionID].SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, false, ctSource.Token);
            else
                webSockets[connectionID].SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, false, ctSource.Token);
        }
    }
}
