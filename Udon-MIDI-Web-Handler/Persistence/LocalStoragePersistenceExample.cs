using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using System;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class LocalStoragePersistenceExample : UdonSharpBehaviour
{
    // Link this behaviour to the centralized web handler
    public UdonMIDIWebHandler webManager;
    // Create variables to act as arguments for WebRequestReceived()
    int connectionID;
    byte[] connectionData;
    string connectionString;
    string unicodeData;
    int responseCode;

    public InputField keyField;
    public InputField valueField;
    public InputField worldId;
    public Toggle publicToggle;
    public Toggle globalToggle;

    public void _u_Store()
    {
        // StoreLocalValue() arguments:
        // string key: key to store with value
        // string value: value to store
        // bool valueIsPublic: Whether or not this value will be readable by any other world
        // bool global: Whether or not this should be stored as a global key/value pair or the current world id verified by the helper program
        webManager._u_StoreLocalValue(keyField.text, valueField.text, publicToggle.isOn, globalToggle.isOn);
        keyField.text = "";
        valueField.text = "";
    }

    public void _u_Retrieve()
    {
        // RetrieveLocalValue() arguments:
        // UdonSharpBehaviour usb: Takes a reference of the behaviour to call WebRequestReceived() on
        // string key: key to store with value
        // string worldID: world id the key/value pair should be retrieved from.  This can be a valid world id or "global" to access global key/value pairs.
        connectionID = webManager._u_RetrieveLocalValue(this, keyField.text, worldId.text);
    }

    public void _u_WebRequestReceived(/* int connectionID, byte[] connectionData, string connectionString, int responseCode */)
    {
        // This is called when the retrieve request has been fully received by the web handler.
        // connectionID: the ID of the request being returned
        // connectionData: unused
        // connectionString: Value of the retrieved key/value pair
        // responseCode: HTTP-esque response code for request.  Code 111 if there was a problem making the request.  404 if the value couldn't be found.  403 if the value is private.
        valueField.text = responseCode + " " + connectionString;
    }
}
