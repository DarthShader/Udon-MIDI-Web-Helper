using System;
using System.Collections.Generic;
using TobiasErichsen.teVirtualMIDI;
using System.Text;

namespace Udon_MIDI_Web_Helper
{
    class MIDIManager
    {
        public const int MAX_ACTIVE_CONNECTIONS = 256;
        public const int MAX_USABLE_CONNECTIONS = 255;

        class ConnectionResponse
        {
            public byte connectionID;
            public byte[] data;
            public int bytesSent;
        }
        Queue<ConnectionResponse>[] responses = new Queue<ConnectionResponse>[MAX_ACTIVE_CONNECTIONS];
        ConnectionResponse pong = null;
        int responsesCount;
        int totalBytesCount;
        int connectionIndex;
        MIDIFrame lastFrame;
        TeVirtualMIDI port;
        bool flipFlop;

        bool gameReady = true;
        public bool GameIsReady
        {
            get
            {
                return gameReady;
            }
            set
            {
                gameReady = value;
            }
        }

        public int ResponsesCount
        {
            get
            {
                return responsesCount;
            }
        }

        public int TotalBytesCount
        {
            get
            {
                return totalBytesCount;
            }
        }

        public MIDIManager()
        {
            port = new TeVirtualMIDI("Udon-MIDI-Web-Helper", MIDIFrame.VRC_MAX_BYTES_PER_UPDATE, TeVirtualMIDI.TE_VM_FLAGS_PARSE_TX | TeVirtualMIDI.TE_VM_FLAGS_INSTANTIATE_TX_ONLY);
            
            for (int i = 0; i < responses.Length; i++)
                responses[i] = new Queue<ConnectionResponse>();
        }

        public void AddConnectionResponse(byte connectionID, byte[] data)
        {
            // This command should be called by HTTP request and WS threads when data is ready
            // to be send back to Udon.

            ConnectionResponse cr = new ConnectionResponse();
            cr.data = data;
            cr.connectionID = connectionID;
            responses[connectionID].Enqueue(cr);
            responsesCount++;
            totalBytesCount += data.Length;
        }

        public void SendFrameIfDataAvailable(bool ACK)
        {
            // If RDY was received when an ACK was expected, it means the frame was dropped
            // and needs to be retransmitted.  Otherwise, mark the last frame as received.
            if (lastFrame != null)
            {
                if (ACK)
                    lastFrame = null;
                else
                {
                    lastFrame.Send(port);
                    GameIsReady = false;
                    return;
                }
            }

            // Send priority ping response if available, or a frame if a connection response is ready
            if (pong != null)
            {
                ConnectionResponse responseToSend = pong;
                MIDIFrame mf = new MIDIFrame();
                mf.AddHeader2(responseToSend.connectionID, responseToSend.data[responseToSend.bytesSent++], flipFlop);
                flipFlop = !flipFlop;
                // Add up to 199 bytes from the active response to an array of data to send
                byte[] bytesToAdd = new byte[199];
                int bytesLeftToSend = responseToSend.data.Length - responseToSend.bytesSent;
                int bytesToAddCount = Math.Min(bytesLeftToSend, 199); // In case there's less than 199 bytes left to send
                Array.Copy(responseToSend.data, responseToSend.bytesSent, bytesToAdd, 0, bytesToAddCount);
                mf.Add199Bytes(bytesToAdd);
                pong = null;
                mf.Send(port);
                GameIsReady = false;
                lastFrame = mf;
            }
            else if (responses[255].Count > 0)
            {
                // Prioritize the loopback connection second
                SendFrameData(255);
            }
            else if (responsesCount > 0)
            {
                // Round robin the rest of the responses
                do
                {
                    connectionIndex++;
                    if (connectionIndex == MAX_USABLE_CONNECTIONS)
                        connectionIndex = 0;
                }
                while (responses[connectionIndex].Count == 0);

                SendFrameData(connectionIndex);
            }
        }

