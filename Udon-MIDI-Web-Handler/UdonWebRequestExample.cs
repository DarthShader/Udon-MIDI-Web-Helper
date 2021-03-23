using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using System;

public class UdonWebRequestExample : UdonSharpBehaviour
{
    public UdonMIDIWebHandler webManager;
    public int connectionID;
    public byte[] connectionData;
    public int responseCode;

    public InputField input;
    public InputField output;

    public override void Interact()
    {
        connectionID = webManager.WebRequestGet(input.text, this);
    }

    public void WebRequestGetCallback(/* int connectionID, byte[] connectionData, int responseCode */)
    {
        // Most web content is going to be in UTF8, but since System.Text.Encoding
        // isn't exposed to Udon and no one in their right mind is going to reimplement
        // that in Udon, a simple ASCII display will do for now.
        output.text = responseCode + " " + EncodingGetASCII(connectionData);
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
