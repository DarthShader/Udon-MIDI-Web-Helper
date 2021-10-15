
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class PostRequestExample : UdonSharpBehaviour
{
    // Link this behaviour to the centralized web handler
    public UdonMIDIWebHandler webManager;
    // Create variables to act as arguments for WebRequestReceived()
    int connectionID = -1;
    byte[] connectionData;
    string connectionString;
    string unicodeData;
    int responseCode;
    
    public string urlToRequest;

    public override void Interact()
    {
        // _u_WebRequestPost() arguments:
        // string uri: The URI of the webpage to retrieve (must begin with http:// or https://)
        // UdonSharpBehaviour usb: Takes a reference of the behaviour to call WebRequestReceived() on
        // bool autoConvertToUTF16: Option to convert response data from UTF8 to UTF16 automatically to properly display in UnityUI
        // bool returnUTF16String: Option to efficiently convert response data to a string before calling WebRequestReceived()
        // string[] keys: POST request argument keys
        // string[] value: POST request argument values
        string[] keys = new string[5];
        string[] values = new string[5];
        // Each of these represents a reserved argument key name for user information that can be verified by the helper program.
        // No matter what value is provided, the associated value argument will be overwritten before making the POST request.
         // World ID of the current world
        keys[0] = "q0vB-6zRlh0";
         // User ID of the current user: requires launching the game with "--log-debug-levels=API".
         // If a POST request is made that uses this argument and the user doesn't have that log level enabled, an empty response with code 112 is returned.
        keys[1] = "5GZGUM6j9tQ";
        // Per-hostname generated key for the URL being used.  These keys are stored in a local .keys file.  As long as the helper program's data files are
        // kept in tact, this argument can be used to authenticate users on a remote web server.
        // Requires extended logging because if you don't have user IDs for authentication, you shouldn't also be using these keys.  Code 112 if you attempt a request without logging.
        // There is currently a hard limit of 100 unique hostnames per world ID so the .keys file cannot be spammed with new entries.  Code 112 if you try to pass the hostname limit.
        keys[2] = "qtkmKZtltyI";
        // Display name of the current user.  Useful for having other users corroborate information with the web server about what other users are in an instance.
        // This is only necessary because user IDs cannot be obtained by Udon.  This does not require extended logging.
        keys[3] = "7yzNonZ5up8";
        // Instance ID of the current world.  Although this might seem like an invasion of privacy, it's really not and could allow for some cool cross-instance creative
        // possibilities.  The connection nonce for the world is not exposed, and the instance owner is not exposed.  Even if this wasn't provided, worlds could still make
        // web requests that report what users are in the instnace at any given time.
        keys[4] = "5EwkJvkFgaQ";
        values[0] = "";
        values[1] = "";
        values[2] = "";
        values[3] = "";
        values[4] = "";
        connectionID = webManager._u_WebRequestPost(urlToRequest, this, true, true, keys, values);
        // The return value of _u_WebRequestPost() is a 0-255 value that can be used to track what web requests this behaviour has active.
        // A returned value of -1 means the request could not be made; there are already too many active connections.
    }

    public void _u_WebRequestReceived(/* int connectionID, byte[] connectionData, string connectionString, int responseCode */)
    {
        // This is called when the web request has been fully received by the web handler.
        // connectionID: the ID of the web request being returned
        // connectionData: raw response data if _u_WebRequestPost()'s returnUTF16String argument was false
        // connectionString: Unicode response string if _u_WebRequestPost()'s returnUTF16String argument was true
        // responseCode: HTTP response code for web request.  Code 111 if there was a problem making the request, Code 112 if POST arguments could not be filled.
    }
}
