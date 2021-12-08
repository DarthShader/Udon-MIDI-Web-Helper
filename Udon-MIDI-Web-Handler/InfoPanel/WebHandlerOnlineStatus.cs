using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class WebHandlerOnlineStatus : UdonSharpBehaviour
{
    public UdonMIDIWebHandler webHandler;
    public SlotPool pool;

    public Color onlineBackground;
    public Color onlineStatus;
    public Color onlineText;
    public Color brokeredBackground;
    public Color brokeredStatus;
    public Color brokeredText;
    public Color offlineBackground;
    public Color offlineStatus;
    public Color offlineText;

    Material background;
    Material status;
    Text playerCount;

    void Start()
    {
        background = transform.Find("background").gameObject.GetComponent<MeshRenderer>().material;
        status = transform.Find("status").gameObject.GetComponent<MeshRenderer>().material;
        playerCount = transform.Find("status/Canvas/Text").gameObject.GetComponent<Text>();
        webHandler._u_RegisterCallbackReceiver(this);
        pool._u_RegisterCallbackReceiver(this);
    }

    public void _u_OnPoolDeserializationComplete()
    {
        _u_OnUdonMIDIWebHandlerOnlineChanged();
    }

    public void _u_OnUdonMIDIWebHandlerOnlineChanged()
    {
        if (webHandler.online)
        {
            background.SetColor("_Color", onlineBackground);
            status.SetColor("_Color", onlineStatus);
            playerCount.color = onlineText;
        }
        else if (webHandler.playersOnline > 0)
        {
            background.SetColor("_Color", brokeredBackground);
            status.SetColor("_Color", brokeredStatus);
            playerCount.color = brokeredText;
        }
        else
        {
            background.SetColor("_Color", offlineBackground);
            status.SetColor("_Color", offlineStatus);
            playerCount.color = offlineText;
        }
        playerCount.text = "" + webHandler.playersOnline;
    }
}
