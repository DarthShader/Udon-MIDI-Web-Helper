using System;
using System.Collections.Generic;
using TobiasErichsen.teVirtualMIDI;

namespace Udon_MIDI_Web_Helper
{
    class MIDIManager
    {
        public const int MAX_ACTIVE_CONNECTIONS = 256;

        class ConnectionResponse
        {
            public byte connectionID;
            public byte[] data;
            public int bytesSent;
        }
        Queue<ConnectionResponse>[] responses = new Queue<ConnectionResponse>[MAX_ACTIVE_CONNECTIONS];
        ConnectionResponse pong = null;
        int responsesCount;
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
            else if (responsesCount > 0)
            {
                // Round robin the responses
                do
                {
                    connectionIndex++;
                    if (connectionIndex == MAX_ACTIVE_CONNECTIONS)
                        connectionIndex = 0;
                }
                while (responses[connectionIndex].Count == 0);
                ConnectionResponse responseToSend = responses[connectionIndex].Peek();

                // Use up to 190 bytes from the response
                MIDIFrame mf = new MIDIFrame();
                // nullref here due to response queue being emptied on other thread
                mf.AddHeader2(responseToSend.connectionID, responseToSend.data[responseToSend.bytesSent++], flipFlop);
                flipFlop = !flipFlop;

                // Add up to 199 bytes from the active response to an array of data to send
                byte[] bytesToAdd = new byte[199];
                int bytesLeftToSend = responseToSend.data.Length - responseToSend.bytesSent;
                int bytesToAddCount = Math.Min(bytesLeftToSend, 199); // In case there's less than 199 bytes left to send
                Array.Copy(responseToSend.data, responseToSend.bytesSent, bytesToAdd, 0, bytesToAddCount);
                mf.Add199Bytes(bytesToAdd);
                responseToSend.bytesSent += bytesToAddCount;

                // Remove response if all bytes have been sent
                if (responseToSend.bytesSent == responseToSend.data.Length)
                {
                    responses[connectionIndex].Dequeue();
                    responsesCount--;
                }

                mf.Send(port);
                GameIsReady = false;
                lastFrame = mf;
            }
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
            // Send a single WS frame with data length 1, with both open/close bits set
            byte[] data = new byte[4 + 1 + 1]; // 4 length, 1 flag byte, 1 data
            Array.Copy(BitConverter.GetBytes((int)2), 0, data, 0, 4); // Length of actual data is 2
            data[4] = 0xC0; // Both bits on a websocket frame represents a hacked-in priority ping response
            ConnectionResponse cr = new ConnectionResponse();
            cr.data = data;
            cr.connectionID = 255; // Reserve last connection ID
            pong = cr;
        }

        public void Reset()
        {
            GameIsReady = true;
            responsesCount = 0;
            connectionIndex = 0;
            lastFrame = null;
            flipFlop = false;
            for (int i = 0; i < responses.Length; i++)
                responses[i] = new Queue<ConnectionResponse>();
        }

        public void ClearQueuedResponses(int connectionID)
        {
            responsesCount -= responses[connectionID].Count;
            responses[connectionID].Clear();
        }
    }
}
