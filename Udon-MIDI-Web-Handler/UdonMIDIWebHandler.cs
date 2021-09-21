using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using System;

public class UdonMIDIWebHandler : UdonSharpBehaviour
{
    // Constants
    const int MAX_ACTIVE_CONNECTIONS = 256;
    const int MAX_USABLE_BYTES_PER_FRAME = 200;
    const float READY_TIMEOUT_SECONDS = 1.0f;

    // Connections management
    int connectionsOpen;
    byte[][] connectionData;
    int[] connectionDataOffsets;
    char[][] connectionDataChars;
    int[] connectionDataCharsOffsets;
    bool[] connectionReturnsStrings;
    UdonSharpBehaviour[] connectionRequesters;
    bool[] connectionIsWebSocket;

    // Current frame
    byte[] currentFrame;
    int currentFrameOffset;
    int currentID;
    bool flipFlop;
    bool ignoreThisFrame;
    int usableBytesThisFrame = MAX_USABLE_BYTES_PER_FRAME; // Some frames are a full 200 bytes, some frames have the 4 byte response length int at the start
    int responseLengthState;
    int responseLengthBytes;
    int utilBitsState;
    int utilBitsByte;
    int commandTypePackedState;
    int commandTypePackedByte;

    // Update loop
    float secondsSinceLastReady;

    void Start()
    {
        connectionData = new byte[MAX_ACTIVE_CONNECTIONS][];
        connectionDataOffsets = new int[MAX_ACTIVE_CONNECTIONS];
        connectionDataChars = new char[MAX_ACTIVE_CONNECTIONS][];
        connectionDataCharsOffsets = new int[MAX_ACTIVE_CONNECTIONS];
        connectionRequesters = new UdonSharpBehaviour[MAX_ACTIVE_CONNECTIONS];
        connectionIsWebSocket = new bool[MAX_ACTIVE_CONNECTIONS];
        connectionReturnsStrings = new bool[MAX_ACTIVE_CONNECTIONS];
        currentFrame = new byte[MAX_USABLE_BYTES_PER_FRAME];

        // Reset the state of the helper
        Debug.Log("[Udon-MIDI-Web-Helper] RST");
    }

    public void OnDestroy()
    {
        // Scene is being unloaded, make sure to close open WS connections.  Large
        // HTTP requests will still accumulate as unsent midi command blocks in the
        // helper program, but that shouldn't be a problem if the helper is reset upon
        // entering a new world with an instance of this behavior.
        for (int i=0; i<MAX_ACTIVE_CONNECTIONS; i++)
            if (connectionRequesters[i] != null && connectionIsWebSocket[i])
                WebSocketClose(i);
    }

    void Update()
    {
        // VRChat's update order for Udon appears to go: Update(), LateUpdate(), MidiNoteX(), at least
        // with VRC Midi Listener after Udon Behaviour on the same gameobject.
        // Therefore, it is safe to print a RDY or ACK in Update() without waiting a game tick.
        // Hopefully this means the Log-MIDI communication is fast enough to send a new frame
        // before the game tick expires, ideally resulting in one MIDI frame every game tick.
        if (currentFrameOffset == usableBytesThisFrame)
        {
            Debug.Log("[Udon-MIDI-Web-Helper] ACK");
            currentFrameOffset = 0;
            secondsSinceLastReady = 0; // Don't send a ready, ACK doubles as a ready
        }

        if (connectionsOpen > 0)
        {
            secondsSinceLastReady += Time.deltaTime;
            if (secondsSinceLastReady > READY_TIMEOUT_SECONDS)
            {
                // Only send a RDY exactly READY_TIMEOUT_SECONDS after at least one connection was opened
                Debug.Log("[Udon-MIDI-Web-Helper] RDY"); // Ready message lets the helper know its safe to send a new frame OR if a frame was dropped and never received
                secondsSinceLastReady = 0;
            }
        }
    }

    public int WebRequestGet(string uri, UdonSharpBehaviour usb, bool autoConvertToUTF16, bool returnUTF16String) 
    {
        int connectionID = getAvailableConnectionID();
        if (connectionID != -1)
        {
            connectionRequesters[connectionID] = usb;
            connectionIsWebSocket[connectionID] = false;
            connectionReturnsStrings[connectionID] = returnUTF16String;
            connectionsOpen++;

            // Allow for full range of Basic Multilingual Plane UTF16 characters.
            // (queries will be properly percent encoded by the helper program)
            // Base64 encode the data because the output log doesn't
            // play nice with certain special characters.  Also so a new log line can't be spoofed.
            byte[] utf16Bytes = EncodingUnicodeGetBytes(uri);
            Debug.Log("[Udon-MIDI-Web-Helper] GET " + connectionID + " " + Convert.ToBase64String(utf16Bytes) + (autoConvertToUTF16 ? " UTF16" : ""));
        }
        return connectionID;
    }

