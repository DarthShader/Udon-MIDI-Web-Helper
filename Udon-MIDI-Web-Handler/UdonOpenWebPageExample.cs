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
        webManager._u_OpenWebPage(url);
    }
}
