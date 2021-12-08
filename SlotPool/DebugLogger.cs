using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class DebugLogger : UdonSharpBehaviour
{
    public InputField inputField;
    public bool logPlayerChanges;

    public void _u_Log(string text)
    {
        Debug.Log(text);
        inputField.text += text + "\n";
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (logPlayerChanges)
            inputField.text += "[DebugLogger] OnPlayerJoined " + player.displayName + "\n";
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (logPlayerChanges && Utilities.IsValid(player))
            inputField.text += "[DebugLogger] OnPlayerLeft " + player.displayName + "\n";
    }
}