    int getAvailableConnectionID()
    {
        int connectionID = -1;
        for (int i=0; i<MAX_ACTIVE_CONNECTIONS; i++)
            if (connectionData[i] == null)
            {
                connectionID = i;
                break;
            }
        
        if (connectionID == -1)
            Debug.LogError("[UdonMIDIWebHandler] Too many web connections active at once!");
        return connectionID;
    }

    public int WebSocketOpen(string uri, UdonSharpBehaviour usb, bool autoConvertToUTF16, bool returnUTF16String)
    {
        int connectionID = getAvailableConnectionID();
        if (connectionID != -1)
        {
            connectionRequesters[connectionID] = usb;
            connectionIsWebSocket[connectionID] = true;
            connectionReturnsStrings[connectionID] = returnUTF16String;
            connectionsOpen++;
            byte[] utf16Bytes = EncodingUnicodeGetBytes(uri);
            Debug.Log("[Udon-MIDI-Web-Helper] WSO " + connectionID + " " + Convert.ToBase64String(utf16Bytes) + (autoConvertToUTF16 ? " UTF16" : ""));
        }
        return connectionID;
    }

    public void WebSocketClose(int connectionID)
    {
        if (connectionID < 0 || connectionID >= MAX_ACTIVE_CONNECTIONS)
        {
            Debug.LogError("[UdonMIDIWebHandler] connectionID: Argument out of range");
            return;
        }

        if (connectionIsWebSocket[connectionID])
        {
            Debug.Log("[Udon-MIDI-Web-Helper] WSC " + connectionID);

            // Don't close connection immediately, there could be some queued data for this connection.
            // Wait for an official 'connection close' MIDI command
            //connectionData[currentID] = null;
            //connectionDataOffsets[currentID] = 0;
            //connectionRequesters[currentID] = null;
            //connectionsOpen--;
        }
    }

    public void WebSocketSendStringASCII(int connectionID, string s)
    {
        WebSocketSend(connectionID, EncodingASCIIGetBytes(s), true, true, false);
    }

    public void WebSocketSendStringUnicode(int connectionID, string s, bool autoConvertToUTF8)
    {
        WebSocketSend(connectionID, EncodingUnicodeGetBytes(s), true, true, autoConvertToUTF8);
    }

    public void WebSocketSend(int connectionID, byte[] data, bool messageIsText, bool endOfMessage, bool autoConvertToUTF8)
    {
        // Other UdonBehaviors will have to convert their own strings to bytes,
        // becuase it is unknown what encoding the string may be in.  The same
        // goes for received messages.  Sending a text message using a specific
        // encoding may be critical for an application, so it can't be 
        // automatically done here.  Two helper functions are available, though.
        // It may also be beneficial for other behaviors to time slice convert
        // their strings if the data that needs to be sent hitches on the helper functions.
        if (connectionID < 0 || connectionID >= MAX_ACTIVE_CONNECTIONS)
        {
            Debug.LogError("[UdonMIDIWebHandler] connectionID: Argument out of range");
            return;
        }

        // Base64 encode the data because the output log doesn't play nice
        // with certain special characters.  Also so a new log line can't be spoofed.
        Debug.Log("[Udon-MIDI-Web-Helper] WSM " + connectionID + (messageIsText ? " txt " : " bin ") 
            + Convert.ToBase64String(data) + (endOfMessage ? " true" : " false") + (autoConvertToUTF8 ? " UTF16" : ""));
    }

    public override void MidiNoteOn(int channel, int number, int velocity)
    {
        ReceiveDataCommand(0x0, channel, number, velocity);
    }

    public override void MidiNoteOff(int channel, int number, int velocity)
    {
        ReceiveDataCommand(0x1, channel, number, velocity);
    }

