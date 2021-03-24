using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;

public class UdonWebSocketExample : UdonSharpBehaviour
{
    public UdonMIDIWebHandler webManager;
    public int connectionID = -1;
    public byte[] connectionData;
    public bool messageIsText;

    public InputField urlField;
    public InputField messageField;
    public InputField output;

    public void UrlEntered()
    {
        if (connectionID != -1)
            webManager.WebSocketClose(connectionID);

        // This function is called from urlField's end editing event
        connectionID = webManager.WebSocketOpen(urlField.text, this);
        output.text = "";
    }

    public override void Interact()
    {
        if (connectionID != -1)
            webManager.WebSocketSendStringUnicode(connectionID, messageField.text);
    }

    public void WebSocketReceive(/* int connectionID, byte[] connectionData, bool messageIsText */)
    {
        output.text += EncodingGetUnicode(connectionData) + '\n';
    }

    public void WebSocketClosed(/* int connectionID */)
    {
        connectionID = -1;
    }

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
}
