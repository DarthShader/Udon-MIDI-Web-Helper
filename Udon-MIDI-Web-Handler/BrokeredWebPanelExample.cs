using System.Text;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;

// WARNING: DO NOT USE THIS IN A REGULAR VRCHAT WORLD, IP ADDRESSES COULD BE LEAKED

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class BrokeredWebPanelExample : UdonSharpBehaviour
{
    public UdonMIDIWebHandler webManager;
    public SlotPool pool;
    public int onlineDataIndexInPoolSlots;
    public InputField input;
    public InputField output;

    [UdonSynced]
    string url;
    [UdonSynced]
    uint urlSerializationsCount;
    uint urlDeserializationsCount;
    [UdonSynced]
    string response;

    bool bufferedDeserializationComplete;
    int requestedConnectionID = -1;

    int connectionID = -1;
    byte[] connectionData;
    string connectionString;
    int responseCode;

    void Start()
    {
        if (Networking.LocalPlayer.isMaster)
            bufferedDeserializationComplete = true;
    }

    public void _u_UrlEntered()
    {
        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        url = input.text;
        urlSerializationsCount++;
        response = "";
        RequestSerialization();
        _u_PickNewBrokerAndRequest();
    }

    void _u_PickNewBrokerAndRequest()
    {
        // Pick a new broker for chat, only switch owners if necessary
        if (!_u_PlayerIsOnlineAndAvailable(Networking.GetOwner(gameObject)) && webManager.online)
        {
            // Go through synced order list of players so web connected connected clients
            // agree on who should be the new broker.
            // Could improve this by prioritizing the first online person with the fewest connections open
            VRCPlayerApi[] players = pool._u_GetPlayersOrdered();
            if (players == null)
                return;
            foreach (VRCPlayerApi player in players)
                if (_u_PlayerIsOnlineAndAvailable(player))
                {
                    if (player == Networking.LocalPlayer)
                        Networking.SetOwner(Networking.LocalPlayer, gameObject);
                    break;
                }
        }

        // Attempt download
        if (!_u_PlayerIsOnlineAndAvailable(Networking.GetOwner(gameObject)))
        {
            output.text = "No player is web connected!";
            return;
        }
        if (!webManager.online || !Networking.IsOwner(gameObject))
            return;
        
        if (requestedConnectionID != -1)
            webManager._u_ClearConnection(requestedConnectionID);
        requestedConnectionID = webManager._u_WebRequestGet(url, this, true, true);
    }

    bool _u_PlayerIsOnlineAndAvailable(VRCPlayerApi player)
    {
        UdonSharpBehaviour[] usbs = pool._u_GetPlayerData(player);
        if (usbs == null) 
        {
            //debug._u_Log("[BrokeredWebPanelExample] Error: _u_PlayerIsOnlineAndAvailable was not ready yet");
            return false; // case where pool is not initialized yet
        }
        SlotDataOnlineStatus playerOnlineStatus = (SlotDataOnlineStatus)usbs[onlineDataIndexInPoolSlots];
        return playerOnlineStatus.online && playerOnlineStatus.connectionsOpen < 255;
    }

    public void _u_WebRequestReceived(/* int connectionID, byte[] connectionData, string connectionString, int responseCode */)
    {
        if (requestedConnectionID != connectionID) return;
        requestedConnectionID = -1;

        response = responseCode + " " + connectionString;
        output.text = response;
        RequestSerialization();
    }

    public override void OnDeserialization()
    {
        if (!bufferedDeserializationComplete)
        {
            urlDeserializationsCount = urlSerializationsCount;
            bufferedDeserializationComplete = true;
        }

        if (urlDeserializationsCount != urlSerializationsCount)
        {
            urlDeserializationsCount = urlSerializationsCount;
            input.text = url;
            _u_PickNewBrokerAndRequest();
        }

        output.text = response;
    }
}
