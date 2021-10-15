using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using System;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class UdonWebRequestExample : UdonSharpBehaviour
{
    // Link this behaviour to the centralized web handler
    public UdonMIDIWebHandler webManager;
    // Create variables to act as arguments for WebRequestReceived()
    int connectionID = -1;
    byte[] connectionData;
    string connectionString;
    string unicodeData;
    int responseCode;

    // Simple address bar
    public InputField input;
    // Simple web page display
    public InputField output;
    // Fake progress bar
    public Scrollbar progressBar;

    public override void Interact()
    {
        // _u_WebRequestGet() arguments:
        // string uri: The URI of the webpage to retrieve (must begin with http:// or https://)
        // UdonSharpBehaviour usb: Takes a reference of the behaviour to call WebRequestReceived() on
        // bool autoConvertToUTF16: Option to convert response data from UTF8 to UTF16 automatically to properly display in UnityUI
        // bool returnUTF16String: Option to efficiently convert response data to a string before calling WebRequestReceived()
        connectionID = webManager._u_WebRequestGet(input.text, this, true, true);
        // The return value of _u_WebRequestGet() is a 0-255 value that can be used to track what web requests this behaviour has active.
        // A returned value of -1 means the request could not be made; there are already too many active connections.
    }

    public void Update()
    {
        if (connectionID != -1)
            progressBar.size = webManager._u_GetProgress(connectionID);
        else progressBar.size = 0;
    }

    public void _u_WebRequestReceived(/* int connectionID, byte[] connectionData, string connectionString, int responseCode */)
    {
        connectionID = -1;
        // This is called when the web request has been fully received by the web handler.
        // connectionID: the ID of the web request being returned
        // connectionData: raw response data if _u_WebRequestGet()'s returnUTF16String argument was false
        // connectionString: Unicode response string if _u_WebRequestGet()'s returnUTF16String argument was true
        // responseCode: HTTP response code for web request.  Code 111 if there was a problem making the request.
        output.text = responseCode + " " + connectionString;
    }
}
