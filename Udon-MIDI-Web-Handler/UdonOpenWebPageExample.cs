using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using System;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class UdonOpenWebPageExample : UdonSharpBehaviour
{
    public UdonMIDIWebHandler webManager;
    public string url;

    public override void Interact()
    {
        // This is currently rate limited in the helper program
        // to 1 web page per second to prevent abuse.
        webManager._u_OpenWebPage(url);
    }
}
