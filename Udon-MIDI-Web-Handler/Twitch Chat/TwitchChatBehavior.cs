using System.Text;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using VRC.SDK3.Components;

enum TwitchChatState
{
    Off,
    WebSocketOpening,
    CapReqSent,
    NicknameSent,
    ChannelJoined,
    Closing
};

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class TwitchChatBehavior : UdonSharpBehaviour
{
    public UdonMIDIWebHandler webManager;
    public VRCUrlInputField urlField;
    public InputField output;
    public int maxChatLines = 42;

    int connectionID = -1;
    byte[] connectionData;
    string connectionString;
    string unicodeData;
    bool messageIsText;

    string username;
    [UdonSynced]
    VRCUrl syncedURL;
    string connectedChannelName;
    TwitchChatState state;
    string[] chatMessages;
    int currentMessageCount;

    void Start()
    {
        chatMessages = new string[maxChatLines];
    }

    public void _u_ChannelEntered()
    {
        // Always sync URL across all players' clients, even if it
        // isn't a valid twitch URL.
        string url = urlField.GetUrl().Get();
        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        syncedURL = urlField.GetUrl();
        RequestSerialization();
        _u_Resync();
    }

    public override void OnDeserialization()
    {
        _u_Resync();
    }

    void _u_Reset()
    {
        // Close if open
        state = TwitchChatState.Off;
        if (connectionID != -1)
        {
            webManager._u_WebSocketClose(connectionID);
            state = TwitchChatState.Closing; // Complete reset
        }
        output.text = "";
        currentMessageCount = 0;
    }

    public void _u_Resync()
    {
        string url = syncedURL.Get();
        // If the channel is a valid twitch link, have everyone
        // adjust or open their connection to that channel.  Otherwise 
        // everyone needs to gracefully close their existing connections.
        if (url.Length >= 23 && url.StartsWith("https://www.twitch.tv/"))
        {
            string channelToConnectTo = url.Remove(0, 22);
            if (connectionID != -1 && state == TwitchChatState.ChannelJoined)
            {
                // Sometimes you need to reconnect to the same channel.  e.g. justinfan nickname is taken
                //if (channelToConnectTo == connectedChannelName) return;

                // Preserve existing websocket connection, simply change irc channel
                webManager._u_WebSocketSendStringUnicode(connectionID, "PART #" + connectedChannelName, true);
                output.text = "";
                currentMessageCount = 0;
                webManager._u_WebSocketClear(connectionID);
                webManager._u_WebSocketSendStringUnicode(connectionID, "JOIN #" + channelToConnectTo, true);
            }
            else
            {
                // Fresh connect!
                // Perform a full reset in case something goes wrong with the websocket connection.
                // Twitch doesn't like to follow full websocket protocol so the helper program runs into issues
                _u_Reset();
                connectionID = webManager._u_WebSocketOpen("wss://irc-ws.chat.twitch.tv/", this, true, true);
                if (connectionID == -1)
                    output.text = "Too many active connections!\n";
                else
                {
                    currentMessageCount = 0;
                    state = TwitchChatState.WebSocketOpening; // Connection opened, waiting for confirmation that the connection was opened
                }
            }
            connectedChannelName = channelToConnectTo;
        }
        else _u_Reset();
    }

    public void _u_WebSocketOpened(/* int connectionID */)
    {
        webManager._u_WebSocketSendStringUnicode(connectionID, "CAP REQ :twitch.tv/tags twitch.tv/commands", true);
        state = TwitchChatState.CapReqSent; // First message sent
    }

    public void _u_WebSocketReceive(/* int connectionID, byte[] connectionData, string connectionString, bool messageIsText */)
    {
        // Remove this for release, as this is a security flaw that could allow injecting log lines
        // Debug.Log("[TwitchChatBehavior] WebSocketReceive: " + connectionString);

        if (connectionString == "PING :tmi.twitch.tv\r\n")
        {
            Debug.Log("PING received");
            webManager._u_WebSocketSendStringUnicode(connectionID, "PONG", true);
            return;
        }
        else if (state == TwitchChatState.CapReqSent)
        {
            webManager._u_WebSocketSendStringUnicode(connectionID, "PASS SCHMOOPIIE", true);
            username = "justinfan" + (int)(1000 + Random.value * 80000);
            webManager._u_WebSocketSendStringUnicode(connectionID, "NICK " + username, true);
            state = TwitchChatState.NicknameSent; // Second message sent
        }
        else if (state == TwitchChatState.NicknameSent && connectionString.Contains("Welcome, GLHF!"))
        {
            webManager._u_WebSocketSendStringUnicode(connectionID, "USER " + username + " 8 * :" + username, true);
            webManager._u_WebSocketSendStringUnicode(connectionID, "JOIN #" + connectedChannelName, true);
            state = TwitchChatState.ChannelJoined; // Third message set sent
        }
        else if (state == TwitchChatState.ChannelJoined)
        {
            // Ready to receive chat messages
            string[] split = connectionString.Split(' ');
            if (split.Length < 5 || split[2] != "PRIVMSG")
            {
                // Debug option exclusively for non-chat-message ws messages
                // Debug.Log("[TwitchChatBehavior] WebSocketReceive: " + connectionString);
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
            // Ignore messages from other channels that may be left over after flushing the websocket connection
            int messageStart = connectionString.IndexOf(messageIndexIdentifier);
            if (messageStart != -1)
            {
                string message = connectionString.Substring(messageStart + messageIndexIdentifier.Length);
                if (currentMessageCount == maxChatLines)
                {
                    currentMessageCount--;
                    // shift all chat messages down by 1 if chat is full
                    for (int i=1; i<=currentMessageCount; i++)
                        chatMessages[i-1] = chatMessages[i];
                }
                // Replace < and > to prevent escapeing Unity UI's rich text markup
                // ≺ ≻
                chatMessages[currentMessageCount] = "<b><color=" + color + ">" + name + "</color></b>: " + message.Replace('<', '〈').Replace('>', '〉').Replace("\r\n", "");
                string thisCouldHaveBeenAStringBuilder = "";
                for (int i=0; i<=currentMessageCount; i++)
                    thisCouldHaveBeenAStringBuilder += chatMessages[i] + "\n";
                output.text = thisCouldHaveBeenAStringBuilder;
                currentMessageCount++;
            }
        }
    }

    public void _u_WebSocketClosed(/* int connectionID */)
    {
        connectionID = -1;

        output.text = "";
        currentMessageCount = 0;
        // This close could have been achieved gracefully as the result of
        // _u_Reset() or ungracefully from twitch aborting the connection
        // because the justinfan nickname was already in use.  If a close wasn't
        // expected, resync.
        if (state != TwitchChatState.Closing)
            _u_Resync();
        else state = TwitchChatState.Off;
    }
}