        void SendFrameData(int connectionID)
        {
            ConnectionResponse responseToSend = responses[connectionID].Peek();
            // Use up to 190 bytes from the response
            MIDIFrame mf = new MIDIFrame();
            // nullref here due to response queue being emptied on other thread
            mf.AddHeader2(responseToSend.connectionID, responseToSend.data[responseToSend.bytesSent++], flipFlop);
            totalBytesCount--;
            flipFlop = !flipFlop;

            // Add up to 199 bytes from the active response to an array of data to send
            byte[] bytesToAdd = new byte[199];
            int bytesLeftToSend = responseToSend.data.Length - responseToSend.bytesSent;
            int bytesToAddCount = Math.Min(bytesLeftToSend, 199); // In case there's less than 199 bytes left to send
            Array.Copy(responseToSend.data, responseToSend.bytesSent, bytesToAdd, 0, bytesToAddCount);
            mf.Add199Bytes(bytesToAdd);
            responseToSend.bytesSent += bytesToAddCount;
            totalBytesCount -= bytesToAddCount;

            // Remove response if all bytes have been sent
            if (responseToSend.bytesSent == responseToSend.data.Length)
            {
                responses[connectionID].Dequeue();
                responsesCount--;
            }

            mf.Send(port);
            GameIsReady = false;
            lastFrame = mf;
        }

        public void SendWebRequestFailedResponse(int connectionID, int responseCode)
        {
            // Make a dummy HTTP frame with data length 1, HTTP response error code
            byte[] data = new byte[4 + 4 + 1]; // 4 length, 4 response code, 1 data
            Array.Copy(BitConverter.GetBytes((int)5), 0, data, 0, 4);
            Array.Copy(BitConverter.GetBytes(responseCode), 0, data, 4, 4);
            AddConnectionResponse((byte)connectionID, data);
        }

        public void SendWebSocketClosedResponse(int connectionID)
        {
            // Send a single WS frame with data length 1, with error bit set in the text/binary byte flag
            byte[] data = new byte[4 + 1 + 1]; // 4 length, 1 flag byte, 1 data
            Array.Copy(BitConverter.GetBytes((int)2), 0, data, 0, 4); // Length of actual data is 2
            data[4] = 0x80; // High bit of the text/bin flag means the the connection was closed.
            AddConnectionResponse((byte)connectionID, data);
        }

        public void SendWebSocketOpenedResponse(int connectionID)
        {
            // Send a single WS frame with data length 1, with opened bit set in the text/binary byte flag
            byte[] data = new byte[4 + 1 + 1]; // 4 length, 1 flag byte, 1 data
            Array.Copy(BitConverter.GetBytes((int)2), 0, data, 0, 4); // Length of actual data is 2
            data[4] = 0x40; // Second highest bit of the text/bin flag means the the connection was opened.
            AddConnectionResponse((byte)connectionID, data);
        }

        public void SendPingResponse()
        {
            int statusCode = 0; // reserved response code for pings
            byte[] data = Encoding.Unicode.GetBytes(responsesCount + " " + totalBytesCount); // piggyback ping response with status info
            int connectionID = 255; // reserved loopback connection

            // A mix of WebManager's AddGenericResponse and MIDIManager's AddConnectionResponse
            // Send 4 bytes for response length, 4 bytes for response code, and then response data
            byte[] responseData = new byte[sizeof(int) * 2 + data.Length];
            Array.Copy(BitConverter.GetBytes(data.Length + sizeof(int)), 0, responseData, 0, sizeof(int)); // data length + response code
            Array.Copy(BitConverter.GetBytes(statusCode), 0, responseData, sizeof(int), sizeof(int));
            Array.Copy(data, 0, responseData, sizeof(int) * 2, data.Length);
            ConnectionResponse cr = new ConnectionResponse();
            cr.data = responseData;
            cr.connectionID = (byte)connectionID;
            pong = cr;

        }

        public void Reset()
        {
            GameIsReady = true;
            responsesCount = 0;
            totalBytesCount = 0;
            connectionIndex = 0;
            lastFrame = null;
            flipFlop = false;
            for (int i = 0; i < responses.Length; i++)
                responses[i] = new Queue<ConnectionResponse>();
        }

        public void ClearQueuedResponses(int connectionID)
        {
            responsesCount -= responses[connectionID].Count;
            int connectionByteTotal = 0;
            foreach (ConnectionResponse cr in responses[connectionID])
                connectionByteTotal += cr.data.Length - cr.bytesSent;
            totalBytesCount -= connectionByteTotal;
            responses[connectionID].Clear();
        }
    }
}
