using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class UdonWebSocketExample : UdonSharpBehaviour
{
    // Link this behaviour to the centralized web handler
    public UdonMIDIWebHandler webManager;
    // Create variables to act as arguments for WebSocketReceive() and other callbacks
    int connectionID = -1;
    byte[] connectionData;
    string connectionString;
    string unicodeData;
    bool messageIsText;

    // Simple address bar (needs to call UrlEntered() onEditEnd)
    public InputField urlField;
    // Simple message field
    public InputField messageField;
    // Simply log-style display
    public InputField output;

    public void _u_UrlEntered()
    {
        // This function is called from urlField's end editing event

        // Close existing connection if one is already open
        if (connectionID != -1)
            webManager._u_WebSocketClose(connectionID);

        // WebSocketOpen() arguments:
        // string uri: The URI of the webpage to retrieve (must begin with ws:// or wss://)
        // UdonSharpBehaviour usb: Takes a reference of the behaviour to call WebSocketReceive() and WebSocketClosed() on
        // bool autoConvertToUTF16: Option to convert received text messages from UTF8 to UTF16 automatically to properly display in UnityUI
        // bool returnUTF16String: Option to efficiently convert received text messages to a string before calling WebSocketReceive()
        connectionID = webManager._u_WebSocketOpen(urlField.text, this, true, true);
        if (connectionID == -1)
            output.text = "Too many active connections!\n";
    }

    public override void Interact()
    {
        // Websocket binary and text messages can be sent with WebSocketSend().  Two helper functions 
        // WebSocketSendStringASCII() and WebSocketSendStringUnicode() can automatically parse and send
        // string based messages.

        // WebSocketSend()
        // int connectionID: The ID of the active WebSocket connection to send data on
        // byte[] data: The data to send
        // bool messageIsText: Flag to mark message data as text vs. binary
        // bool endOfMessage: Flag to mark the message as complete/incomplete if data is too large too be sent in one message
        // bool autoConvertToUTF8: Option to automatically convert provided message data from Unicode to UTF8 before sending

        // WebSocketSendStringASCII()
        // int connectionID: The ID of the active WebSocket connection to send data on
        // string s: data to send

        // WebSocketSendStringUnicode()
        // int connectionID: The ID of the active WebSocket connection to send data on
        // string s: data to send
        // bool autoConvertToUTF8: Option to automatically convert provided message data from Unicode to UTF8 before sending

        if (connectionID != -1)
            webManager._u_WebSocketSendStringUnicode(connectionID, messageField.text, true);
    }

    public void _u_WebSocketReceive(/* int connectionID, byte[] connectionData, string connectionString, bool messageIsText */)
    {
        output.text += connectionString + '\n';
    }

    public void _u_WebSocketOpened(/* int connectionID */)
    {
        output.text = "WebSocket Opened\n";
    }

    public void _u_WebSocketClosed(/* int connectionID */)
    {
        output.text += "WebSocket Closed";
        connectionID = -1;
    }
}
