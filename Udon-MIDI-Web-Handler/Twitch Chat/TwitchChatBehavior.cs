using System.Text;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using VRC.SDK3.Components;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class TwitchChatBehavior : UdonSharpBehaviour
{
    public UdonMIDIWebHandler webManager;

    int connectionID = -1;
    byte[] connectionData;
    string connectionString;
    string unicodeData;
    bool messageIsText;

    string username;
    [UdonSynced]
    VRCUrl syncedURL;
    string connectedChannelName;
    int state;

    string[] chatMessages;
    int currentMessageCount;

    public VRCUrlInputField urlField;
    public InputField output;
    public int maxChatLines = 42;

    void Start()
    {
        chatMessages = new string[maxChatLines];
    }

    public void ChannelEntered()
    {
        string url = urlField.GetUrl().Get();
        if (url.StartsWith("https://www.twitch.tv/"))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
            syncedURL = urlField.GetUrl();
            Resync();
            RequestSerialization();
        }
    }

    public override void OnDeserialization()
    {
        Resync();
    }

    public void Resync()
    {
        string url = syncedURL.Get();
        connectedChannelName = url.Remove(0, 22);

        if (connectionID != -1)
            webManager.WebSocketClose(connectionID);

        if (connectedChannelName != "")
        {
            connectionID = webManager.WebSocketOpen("wss://irc-ws.chat.twitch.tv/", this, true, true);
            if (connectionID == -1)
                output.text = "Too many active connections!\n";
            else
            {
                output.text = "";
                currentMessageCount = 0;
                state = 1;
            }
        }
    }

    public void WebSocketOpened(/* int connectionID */)
    {
        webManager.WebSocketSendStringUnicode(connectionID, "CAP REQ :twitch.tv/tags twitch.tv/commands", true);
        state = 2;
    }

    public void WebSocketReceive(/* int connectionID, byte[] connectionData, string connectionString, bool messageIsText */)
    {
        Debug.Log("[TwitchChatBehavior] WebSocketReceive: " + connectionString);
        if (connectionString == "PING :tmi.twitch.tv\r\n")
        {
            Debug.Log("PING received");
            webManager.WebSocketSendStringUnicode(connectionID, "PONG", true);
            return;
        }
        else if (state == 2)
        {
            webManager.WebSocketSendStringUnicode(connectionID, "PASS SCHMOOPIIE", true);
            username = "justinfan" + (int)(1000 + Random.value * 80000);
            webManager.WebSocketSendStringUnicode(connectionID, "NICK " + username, true);
            state = 3;
        }
        else if (state == 3 && connectionString.Contains("Welcome, GLHF!"))
        {
            webManager.WebSocketSendStringUnicode(connectionID, "USER " + username + " 8 * :" + username, true);
            webManager.WebSocketSendStringUnicode(connectionID, "JOIN #" + connectedChannelName, true);
            state = 4;
        }
        else if (state == 4)
        {
            // Ready to receive chat messages
            string[] split = connectionString.Split(' ');
            if (split[2] != "PRIVMSG")
            {
                //output.text += connectionString + '\n';
                return;
            }

            string color = "white";
            string[] kvPairs = split[0].Split(';');
            foreach (string kv in kvPairs)
            {
                string[] kvSplit = kv.Split('=');
                if (kvSplit.Length > 1 && kvSplit[0] == "color")
                {
                    color = kvSplit[1];
                    break;
                }
            }
            int nicknameSplitIndex = split[1].IndexOf('!');
            if (nicknameSplitIndex < 1 || split[1].Length < 2)
                return;
            string name = split[1].Substring(1, nicknameSplitIndex-1);
            if (split[4].Length < 1)
                return;
            string messageIndexIdentifier = " PRIVMSG #" + connectedChannelName + " :";
            string message = connectionString.Substring(connectionString.IndexOf(messageIndexIdentifier) + messageIndexIdentifier.Length);

            // ≺ ≻
            if (currentMessageCount == maxChatLines)
            {
                currentMessageCount--;
                // shift all chat messages down by 1 if chat is full
                for (int i=1; i<=currentMessageCount; i++)
                    chatMessages[i-1] = chatMessages[i];
            }
            chatMessages[currentMessageCount] = "<b><color=" + color + ">" + name + "</color></b>: " + message.Replace('<', '〈').Replace('>', '〉').Replace("\r\n", "");
            string thisCouldHaveBeenAStringBuilder = "";
            for (int i=0; i<=currentMessageCount; i++)
                thisCouldHaveBeenAStringBuilder += chatMessages[i] + "\n";
            output.text = thisCouldHaveBeenAStringBuilder;
            currentMessageCount++;
            //output.MoveTextEnd(false);
        }
    }

    public void WebSocketClosed(/* int connectionID */)
    {
        output.text += "WebSocket Closed!";
        connectionID = -1;
        state = 0;
    }
}