    void ReceiveDataCommand(int commandTypeBit, int channel, int number, int velocity)
    {
        if (ignoreThisFrame) return;

        byte byteA = (byte)(((channel & 0x1) << 7) | number);
        byte byteB = (byte)(((channel & 0x2) << 6) | velocity);
        int channelHighBits = (channel & 0xC) >> 2;

        // The first 4 commands of the first frame of a response store a 4-byte int for the response's full length.
        // The bytes received from this midi command are either stored normally or used to piece together that int.
        switch (responseLengthState)
        {
            case 0: // Not looking for response length bytes
                currentFrame[currentFrameOffset++] = byteA;
                currentFrame[currentFrameOffset++] = byteB;
                break;
            case 1: // The middle two bytes of response length; the first was received by a ControlChange command
                responseLengthBytes |= (int)byteA << 8;
                responseLengthBytes |= (int)byteB << 16;
                responseLengthState++;
                break;
            case 2: // Last byte of the response, plus a normal data byte
                responseLengthBytes |= (int)byteA << 24;
                currentFrame[currentFrameOffset++] = byteB;
                responseLengthState = 0;
                break;
        }
        
        // Every 4 data commands, an extra byte can be decoded from the channel's high two bits
        switch (utilBitsState)
        {
            case 0:
                utilBitsByte = channelHighBits;
                break;
            case 1:
                utilBitsByte |= channelHighBits << 2;
                break;
            case 2:
                utilBitsByte |= channelHighBits << 4;
                break;
            case 3:
                utilBitsByte |= channelHighBits << 6;
                utilBitsState = -1; // incremented back to zero
                currentFrame[currentFrameOffset++] = (byte)utilBitsByte;
                break;
        }
        utilBitsState++;

        // Every 8 data commands, an extra byte can be decoded from the sequential commandTypeBits
        commandTypePackedByte |= commandTypeBit << commandTypePackedState++;
        if (commandTypePackedState == 8)
        {
            currentFrame[currentFrameOffset++] = (byte)commandTypePackedByte;
            commandTypePackedState = 0;
            commandTypePackedByte = 0;
        }

        // Check for complete midi frame
        if (currentFrameOffset == usableBytesThisFrame)
            ReceiveFrame();
    }

    public override void MidiControlChange(int channel, int number, int velocity)
    {
        // Check for freak accidents
        if (flipFlop != ((channel & 0x4) == 0x0))
        {
            // Race condition - frame mistakenly retransmitted.
            // Ignore all MIDI commands until the next header is received.
            ignoreThisFrame = true;
            return;
        }
        else if (currentFrameOffset != 0)
        {
            // Next frame was transmitted without the previous being ACK'd
            // No idea what causes this
            Debug.LogError("[UdonMIDIWebHandler] Freak accident! Header frame appeared without an ACK or RDY");
            ignoreThisFrame = true;
            return;
        }
        flipFlop = !flipFlop;
        ignoreThisFrame = false;

        // The first of 85 commands that are guaranteed to be received within this game tick.
        // Instantiate new frame buffer, and read the connectionID.
        currentID = ((channel & 0x1) << 7) | number;
        byte byteB = (byte)(((channel & 0x2) << 6) | velocity);

        // If this is the first frame for a response, it means the next two
        // MidiNoteOn/MidiNoteOff events' main bytes will contain the length of the response.
        if (connectionData[currentID] == null)
        {
            usableBytesThisFrame = MAX_USABLE_BYTES_PER_FRAME - 4; // ignore the response length int
            responseLengthState = 1;
            responseLengthBytes = (int)byteB;
        }
        else 
        {
            usableBytesThisFrame = MAX_USABLE_BYTES_PER_FRAME;
            currentFrame[currentFrameOffset++] = byteB;
        }

        commandTypePackedState = 0;
        commandTypePackedByte = 0;
    }

    void ReceiveFrame()
    {
        // If this is the first frame of a response, use the first 4 bytes to allocate a buffer for it
        if (connectionData[currentID] == null)
        {
            connectionData[currentID] = new byte[responseLengthBytes];
            connectionDataOffsets[currentID] = 0;
            if (connectionReturnsStrings[currentID])
            {
                // WebSocket responses have one byte header, HTTP requests have 4 byte responseCode
                int responseHeaderLengthBytes = connectionIsWebSocket[currentID] ? 1 : 4;
                // This assumes the response data to convert is Unicode bytes
                connectionDataChars[currentID] = new char[(responseLengthBytes-responseHeaderLengthBytes) / 2];
                connectionDataCharsOffsets[currentID] = 0;
            }
        }

        // Fill response with current frame data
        for (int i = 0; i < usableBytesThisFrame; i++)
        {
            // Stop if the response buffer is full (frames MUST be sent in 190 byte chunks)
            if (connectionDataOffsets[currentID] == connectionData[currentID].Length)
                break;
            
            connectionData[currentID][connectionDataOffsets[currentID]++] = currentFrame[i];
        }

        // Convert as many bytes to Unicode chars as possible.
        // Due to VRChat's limited throughput MIDI implementation, converting characters
        // here is an effective way to time slice an expensive operation rather than doing it
        // all when the full buffer has been received.
        if (connectionReturnsStrings[currentID] && (!connectionIsWebSocket[currentID] || (connectionData[currentID][0] & 0x1) == 0))
        {
            int responseHeaderLengthBytes = connectionIsWebSocket[currentID] ? 1 : 4;
            for (int i=responseHeaderLengthBytes + connectionDataCharsOffsets[currentID] * 2; i<connectionDataOffsets[currentID]-1; i+= 2)
            {
                ushort charUTF16 = connectionData[currentID][i];
                charUTF16 |= (ushort)(connectionData[currentID][i+1] << 8);
                connectionDataChars[currentID][connectionDataCharsOffsets[currentID]++] = Convert.ToChar(charUTF16);
            }
        }

        if (connectionDataOffsets[currentID] == connectionData[currentID].Length)
            ReceiveResponse();
    }

