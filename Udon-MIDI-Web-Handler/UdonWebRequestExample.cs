using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using System;

public class UdonWebRequestExample : UdonSharpBehaviour
{
    public UdonMIDIWebHandler webManager;
    int connectionID;
    byte[] connectionData;
    int responseCode;

    public InputField input;
    public InputField output;

    public override void Interact()
    {
        connectionID = webManager.WebRequestGet(input.text, this, true);
    }

    public void WebRequestGetCallback(/* int connectionID, byte[] connectionData, int responseCode */)
    {
        // EncodingGetUnicode can freeze the game for a second, it would be better
        // to time slice the byte array to string conversion for very large results.
        output.text = responseCode + " " + EncodingGetUnicode(connectionData);
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
