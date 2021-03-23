using System;
using System.Collections.Generic;
using TobiasErichsen.teVirtualMIDI;

namespace Udon_MIDI_Web_Helper
{
    class MIDIManager
    {
        public const int MAX_ACTIVE_CONNECTIONS = 255;
        const int WEB_REQUEST_FAILED_ERROR_CODE = 111;

        class ConnectionResponse
        {
            public byte connectionID;
            public byte[] data;
            public int bytesSent;
        }
        Queue<ConnectionResponse>[] responses = new Queue<ConnectionResponse>[MAX_ACTIVE_CONNECTIONS];
        int responsesCount;
        int connectionIndex;
        MIDIFrame lastFrame;
        TeVirtualMIDI port;

        bool gameReady = false;
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

        public MIDIManager()
        {
            port = new TeVirtualMIDI("Udon-MIDI-HTTP-Helper", MIDIFrame.VRC_MAX_BYTES_PER_UPDATE, TeVirtualMIDI.TE_VM_FLAGS_PARSE_TX | TeVirtualMIDI.TE_VM_FLAGS_INSTANTIATE_TX_ONLY);
            
            // Print MIDI driver header
            Console.WriteLine("TeVirtualMIDI started");
            Console.WriteLine("using dll-version:    " + TeVirtualMIDI.versionString);
            Console.WriteLine("using driver-version: " + TeVirtualMIDI.driverVersionString);

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

            if (GameIsReady)
                SendFrameIfDataAvailable(false);
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

            // Send a frame if data is available
            if (responsesCount > 0)
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
                mf.AddHeader(responseToSend.connectionID, responseToSend.data[0]);
                responseToSend.bytesSent++;

                // Add bytes in increments of 9 until either all bytes to send have been added
                // or no more space is available in the MIDIFrame.
                byte[] bytesToAdd = new byte[9];
                while (responseToSend.bytesSent < responseToSend.data.Length)
                {
                    if (mf.SpaceAvailable < 9)
                        break;

                    int bytesLeftToSend = responseToSend.data.Length - responseToSend.bytesSent;
                    int bytesToAddCount = Math.Min(bytesLeftToSend, 9); // In case there's less than 9 bytes left to send
                    Array.Copy(responseToSend.data, responseToSend.bytesSent, bytesToAdd, 0, bytesToAddCount);
                    mf.Add9Bytes(bytesToAdd);
                    responseToSend.bytesSent += 9;
                }

                // Fill the rest of the frame with dummy midi commands so it gets delivered correctly
                while (mf.SpaceAvailable > 0)
                    mf.Add9Bytes(bytesToAdd);

                // Remove response if all bytes have been sent
                if (responseToSend.bytesSent >= responseToSend.data.Length)
                {
                    responses[connectionIndex].Dequeue();
                    responsesCount--;
                }

                mf.Send(port);
                GameIsReady = false;
                lastFrame = mf;
            }
        }

        public void SendWebRequestFailedResponse(int connectionID)
        {
            // Make a dummy HTTP frame with data length 1, HTTP response error code
            byte[] data = new byte[4 + 4 + 1]; // 4 length, 4 response code, 1 data
            Array.Copy(BitConverter.GetBytes((int)5), 0, data, 0, 4);
            Array.Copy(BitConverter.GetBytes(WEB_REQUEST_FAILED_ERROR_CODE), 0, data, 4, 4);
            AddConnectionResponse((byte)connectionID, data);
        }

        public void SendWebSocketClosedResponse(int connectionID)
        {
            // Send a single WS frame with data length 1, error bit set in the text/binary byte flag
            byte[] data = new byte[4 + 1 + 1]; // 4 length, 1 flag byte, 1 data
            Array.Copy(BitConverter.GetBytes((int)2), 0, data, 0, 4);
            data[4] = 0x80; // High bit of the text/bin flag means the the connection was closed.
            AddConnectionResponse((byte)connectionID, data);
        }
    }
}
