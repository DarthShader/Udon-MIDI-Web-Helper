using System.Text;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using VRC.SDK3.Components;
using System.Text.RegularExpressions;
using System;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class YoutubeVideoInfo : UdonSharpBehaviour
{
    public UdonMIDIWebHandler webManager;
    public SlotPool pool;
    public int onlineDataIndexInPoolSlots;
    public VRCUrlInputField urlField;
    // Example API key.  This will most likely be fully used/abused (10,000 daily request quota).
    // It is recommended to get a new api key for each VRChat world.
    // Appreciate the dislikes while you can :^)
    public string youtubeApiKey = "AIzaSyAH3T9OVGe9ii7uW1J7Hax9fFr0qFUzNIY";
    public Text videoTitleText;
    public Text channelNameText;
    public Text videoDescriptionText;
    public Text viewsCountText;
    public Text likesCountText;
    public Text dislikesCountText;
    public Text commentCountText;

    int requestConnectionID = -1;
    int connectionID = -1;
    byte[] connectionData;
    string connectionString;
    int responseCode;
    bool messageIsText;

    [UdonSynced]
    VRCUrl syncedURL;
    [UdonSynced]
    uint syncedURLserializations;
    uint syncedURLdeserializations;

    [UdonSynced]
    string title;
    [UdonSynced]
    string channel;
    [UdonSynced]
    string description;
    [UdonSynced]
    uint viewCount;
    [UdonSynced]
    uint likeCount;
    [UdonSynced]
    uint dislikeCount;
    [UdonSynced]
    uint commentCount;
    [UdonSynced]
    uint videoInfoSerializations;
    uint videoInfoDeserializations;

    bool urlIsYoutubeVideo;
    bool bufferedDeserializationDone;
    bool requestingOwnership;

    public DebugLogger debug;

    void Start()
    {
        debug._u_Log("[YoutubeVideoInfo] Start");
        webManager._u_RegisterCallbackReceiver(this);
    }

    // Event called locally by any person who enters a new URL
    public void _u_ChannelEntered()
    {
        debug._u_Log("[YoutubeVideoInfo] _u_ChannelEntered");
        // Always sync URL across all players' clients, even if it
        // isn't a valid youtube video.  It would be better if there
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
        if (!bufferedDeserializationDone)
        {
            bufferedDeserializationDone = true;
            syncedURLdeserializations = syncedURLserializations;
            videoInfoDeserializations = videoInfoSerializations;
            //_u_Resync();
            return;
        }

        if (syncedURLdeserializations != syncedURLserializations)
        {
            debug._u_Log("[YoutubeVideoInfo] syncedURLdeserialization");
            syncedURLdeserializations = syncedURLserializations;
            _u_Resync();
        }

        if (videoInfoDeserializations != videoInfoSerializations)
        {
            videoInfoDeserializations = videoInfoSerializations;
            _u_ApplyVideoInfo();
        }
    }

    void _u_Resync()
    {
        debug._u_Log("[YoutubeVideoInfo] _u_Resync");
        // Verify twitch URL is a twitch URL
        if (syncedURL != null)
        {
            string url = syncedURL.Get();
            if (url.Length >= 23 && url.StartsWith("https://www.youtube.com/watch?"))
                urlIsYoutubeVideo = true;
            else urlIsYoutubeVideo = false;
        }
        else urlIsYoutubeVideo = false;

        // Pick a new broker for chat, only switch owners if necessary
        if (!_u_PlayerIsOnlineAndAvailable(Networking.GetOwner(gameObject)) && webManager.online)
        {
            // Go through synced order list of players so web connected connected clients
            // agree on who should be the new broker.
            // Could improve this by prioritizing the first online person with the fewest connections open
            VRCPlayerApi[] players = pool._u_GetPlayersOrdered();
            if (players == null)
            {
                debug._u_Log("[YoutubeVideoInfo] Error: ordered players array not initialized yet");
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

        _u_TryGetYoutubeVideoInfo();
    }

    bool _u_PlayerIsOnlineAndAvailable(VRCPlayerApi player)
    {
        UdonSharpBehaviour[] usbs = pool._u_GetPlayerData(player);
        if (usbs == null) 
        {
            debug._u_Log("[YoutubeVideoInfo] Error: _u_PlayerIsOnlineAndAvailable was not ready yet");
            return false; // case where pool is not initialized yet
        }
        SlotDataOnlineStatus playerOnlineStatus = (SlotDataOnlineStatus)usbs[onlineDataIndexInPoolSlots];
        return playerOnlineStatus.online && playerOnlineStatus.connectionsOpen < 255;
    }

    void _u_TryGetYoutubeVideoInfo()
    {
        debug._u_Log("[YoutubeVideoInfo] _u_TryGetYoutubeVideoInfo");

        if (!_u_PlayerIsOnlineAndAvailable(Networking.GetOwner(gameObject)))
            return;

        // Only the online owner should attempt to get metadata from the youtube api
        if (!webManager.online || !Networking.IsOwner(gameObject) || !urlIsYoutubeVideo)
            return;

        string url = syncedURL.Get();
        int videoArgIndex = url.IndexOf("v=", 29);
        if (videoArgIndex == -1)
        {
            debug._u_Log("[YoutubeVideoInfo] Could not parse video id");
            return;
        }

        string videoID = url.Substring(videoArgIndex+2).Split('&')[0];
        string youtubeApiUrl = "https://www.googleapis.com/youtube/v3/videos?id=" + videoID + "&part=snippet,statistics&fields=items(snippet(publishedAt,title,description,channelTitle),statistics)&key=" + youtubeApiKey;
        if (requestConnectionID != -1)
            webManager._u_ClearConnection(requestConnectionID);
        requestConnectionID = webManager._u_WebRequestGet(youtubeApiUrl, this, true, true);
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        if (requestingOwnership)
            requestingOwnership = false;
        else if (player == Networking.LocalPlayer)
            _u_Resync(); // Master reset
    }

    public void _u_WebRequestReceived(/* int connectionID, byte[] connectionData, string connectionString, int responseCode */)
    {
        if (connectionID != requestConnectionID) return;
        requestConnectionID = -1;
        debug._u_Log("[YoutubeVideoInfo] _u_WebRequestReceived");

        if (responseCode == 200)
        {
            // response json is already pretty printed
            string[] split = connectionString.Split('\n');
            for (int i=0; i<split.Length; i++)
            {
                string trimmedLine = split[i].TrimStart(' ');
                if (trimmedLine.StartsWith("\"title\": \""))
                    // trim json key name and ", at end of line
                    title = _u_JSONUnescape(trimmedLine.Substring(10, trimmedLine.Length-12));
                else if (trimmedLine.StartsWith("\"description\": \""))
                    description = _u_JSONUnescape(trimmedLine.Substring(16, trimmedLine.Length-18));
                else if (trimmedLine.StartsWith("\"channelTitle\": \""))
                    channel = _u_JSONUnescape(trimmedLine.Substring(17, trimmedLine.Length-19));
                else if (trimmedLine.StartsWith("\"viewCount\": \""))
                    viewCount = UInt32.Parse(trimmedLine.Substring(14, trimmedLine.Length-16));
                else if (trimmedLine.StartsWith("\"likeCount\": \""))
                    likeCount = UInt32.Parse(trimmedLine.Substring(14, trimmedLine.Length-16));
                else if (trimmedLine.StartsWith("\"dislikeCount\": \""))
                    dislikeCount = UInt32.Parse(trimmedLine.Substring(17, trimmedLine.Length-19));
                else if (trimmedLine.StartsWith("\"commentCount\": \""))
                    commentCount = UInt32.Parse(trimmedLine.Substring(17, trimmedLine.Length-19));
            }
            videoInfoSerializations++;
            RequestSerialization();
            _u_ApplyVideoInfo();
        }
        else
        {
            debug._u_Log("[YoutubeVideoInfo] Web request failed: " + responseCode + " " + connectionString);
        }
    }

    void _u_ApplyVideoInfo()
    {
        videoTitleText.text = title;
        channelNameText.text = channel;
        viewsCountText.text = viewCount.ToString();
        likesCountText.text = likeCount.ToString();
        dislikesCountText.text = dislikeCount.ToString();
        commentCountText.text = commentCount.ToString();
        videoDescriptionText.text = description;
    }

    // UdonSharp.Core.NotExposedException: Method is not exposed to Udon: 'Regex.Unescape
    string _u_JSONUnescape(string s)
    {
        return s.Replace("\\\\", "\\").Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\"", "\"");
    }

    public void _u_OnUdonMIDIWebHandlerOnlineChanged()
    {
        _u_Resync();
    }
}
