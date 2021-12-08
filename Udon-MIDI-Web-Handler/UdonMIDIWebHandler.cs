using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using System;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class UdonMIDIWebHandler : UdonSharpBehaviour
{
    // Constants
    const int MAX_ACTIVE_CONNECTIONS = 256;
    const int MAX_USABLE_CONNECTIONS = 255;
    const int LOOPBACK_CONNECTION_ID = 255;
    const int MAX_USABLE_BYTES_PER_FRAME = 200;
    const float READY_TIMEOUT_SECONDS = 1.0f;
    const float PING_INTERVAL_SECONDS = 10f;
    const int PONG_TIMEOUT_FRAMES = 100;

    public const int CONNECTION_ID_TOO_MANY_ACTIVE_CONNECTIONS = -1;
    public const int CONNECTION_ID_OFFLINE = -2;
    public const int CONNECTION_ID_REQUEST_INVALID = -3;
    public const int RESPONSE_CODE_REQUEST_FAILED = 111;
    public const int RESPONSE_CODE_POST_ARGUMENTS_UNVERIFIABLE = 112;
    public const int RESPONSE_CODE_RATE_LIMITED = 113;
    public const int RESPONSE_CODE_DISCONNECTED = 114;

    // Connections management
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

    // Status information
    // These are supposed to be "read only" - not in the scuffed C# readonly keyword sense - but in the Get-only-accessor-property sense.
    // But since making an actual function accessor currently has a runtime performance cost in UdonSharp and isn't a compile time abstraction,
    // I'm going to leave it public until something changes @Merlin @Synergiance :) - Do C# properties carry the same overhead?
    [HideInInspector]
    public bool online;
    [HideInInspector]
    public int connectionsOpen;
    [HideInInspector]
    public int playersOnline;
    [HideInInspector]
    public int commandsSent;
    [HideInInspector]
    public int responsesReceived;
    [HideInInspector]
    public int bytesReceived;
    [HideInInspector]
    public int queuedResponsesCount;
    [HideInInspector]
    public int queuedBytesCount;

    // Update loop
    float secondsSinceLastReady;
    float secondsSinceLastPing = PING_INTERVAL_SECONDS;
    bool awaitingPong;
    int framesSinceAwaitingPong;

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

        // Connection 255 is always open, ping responses and avatar changes
        // are sent when they are detected in the output log
        connectionIsWebSocket[LOOPBACK_CONNECTION_ID] = false;
        connectionReturnsStrings[LOOPBACK_CONNECTION_ID] = true;

        // Reset the state of the helper
        Debug.Log("[Udon-MIDI-Web-Helper] RESET");
    }

    public void OnDestroy()
    {
        // Scene is being unloaded, make sure to close open WS connections.  Large
        // HTTP requests will still accumulate as unsent midi command blocks in the
        // helper program, but that shouldn't be a problem if the helper is reset upon
        // entering a new world with an instance of this behavior.
        for (int i=0; i<MAX_ACTIVE_CONNECTIONS; i++)
            if (connectionRequesters[i] != null && connectionIsWebSocket[i])
                _u_WebSocketClose(i);
    }

    void Update()
    {
        secondsSinceLastPing += Time.deltaTime;
        if (secondsSinceLastPing > PING_INTERVAL_SECONDS)
        {
            // Ping the helper program using a dedicated command, on a dedicated connection ID.
            // The loopback response from this has priority over all 255 other connections.
            // This is done on a dedicated connectionID to ensure it is received like all other
            // MIDI frames, and to make sure there's always a free, prioritized line for auxiliary data.
            Debug.Log("[Udon-MIDI-Web-Helper] PING");
            commandsSent++;
            secondsSinceLastPing = 0;
            awaitingPong = true;
        }

        if (awaitingPong)
        {
            // Measure time since ping response in number of frames, as the response should be 
            // nearly instantaneous.  VRChat may lag and drop connection for a while, but the
            // helper program never should.  If VRChat is running successfully and pong isn't
            // received, its safe to assume the connection is dead.
            if (++framesSinceAwaitingPong > PONG_TIMEOUT_FRAMES && online)
            {
                online = false;
                playersOnline--;
                _u_SendCallback("_u_OnUdonMIDIWebHandlerOnlineChanged");
                // If the handler goes offline, there is no hope of reconnecting until restart.
                // Cancel/close all open connections with a failure response code.
                for (int i=0; i<MAX_USABLE_CONNECTIONS; i++)
                    if (connectionRequesters[i] != null)
                    {
                        UdonSharpBehaviour usb = connectionRequesters[i];
                        usb.SetProgramVariable("connectionID", i);
                        if (connectionIsWebSocket[i])
                            usb.SendCustomEvent("_u_WebSocketClosed");
                        else
                        {
                            usb.SetProgramVariable("responseCode", RESPONSE_CODE_DISCONNECTED);
                            usb.SendCustomEvent("_u_WebRequestReceived");
                        }
                        connectionRequesters[i] = null;
                        connectionsOpen--;
                        _u_SendCallback("_u_OnUdonMIDIWebHandlerConnectionCountChanged");
                    }
            }
        }

        // VRChat's update order for Udon appears to go: Update(), LateUpdate(), MidiNoteX(), at least
        // with VRC Midi Listener after Udon Behaviour on the same gameobject.
        // Therefore, it should be safe to print a READY or ACK each frame without waiting a game tick.
        // (This whole protocol is built around VRChat crashing if more than 1 midi frame of data is recevied per game tick)
        // Hopefully this means the Log-MIDI communication is fast enough to send a new frame
        // before the game loops around again, ideally resulting in one MIDI frame every game tick.
        if (currentFrameOffset == usableBytesThisFrame)
        {
            Debug.Log("[Udon-MIDI-Web-Helper] ACK");
            currentFrameOffset = 0;
            secondsSinceLastReady = 0; // Don't send a ready, ACK doubles as a ready
        }

        secondsSinceLastReady += Time.deltaTime;
        if (secondsSinceLastReady > READY_TIMEOUT_SECONDS)
        {
            // Only send a RDY exactly READY_TIMEOUT_SECONDS after at least one connection was opened
            Debug.Log("[Udon-MIDI-Web-Helper] READY"); // Ready message lets the helper know its safe to send a new frame OR if a frame was dropped and never received
            secondsSinceLastReady = 0;
        }

    }

    public int _u_WebRequestGet(string uri, UdonSharpBehaviour usb, bool autoConvertToUTF16, bool returnUTF16String) 
    {
        int connectionID = _u_getAvailableConnectionID();
        if (connectionID >= 0)
        {
            connectionRequesters[connectionID] = usb;
            connectionIsWebSocket[connectionID] = false;
            connectionReturnsStrings[connectionID] = returnUTF16String;
            connectionsOpen++;
            _u_SendCallback("_u_OnUdonMIDIWebHandlerConnectionCountChanged");

            // Allow for full range of Basic Multilingual Plane UTF16 characters.
            // (queries will be properly percent encoded by the helper program)
            // Base64 encode the data because the output log doesn't
            // play nice with certain special characters.  Also so a new log line can't be spoofed.
            byte[] utf16Bytes = _u_EncodingUnicodeGetBytes(uri);
            Debug.Log("[Udon-MIDI-Web-Helper] GET " + connectionID + " " + Convert.ToBase64String(utf16Bytes) + (autoConvertToUTF16 ? " UTF16" : ""));
            commandsSent++;
        }
        return connectionID;
    }

    public int _u_WebRequestPost(string uri, UdonSharpBehaviour usb, bool autoConvertToUTF16, bool returnUTF16String, string[] keys, string[] values) 
    {
        if (keys != null && keys.Length != values.Length)
        {
            Debug.LogError("[UdonMIDIWebHandler] Incorrect number of key/value arguments for POST request!  Keys length: " + keys.Length + " Values length: " + values.Length);
            return CONNECTION_ID_REQUEST_INVALID;
        }

        int connectionID = _u_getAvailableConnectionID();
        if (connectionID >= 0)
        {
            connectionRequesters[connectionID] = usb;
            connectionIsWebSocket[connectionID] = false;
            connectionReturnsStrings[connectionID] = returnUTF16String;
            connectionsOpen++;
            _u_SendCallback("_u_OnUdonMIDIWebHandlerConnectionCountChanged");

            // Allow for full range of Basic Multilingual Plane UTF16 characters.
            // (queries will be properly percent encoded by the helper program)
            // Base64 encode the data because the output log doesn't
            // play nice with certain special characters.  Also so a new log line can't be spoofed.
            byte[] utf16Bytes = _u_EncodingUnicodeGetBytes(uri);

            string args = "";
            if (keys != null && values != null)
                for (int i=0; i<keys.Length; i++)
                {
                    string keyEncoded, valueEncoded;
                    if (keys[i] == "") keyEncoded = "="; // Empty string key or value in post? Sure why not
                    else keyEncoded = Convert.ToBase64String(_u_EncodingUnicodeGetBytes(keys[i]));
                    if (values[i] == "") valueEncoded = "=";
                    else valueEncoded = Convert.ToBase64String(_u_EncodingUnicodeGetBytes(values[i]));
                    args += " " + keyEncoded + " " + valueEncoded;
                }

            Debug.Log("[Udon-MIDI-Web-Helper] POST " + connectionID + " " + Convert.ToBase64String(utf16Bytes) + (autoConvertToUTF16 ? " UTF16" : "UTF8") + args);
            commandsSent++;
        }
        return connectionID;
    }

    int _u_getAvailableConnectionID()
    {
        if (!online)
            return CONNECTION_ID_OFFLINE;

        int connectionID = CONNECTION_ID_TOO_MANY_ACTIVE_CONNECTIONS;
        for (int i=0; i<MAX_USABLE_CONNECTIONS; i++)
            if (connectionRequesters[i] == null)
            {
                connectionID = i;
                break;
            }
        
        if (connectionID == CONNECTION_ID_TOO_MANY_ACTIVE_CONNECTIONS)
            Debug.LogError("[UdonMIDIWebHandler] Too many web connections active at once!");
        return connectionID;
    }

    public void _u_StoreLocalValue(string key, string value, bool valueIsPublic, bool global)
    {
        string k = Convert.ToBase64String(_u_EncodingUnicodeGetBytes(key));
        string v = Convert.ToBase64String(_u_EncodingUnicodeGetBytes(value));
        Debug.Log("[Udon-MIDI-Web-Helper] STORE " + k + " " + v + (valueIsPublic ? " public" : " private") + (global ? " global" : ""));
        commandsSent++;
    }

    public int _u_RetrieveLocalValue(UdonSharpBehaviour usb, string key, string worldID)
    {
        int connectionID = _u_getAvailableConnectionID();
        if (connectionID >= 0)
        {
            connectionRequesters[connectionID] = usb;
            connectionIsWebSocket[connectionID] = false;
            connectionReturnsStrings[connectionID] = true;
            connectionsOpen++;
            _u_SendCallback("_u_OnUdonMIDIWebHandlerConnectionCountChanged");
            byte[] utf16Bytes = _u_EncodingUnicodeGetBytes(key);
            byte[] worldIDBytes = _u_EncodingUnicodeGetBytes(worldID);
            Debug.Log("[Udon-MIDI-Web-Helper] RETRIEVE " + connectionID + " " + Convert.ToBase64String(utf16Bytes) + " " + Convert.ToBase64String(worldIDBytes));
            commandsSent++;
        }
        return connectionID;
    }

    public void _u_OpenWebPage(string uri) 
    {
        byte[] utf16Bytes = _u_EncodingUnicodeGetBytes(uri);
        Debug.Log("[Udon-MIDI-Web-Helper] OPENBROWSER "+ Convert.ToBase64String(utf16Bytes));
        commandsSent++;
    }

    public int _u_WebSocketOpen(string uri, UdonSharpBehaviour usb, bool autoConvertToUTF16, bool returnUTF16String)
    {
        int connectionID = _u_getAvailableConnectionID();
        if (connectionID >= 0)
        {
            connectionRequesters[connectionID] = usb;
            connectionIsWebSocket[connectionID] = true;
            connectionReturnsStrings[connectionID] = returnUTF16String;
            connectionsOpen++;
            _u_SendCallback("_u_OnUdonMIDIWebHandlerConnectionCountChanged");
            byte[] utf16Bytes = _u_EncodingUnicodeGetBytes(uri);
            Debug.Log("[Udon-MIDI-Web-Helper] WSOPEN " + connectionID + " " + Convert.ToBase64String(utf16Bytes) + (autoConvertToUTF16 ? " UTF16" : ""));
            commandsSent++;
        }
        return connectionID;
    }

    public void _u_WebSocketClose(int connectionID)
    {
        if (connectionID < 0 || connectionID >= MAX_USABLE_CONNECTIONS)
        {
            Debug.LogError("[UdonMIDIWebHandler] connectionID: Argument out of range");
            return;
        }

        if (connectionIsWebSocket[connectionID])
        {
            Debug.Log("[Udon-MIDI-Web-Helper] WSCLOSE " + connectionID);
            commandsSent++;

            // Clear existing data and wait for the full close midi response from the helper program.
            connectionData[currentID] = null;
            connectionDataOffsets[currentID] = 0;
            connectionDataChars[connectionID] = null;
            connectionDataCharsOffsets[connectionID] = 0;
        }
        else Debug.Log("[UdonMIDIWebHandler] Error: Provided connectionID isn't a WebSocket connection.");
    }

    public void _u_ClearConnection(int connectionID)
    {
        if (connectionID < 0 || connectionID >= MAX_USABLE_CONNECTIONS)
        {
            Debug.LogError("[UdonMIDIWebHandler] connectionID: Argument out of range");
            return;
        }

        // Reset connection data for connectionID in case a response is in the
        // middle of being sent.  The helper program also clears incomplete responses.
        connectionData[connectionID] = null;
        connectionDataOffsets[connectionID] = 0;
        connectionDataChars[connectionID] = null;
        connectionDataCharsOffsets[connectionID] = 0;
        Debug.Log("[Udon-MIDI-Web-Helper] CLEAR " + connectionID);
        commandsSent++;
    }

    public void _u_WebSocketSendStringASCII(int connectionID, string s)
    {
        _u_WebSocketSend(connectionID, _u_EncodingASCIIGetBytes(s), true, true, false);
    }

    public void _u_WebSocketSendStringUnicode(int connectionID, string s, bool autoConvertToUTF8)
    {
        _u_WebSocketSend(connectionID, _u_EncodingUnicodeGetBytes(s), true, true, autoConvertToUTF8);
    }

    public void _u_WebSocketSend(int connectionID, byte[] data, bool messageIsText, bool endOfMessage, bool autoConvertToUTF8)
    {
        // Other UdonBehaviors will have to convert their own strings to bytes,
        // becuase it is unknown what encoding the string may be in.  The same
        // goes for received messages.  Sending a text message using a specific
        // encoding may be critical for an application, so it can't be 
        // automatically done here.  Two helper functions are available, though.
        // It may also be beneficial for other behaviors to time slice convert
        // their strings if the data that needs to be sent hitches on the helper functions.
        if (connectionID < 0 || connectionID >= MAX_USABLE_CONNECTIONS)
        {
            Debug.LogError("[UdonMIDIWebHandler] connectionID: Argument out of range");
            return;
        }

        // Base64 encode the data because the output log doesn't play nice
        // with certain special characters.  Also so a new log line can't be spoofed.
        Debug.Log("[Udon-MIDI-Web-Helper] WSMESSAGE " + connectionID + (messageIsText ? " txt " : " bin ") 
            + Convert.ToBase64String(data) + (endOfMessage ? " true" : " false") + (autoConvertToUTF8 ? " UTF16" : ""));
        commandsSent++;
    }

    public override void MidiNoteOn(int channel, int number, int velocity)
    {
        _u_ReceiveDataCommand(0x0, channel, number, velocity);
    }

    public override void MidiNoteOff(int channel, int number, int velocity)
    {
        _u_ReceiveDataCommand(0x1, channel, number, velocity);
    }

    void _u_ReceiveDataCommand(int commandTypeBit, int channel, int number, int velocity)
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
            _u_ReceiveFrame();
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

    void _u_ReceiveFrame()
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
        int i;
        for (i = 0; i < usableBytesThisFrame; i++)
        {
            // Stop if the response buffer is full (frames MUST be sent in 190 byte chunks)
            if (connectionDataOffsets[currentID] == connectionData[currentID].Length)
                break;
            
            connectionData[currentID][connectionDataOffsets[currentID]++] = currentFrame[i];
        }
        bytesReceived += i;

        // Convert as many bytes to Unicode chars as possible.
        // Due to VRChat's limited throughput MIDI implementation, converting characters
        // here is an effective way to time slice an expensive operation rather than doing it
        // all when the full buffer has been received.
        if (connectionReturnsStrings[currentID] && (!connectionIsWebSocket[currentID] || (connectionData[currentID][0] & 0x1) == 0))
        {
            int responseHeaderLengthBytes = connectionIsWebSocket[currentID] ? 1 : 4;
            for (i=responseHeaderLengthBytes + connectionDataCharsOffsets[currentID] * 2; i<connectionDataOffsets[currentID]-1; i+= 2)
            {
                ushort charUTF16 = connectionData[currentID][i];
                charUTF16 |= (ushort)(connectionData[currentID][i+1] << 8);
                connectionDataChars[currentID][connectionDataCharsOffsets[currentID]++] = Convert.ToChar(charUTF16);
            }
        }

        if (connectionDataOffsets[currentID] == connectionData[currentID].Length)
            _u_ReceiveResponse();
    }

    void _u_ReceiveResponse()
    {
        responsesReceived++;

        // Requies convention public variable and event names
        UdonSharpBehaviour usb = connectionRequesters[currentID];
        if (connectionIsWebSocket[currentID])
        {
            // First byte is the text/binary flag, with highest bit indiciating
            // that this is a dummy 'connection closed' response.  Second highest
            // bit indicates a websocket opened message.
            if ((connectionData[currentID][0] & 0x40) == 0x40)
            {
                usb.SetProgramVariable("connectionID", currentID);
                usb.SendCustomEvent("_u_WebSocketOpened");
            }
            else if ((connectionData[currentID][0] & 0x80) == 0x80)
            {
                usb.SetProgramVariable("connectionID", currentID);
                usb.SendCustomEvent("_u_WebSocketClosed");
                connectionRequesters[currentID] = null;
                connectionsOpen--;
                _u_SendCallback("_u_OnUdonMIDIWebHandlerConnectionCountChanged");
            }
            else
            {
                usb.SetProgramVariable("connectionID", currentID);
                bool messageIsText = (connectionData[currentID][0] & 0x1) == 0x0;
                byte[] messsageData = new byte[connectionData[currentID].Length-1];
                Array.Copy(connectionData[currentID], 1, messsageData, 0, connectionData[currentID].Length-1);
                usb.SetProgramVariable("messageIsText", messageIsText);
                if (messageIsText && connectionReturnsStrings[currentID])
                    usb.SetProgramVariable("connectionString", new string(connectionDataChars[currentID]));
                else
                    usb.SetProgramVariable("connectionData", messsageData);
                usb.SendCustomEvent("_u_WebSocketReceive");
            }
        }
        else
        {
            // Non-websocket response
            // First 4 bytes of data are an int for HTTP response code or request error code
            int responseCode = _u_BitConverterToInt32(connectionData[currentID], 0);
            byte[] responseData = new byte[connectionData[currentID].Length-4];
            Array.Copy(connectionData[currentID], 4, responseData, 0, connectionData[currentID].Length-4);
            // Process loopback connection
            if (currentID == LOOPBACK_CONNECTION_ID)
            {
                string connectionString = new string(connectionDataChars[currentID]);
                // responseCode determines what kind of message it is, rather than typical HTTP 
                // or extra Udon handler response codes.
                if (responseCode == 0) // ping response
                {
                    if (online == false)
                    {
                        online = true;
                        playersOnline++;
                        _u_SendCallback("_u_OnUdonMIDIWebHandlerOnlineChanged");
                    }
                    awaitingPong = false;
                    framesSinceAwaitingPong = 0;
                    string[] split = connectionString.Split(' ');
                    queuedResponsesCount = Int32.Parse(split[0]);
                    queuedBytesCount = Int32.Parse(split[1]);
                }
                else if (responseCode == 1) // avatar change
                {
                    string[] split = connectionString.Split('\n');
                    _u_SendAvatarChangedCallback(split[0], split[1]);
                }
            }
            else
            {
                usb.SetProgramVariable("connectionID", currentID);
                usb.SetProgramVariable("responseCode", responseCode);
                if (connectionReturnsStrings[currentID])
                    usb.SetProgramVariable("connectionString", new string(connectionDataChars[currentID]));
                else
                    usb.SetProgramVariable("connectionData", responseData);
                usb.SendCustomEvent("_u_WebRequestReceived");
                connectionRequesters[currentID] = null;
                connectionsOpen--;
                _u_SendCallback("_u_OnUdonMIDIWebHandlerConnectionCountChanged");
            }
        }
        
        // Reset response arary
        connectionData[currentID] = null;
        connectionDataOffsets[currentID] = 0;
    }

    // SystemBitConverter.__ToInt32__SystemByteArray_SystemInt32__SystemInt32 is not exposed in Udon
    // Buffer.BlockCopy is also not whitelisted.
    int _u_BitConverterToInt32(byte[] data, int startIndex)
    {
        int result = data[startIndex++];
        result |= data[startIndex++] << 8;
        result |= data[startIndex++] << 16;
        result |= data[startIndex++] << 24;
        return result;
    }

    // SystemTextEncoding.__get_Unicode__SystemTextEncoding is not exposed in Udon
    byte[] _u_EncodingUnicodeGetBytes(string s)
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
    byte[] _u_EncodingASCIIGetBytes(string s)
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

    public float _u_GetProgress(int connectionID)
    {
        if (connectionData[connectionID] == null) 
            return 0f;
        else 
            return (float)connectionDataOffsets[connectionID] / (float)connectionData[connectionID].Length;
    }

    public void _u_BufferedIncrementPlayersOnline()
    {
        playersOnline++;
    }

    public void _u_IncrementPlayersOnline()
    {
        playersOnline++;
        // Changed from offline to partially online
        if (playersOnline == 1 && !online)
            _u_SendCallback("_u_OnUdonMIDIWebHandlerOnlineChanged");
    }

    public void _u_DecrementPlayersOnline()
    {
        playersOnline--;
        // Changed from partially online to offline
        if (playersOnline == 0 && !online)
            _u_SendCallback("_u_OnUdonMIDIWebHandlerOnlineChanged");
    }

    void _u_SendAvatarChangedCallback(string displayName, string avatarID)
    {
        _u_SetCallbackVariable("avatarChangeDisplayName", displayName);
        _u_SetCallbackVariable("avatarChangeAvatarID", avatarID);
        _u_SendCallback("_u_OnAvatarChanged");
    }

