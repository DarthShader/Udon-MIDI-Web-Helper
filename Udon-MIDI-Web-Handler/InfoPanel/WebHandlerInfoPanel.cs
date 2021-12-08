using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class WebHandlerInfoPanel : UdonSharpBehaviour
{
    const float UPDATE_INTERVAL_SECONDS = 0.5f;

    public UdonMIDIWebHandler webHandler;
    public Text onlineStatus;
    public Text playerCount;
    public Text connectionsOpen;
    public Text commandsSent;
    public Text responsesReceived;
    public Text bytesReceived;
    public Text responsesQueued;
    public Text bytesQueued;

    float t;
    int oldBytesReceived;

    void Update()
    {
        t += Time.deltaTime;
        if (t > UPDATE_INTERVAL_SECONDS)
        {
            t = 0;
            if (webHandler.online)
                onlineStatus.text = "Online";
            else if (webHandler.playersOnline > 0)
                onlineStatus.text = "Brokered";
            else onlineStatus.text = "Offline";
            playerCount.text = webHandler.playersOnline.ToString();
            connectionsOpen.text = webHandler.connectionsOpen.ToString();
            commandsSent.text = webHandler.commandsSent.ToString();
            responsesReceived.text = webHandler.responsesReceived.ToString();
            int bytesReceivedSpeed = (webHandler.bytesReceived - oldBytesReceived) * (int)(1f/UPDATE_INTERVAL_SECONDS);
            oldBytesReceived = webHandler.bytesReceived;
            bytesReceived.text = webHandler.bytesReceived + " (" + bytesReceivedSpeed + "/s)";
            responsesQueued.text = webHandler.queuedResponsesCount.ToString();
            bytesQueued.text = webHandler.queuedBytesCount.ToString();
        }
    }
}
