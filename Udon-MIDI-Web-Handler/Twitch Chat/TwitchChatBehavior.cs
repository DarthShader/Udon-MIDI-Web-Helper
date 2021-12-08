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
    public SlotPool pool;
    public int onlineDataIndexInPoolSlots;
    public VRCUrlInputField urlField;
    public InputField output;
    public int maxChatLines = 42;

    int connectionID = -1;
    byte[] connectionData;
    string connectionString;
    bool messageIsText;

    [UdonSynced]
    VRCUrl syncedURL;
    [UdonSynced]
    uint syncedURLserializations;
    uint syncedURLdeserializations;

    [UdonSynced]
    string brokeredMessage;
    [UdonSynced]
    uint brokeredMessageSerializations;
    uint brokeredMessageDeserializations;

    bool urlIsTwitchChannel;
    bool bufferedDeserializationDone;
    string username;
    string connectedChannelName;
    TwitchChatState state;
    string[] chatMessages;
    int currentMessageCount;
    bool requestingOwnership;

    public DebugLogger debug;

    void Start()
    {
        debug._u_Log("[TwitchChatBehavior] Start");

        chatMessages = new string[maxChatLines];
        webManager._u_RegisterCallbackReceiver(this);
    }

    // Event called locally by any person who enters a new URL
    public void _u_ChannelEntered()
    {
        debug._u_Log("[TwitchChatBehavior] _u_ChannelEntered");

        // Always sync URL across all players' clients, even if it
        // isn't a valid twitch URL.  It would be better if there
        // were deserialization callbacks on the main video player to get
        // its synced url instead.
        requestingOwnership = true;
        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        syncedURL = urlField.GetUrl();
        syncedURLserializations++;
        RequestSerialization();
        _u_Resync();
    }

    public override void OnDeserialization()
    {
        debug._u_Log("[TwitchChatBehavior] OnDeserialization");

        if (!bufferedDeserializationDone)
        {
            bufferedDeserializationDone = true;
            syncedURLdeserializations = syncedURLserializations;
            brokeredMessageDeserializations = brokeredMessageSerializations;
            _u_Resync();
            return;
        }

        if (syncedURLdeserializations != syncedURLserializations)
        {
            debug._u_Log("[TwitchChatBehavior] syncedURLdeserialization");
            syncedURLdeserializations = syncedURLserializations;
            output.text = "";
            currentMessageCount = 0;
            _u_Resync();
        }

        if (brokeredMessageDeserializations != brokeredMessageSerializations)
        {
            Debug.Log("[TwitchChatBehavior] OnDeserialization brokeredMessageDeserializations");
            brokeredMessageDeserializations = brokeredMessageSerializations;
            _u_AppendChatMessage();
        }
    }

    void _u_Resync()
    {
        debug._u_Log("[TwitchChatBehavior] _u_Resync");

        // Verify twitch URL is a twitch URL
        if (syncedURL != null)
        {
            string url = syncedURL.Get();
            if (url.Length >= 23 && (url.StartsWith("https://www.twitch.tv/") || url.StartsWith("https://twitch.tv/")))
                urlIsTwitchChannel = true;
            else urlIsTwitchChannel = false;
        }
        else urlIsTwitchChannel = false;

        // Pick a new broker for chat, only switch owners if necessary
        if (!_u_PlayerIsOnlineAndAvailable(Networking.GetOwner(gameObject)) && webManager.online)
        {
            // Go through synced order list of players so web connected connected clients
            // agree on who should be the new broker.
            // Could improve this by prioritizing the first online person with the fewest connections open
            VRCPlayerApi[] players = pool._u_GetPlayersOrdered();
            if (players == null)
            {
                debug._u_Log("[TwitchChatBehavior] Error: ordered players array not initialized yet");
                return;
            }
            foreach (VRCPlayerApi player in players)
                if (_u_PlayerIsOnlineAndAvailable(player))
                {
                    if (player == Networking.LocalPlayer)
                    {
                        requestingOwnership = true;
                        Networking.SetOwner(Networking.LocalPlayer, gameObject);
                    }
                    break;
                }
        }

        _u_TryStartChat();
    }

    bool _u_PlayerIsOnlineAndAvailable(VRCPlayerApi player)
    {
        debug._u_Log("[TwitchChatBehavior] _u_PlayerIsOnlineAndAvailable player:" + player.displayName);

        UdonSharpBehaviour[] usbs = pool._u_GetPlayerData(player);
        if (usbs == null) 
        {
            debug._u_Log("[TwitchChatBehavior] Error: _u_PlayerIsOnlineAndAvailable was not ready yet");
            return false; // case where pool is not initialized yet
        }
        SlotDataOnlineStatus playerOnlineStatus = (SlotDataOnlineStatus)usbs[onlineDataIndexInPoolSlots];
        return playerOnlineStatus.online && playerOnlineStatus.connectionsOpen < 255;
    }

    void _u_TryStartChat()
    {
        debug._u_Log("[TwitchChatBehavior] _u_TryStartChat");

        if (!_u_PlayerIsOnlineAndAvailable(Networking.GetOwner(gameObject)))
        {
            debug._u_Log("[TwitchChatBehavior] No player is web connected!");

            output.text = "No player is web connected!";
            currentMessageCount = 0;
            return;
        }

        // The current behavior is to only allow the chat owner to connect through the web handler.
        // Since messages are going to be broadcast to everyone anyway, it would be best for all
        // other web conncted clients to save their Udon processing power and connection counts.
        // Could make chat brokering opt-out and brokering disable options in the future.
        if (!webManager.online || !Networking.IsOwner(gameObject) || !urlIsTwitchChannel)
            return;

        string url = syncedURL.Get();
        string channelToConnectTo = url.Remove(0, 22);
        if (connectionID != -1 && state == TwitchChatState.ChannelJoined && channelToConnectTo != connectedChannelName)
        {
            // Preserve existing websocket connection, simply change irc channel
            webManager._u_WebSocketSendStringUnicode(connectionID, "PART #" + connectedChannelName, true);
            output.text = "";
            currentMessageCount = 0;
            webManager._u_ClearConnection(connectionID);
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
            {
                output.text = "Too many active connections!  Finding new broker.\n";
                _u_Resync();
                return;
            }
            else state = TwitchChatState.WebSocketOpening;
        }
        connectedChannelName = channelToConnectTo;
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        debug._u_Log("[TwitchChatBehavior] OnOwnershipTransferred");

        if (requestingOwnership)
            requestingOwnership = false;
        else if (player == Networking.LocalPlayer)
            _u_Resync(); // Master reset
    }

    void _u_Reset()
    {
        debug._u_Log("[TwitchChatBehavior] _u_Reset");

        // Close if open
        state = TwitchChatState.Off;
        if (connectionID != -1)
        {
            webManager._u_WebSocketClose(connectionID);
            state = TwitchChatState.Closing; // Complete reset
        }
        connectedChannelName = "";
        output.text = "";
        currentMessageCount = 0;
    }

    public void _u_WebSocketOpened(/* int connectionID */)
    {
        debug._u_Log("[TwitchChatBehavior] _u_WebSocketOpened");

        webManager._u_WebSocketSendStringUnicode(connectionID, "CAP REQ :twitch.tv/tags twitch.tv/commands", true);
        state = TwitchChatState.CapReqSent; // First message sent
    }

    public void _u_WebSocketReceive(/* int connectionID, byte[] connectionData, string connectionString, bool messageIsText */)
    {
        if (connectionString == "PING :tmi.twitch.tv\r\n")
        {
            debug._u_Log("[TwitchChatBehavior] PING received");
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
                // Remove this for release, as this is a security flaw that could allow injecting log lines via twitch chat messages
                // Debug option exclusively for non-chat-message websocket messages.
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
                // Replace < and > to prevent escapeing Unity UI's rich text markup
                // ≺ ≻
                string completeMessage = "<b><color=" + color + ">" + name + "</color></b>: " + message.Replace('<', '〈').Replace('>', '〉').Replace("\r\n", "");
                if (brokeredMessage != "")
                    brokeredMessage += "\n";
                brokeredMessage += completeMessage;
                brokeredMessageSerializations++;
                RequestSerialization();
                _u_AppendChatMessage();
                // Special case where OnPostSerialization is never reached
                if (VRCPlayerApi.GetPlayerCount() == 1)
                    brokeredMessage = "";
            }
        }
    }

    public void _u_WebSocketClosed(/* int connectionID */)
    {
        debug._u_Log("[TwitchChatBehavior] _u_WebSocketClosed");

        connectionID = -1;
        output.text = "";
        currentMessageCount = 0;
        // This close could have been achieved gracefully as the result of
        // _u_Reset() or ungracefully from twitch aborting the connection
        // because the justinfan nickname was already in use.  If a close wasn't
        // expected, resync.
        connectedChannelName = "";
        if (state != TwitchChatState.Closing)
            _u_TryStartChat();
        else state = TwitchChatState.Off;
    }

    void _u_AppendChatMessage()
    {
        if (currentMessageCount == maxChatLines)
        {
            currentMessageCount--;
            // shift all chat messages down by 1 if chat is full
            for (int i=1; i<=currentMessageCount; i++)
                chatMessages[i-1] = chatMessages[i];
        }
        chatMessages[currentMessageCount] = brokeredMessage;
        string thisCouldHaveBeenAStringBuilder = "";
        for (int i=0; i<=currentMessageCount; i++)
            thisCouldHaveBeenAStringBuilder += chatMessages[i] + "\n";
        output.text = thisCouldHaveBeenAStringBuilder;
        currentMessageCount++;
    }

    public void OnPostSerialization(VRC.Udon.Common.SerializationResult result)
    {
        brokeredMessage = "";
        //debug._u_Log("[TwitchChatBehavior] OnPostSerialization: " + result.success + " " + result.byteCount);
    }

    public void _u_OnUdonMIDIWebHandlerOnlineChanged()
    {
        debug._u_Log("[TwitchChatBehavior] _u_OnUdonMIDIWebHandlerOnlineChanged");
        _u_Resync();
    }
}