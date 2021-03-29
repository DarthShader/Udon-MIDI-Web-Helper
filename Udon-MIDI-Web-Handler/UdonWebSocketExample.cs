using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;

public class UdonWebSocketExample : UdonSharpBehaviour
{
    // Link this behaviour to the centralized web handler
    public UdonMIDIWebHandler webManager;
    // Create variables to act as arguments for WebRequestGetCallback()
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

    public void UrlEntered()
    {
        // This function is called from urlField's end editing event

        // Close existing connection if one is already open
        if (connectionID != -1)
            webManager.WebSocketClose(connectionID);

        // WebSocketOpen() arguments:
        // string uri: The URI of the webpage to retrieve (must begin with ws:// or wss://)
        // UdonSharpBehaviour usb: Takes a reference of the behaviour to call WebSocketReceive() and WebSocketClosed() on
        // bool autoConvertToUTF16: Option to convert received text messages from UTF8 to UTF16 automatically to properly display in UnityUI
        // bool returnUTF16String: Option to efficiently convert received text messages to a string before calling WebSocketReceive()
        connectionID = webManager.WebSocketOpen(urlField.text, this, true, true);
        if (connectionID == -1)
            output.text = "Failed to open WebSocket\n";
        else
            output.text = "WebSocket Opened\n";
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
        // bool autoConvertToUTF8: Option to automatically convert provided message data from Unicode to UTF8 before sending

        // WebSocketSendStringASCII()
        // int connectionID: The ID of the active WebSocket connection to send data on
        // string s: data to send

        // WebSocketSendStringUnicode()
        // int connectionID: The ID of the active WebSocket connection to send data on
        // string s: data to send
        // bool autoConvertToUTF8: Option to automatically convert provided message data from Unicode to UTF8 before sending

        if (connectionID != -1)
            webManager.WebSocketSendStringUnicode(connectionID, messageField.text, true);
    }

    public void WebSocketReceive(/* int connectionID, byte[] connectionData, string connectionString, bool messageIsText */)
    {
        output.text += connectionString + '\n';
    }

    public void WebSocketClosed(/* int connectionID */)
    {
        output.text += "WebSocket Closed";
        connectionID = -1;
    }
}