    void ReceiveResponse()
    {
        // Requies convention public variable and event names
        UdonSharpBehaviour usb = connectionRequesters[currentID];
        usb.SetProgramVariable("connectionID", currentID);
        if (connectionIsWebSocket[currentID])
        {
            // First byte is the text/binary flag, with highest bit indiciating
            // that this is a dummy 'connection closed' response.
            bool connectionClosedResponse = (connectionData[currentID][0] & 0x80) == 0x80;
            bool connectionOpenedResponse = (connectionData[currentID][0] & 0x40) == 0x40;
            if (connectionOpenedResponse)
                usb.SendCustomEvent("WebSocketOpened");
            else if (connectionClosedResponse)
            {
                usb.SendCustomEvent("WebSocketClosed");
                connectionRequesters[currentID] = null;
                connectionsOpen--;
            }
            else
            {
                bool messageIsText = (connectionData[currentID][0] & 0x1) == 0x0;
                byte[] messsageData = new byte[connectionData[currentID].Length-1];
                Array.Copy(connectionData[currentID], 1, messsageData, 0, connectionData[currentID].Length-1);
                usb.SetProgramVariable("messageIsText", messageIsText);
                if (messageIsText && connectionReturnsStrings[currentID])
                    usb.SetProgramVariable("connectionString", new string(connectionDataChars[currentID]));
                else
                    usb.SetProgramVariable("connectionData", messsageData);
                usb.SendCustomEvent("WebSocketReceive");
            }
        }
        else
        {
            // First 4 bytes of data are an int for HTTP response code or request error code
            int responseCode = BitConverterToInt32(connectionData[currentID], 0);
            byte[] responseData = new byte[connectionData[currentID].Length-4];
            Array.Copy(connectionData[currentID], 4, responseData, 0, connectionData[currentID].Length-4);

            usb.SetProgramVariable("responseCode", responseCode);
            if (connectionReturnsStrings[currentID])
                usb.SetProgramVariable("connectionString", new string(connectionDataChars[currentID]));
            else
                usb.SetProgramVariable("connectionData", responseData);
            usb.SendCustomEvent("WebRequestReceived");
            connectionRequesters[currentID] = null;
            connectionsOpen--;
        }
        
        // Reset response arary
        connectionData[currentID] = null;
        connectionDataOffsets[currentID] = 0;
    }

    // SystemBitConverter.__ToInt32__SystemByteArray_SystemInt32__SystemInt32 is not exposed in Udon
    int BitConverterToInt32(byte[] data, int startIndex)
    {
        int result = data[startIndex++];
        result |= data[startIndex++] << 8;
        result |= data[startIndex++] << 16;
        result |= data[startIndex++] << 24;
        return result;
    }

    // SystemTextEncoding.__get_Unicode__SystemTextEncoding is not exposed in Udon
    byte[] EncodingUnicodeGetBytes(string s)
    {
        byte[] data = new byte[s.Length * 2];
        int offset = 0;
        for (int i = 0; i < s.Length; i++)
        {
            ushort charUTF16 = Convert.ToUInt16(s[i]);
            data[offset++] = (byte)(charUTF16 & 0xFF);
            data[offset++] = (byte)((charUTF16 & 0xFF00) >> 8);
        }
        return data;
    }

    // SystemTextEncoding.__get_ASCII__SystemTextEncoding is not exposed in Udon
    byte[] EncodingASCIIGetBytes(string s)
    {
        byte[] data = new byte[s.Length];
        for (int i=0; i<s.Length; i++)
            data[i] = (byte)s[i];
        return data;
    }

    /*
    // SystemTextEncoding.__get_ASCII__SystemTextEncoding is not exposed in Udon
    string EncodingGetASCII(byte[] bytes)
    {
        char[] chars = new char[bytes.Length];
        for (int i=0; i<chars.Length; i++)
            chars[i] = (char)bytes[i];
        return new string(chars);
    }

    // SystemTextEncoding.__get_Unicode__SystemTextEncoding is not exposed in Udon
    string EncodingGetUnicode(byte[] bytes)
    {
        char[] chars = new char[bytes.Length / 2];
        int offset = 0;
        for (int i=0; i<bytes.Length; i+=2)
        {
            ushort charUTF16 = bytes[i];
            charUTF16 |= (ushort)(bytes[i+1] << 8);
            chars[offset++] = Convert.ToChar(charUTF16);
        }
        return new string(chars);
    }
    */
}