// Callbacks
// _u_OnUdonMIDIWebHandlerOnlineChanged: called when online status is changed (Offline, Brokered, Online)
// _u_OnAvatarChanged (args: avatarChangeDisplayName, avatarChangeAvatarID)
    // requires --log-debug-levels=NetworkTransport
// _u_OnUdonMIDIWebHandlerConnectionCountChanged: called when a connection is opened or closed
#region Callback Receivers
// Shamelessly taken from USharpVideoPlayer.  Thanks Merlin.
// Edited to be copy/pastable between any UdonSharp behavior
        UdonSharpBehaviour[] _registeredCallbackReceivers;
        public void _u_RegisterCallbackReceiver(UdonSharpBehaviour callbackReceiver)
        {
            if (!Utilities.IsValid(callbackReceiver))
                return;
            if (_registeredCallbackReceivers == null)
                _registeredCallbackReceivers = new UdonSharpBehaviour[0];
            foreach (UdonSharpBehaviour currReceiver in _registeredCallbackReceivers)
                if (callbackReceiver == currReceiver)
                    return;
            UdonSharpBehaviour[] newControlHandlers = new UdonSharpBehaviour[_registeredCallbackReceivers.Length + 1];
            _registeredCallbackReceivers.CopyTo(newControlHandlers, 0);
            _registeredCallbackReceivers = newControlHandlers;
            _registeredCallbackReceivers[_registeredCallbackReceivers.Length - 1] = callbackReceiver;
        }

        public void _u_UnregisterCallbackReceiver(UdonSharpBehaviour callbackReceiver)
        {
            if (!Utilities.IsValid(callbackReceiver))
                return;
            if (_registeredCallbackReceivers == null)
                _registeredCallbackReceivers = new UdonSharpBehaviour[0];
            int callbackReceiverCount = _registeredCallbackReceivers.Length;
            for (int i = 0; i < callbackReceiverCount; ++i)
            {
                UdonSharpBehaviour currHandler = _registeredCallbackReceivers[i];
                if (callbackReceiver == currHandler)
                {
                    UdonSharpBehaviour[] newCallbackReceivers = new UdonSharpBehaviour[callbackReceiverCount - 1];
                    for (int j = 0; j < i; ++j)
                        newCallbackReceivers[j] = _registeredCallbackReceivers[j];
                    for (int j = i + 1; j < callbackReceiverCount; ++j)
                        newCallbackReceivers[j - 1] = _registeredCallbackReceivers[j];
                    _registeredCallbackReceivers = newCallbackReceivers;
                    return;
                }
            }
        }

        void _u_SendCallback(string callbackName)
        {
            if (_registeredCallbackReceivers == null) 
                return;
            foreach (UdonSharpBehaviour callbackReceiver in _registeredCallbackReceivers)
                if (Utilities.IsValid(callbackReceiver))
                    callbackReceiver.SendCustomEvent(callbackName);
        }

        void _u_SetCallbackVariable(string symbolName, object value)
        {
            if (_registeredCallbackReceivers == null) 
                return;
            foreach (UdonSharpBehaviour callbackReceiver in _registeredCallbackReceivers)
                if (Utilities.IsValid(callbackReceiver))
                    callbackReceiver.SetProgramVariable(symbolName, value);
        }
#endregion
}