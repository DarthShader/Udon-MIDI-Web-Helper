using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using System;

public class UdonWebRequestExample : UdonSharpBehaviour
{
    // Link this behaviour to the centralized web handler
    public UdonMIDIWebHandler webManager;
    // Create variables to act as arguments for WebRequestGetCallback()
    int connectionID;
    byte[] connectionData;
    string connectionString;
    string unicodeData;
    int responseCode;

    // Simple address bar
    public InputField input;
    // Simple web page display
    public InputField output;

    public override void Interact()
    {
        // WebRequestGet() arguments:
        // string uri: The URI of the webpage to retrieve (must begin with http:// or https://)
        // UdonSharpBehaviour usb: Takes a reference of the behaviour to call WebRequestGetCallback() on
        // bool autoConvertToUTF16: Option to convert response data from UTF8 to UTF16 automatically to properly display in UnityUI
        // bool returnUTF16String: Option to efficiently convert response data to a string before calling WebRequestGetCallback()
        connectionID = webManager.WebRequestGet(input.text, this, true, true);
        // The return value of WebRequestGet() is a 0-255 value that can be used to track what web requests this behaviour has active.
        // A returned value of -1 means the request could not be made; there are already too many active connections.
    }

    public void WebRequestGetCallback(/* int connectionID, byte[] connectionData, string connectionString, int responseCode */)
    {
        // This is called when the web request has been fully received by the web handler.
        // connectionID: the ID of the web request being returned
        // connectionData: raw response data if WebRequestGet()'s returnUTF16String argument was false
        // connectionString: Unicode response string if WebRequestGet()'s returnUTF16String argument was true
        // responseCode: HTTP response code for web request.  Code 111 if there was a problem making the request.
        output.text = responseCode + " " + connectionString;
    }
}
