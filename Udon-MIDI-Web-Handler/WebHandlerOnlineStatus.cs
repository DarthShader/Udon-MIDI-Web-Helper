
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class WebHandlerOnlineStatus : UdonSharpBehaviour
{
    public UdonMIDIWebHandler webHandler;
    public Text Label;

    void Start()
    {
        webHandler.RegisterCallbackReceiver(this);
    }

    public void _u_OnUdonMIDIWebHandlerOnline()
    {
        Label.text = "<color=\"green\">ONLINE</color>";
    }

    public void _u_OnUdonMIDIWebHandlerOffline()
    {
        Label.text = "<color=\"red\">OFFLINE</color>";
    }
}
