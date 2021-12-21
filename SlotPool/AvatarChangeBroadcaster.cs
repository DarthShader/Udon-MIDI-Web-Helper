using System.Text;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;

// Requires --log-debug-levels=NetworkTransport

// UPDATE: As of VRChat build 1160 (12/10/2021), NetworkTransport logging has been completely removed.
// Avatar changes can no longer be parsed and sent through MIDI

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class AvatarChangeBroadcaster : UdonSharpBehaviour
{
    public UdonMIDIWebHandler webManager;
    public SlotPool pool;
    public int onlineDataIndexInPoolSlots;

    [UdonSynced]
    string displayName = "";
    [UdonSynced]
    string avatarID = "";
    [UdonSynced]
    uint avatarChangeSerializations;
    uint avatarChangeDeserializations;

    int connectionID = -1;
    byte[] connectionData;
    string connectionString;
    int responseCode;

    string avatarChangeDisplayName;
    string avatarChangeAvatarID;

    bool requestingOwnership;
    bool bufferedDeserializationComplete;

    public DebugLogger debug;

    void Start()
    {
        if (Networking.LocalPlayer.isMaster)
            bufferedDeserializationComplete = true;
        webManager._u_RegisterCallbackReceiver(this);
    }

    public override void OnDeserialization()
    {
        if (!bufferedDeserializationComplete)
        {
            bufferedDeserializationComplete = true;
            avatarChangeDeserializations = avatarChangeSerializations;
            _u_PickNewBroker();
            return;
        }

        if (avatarChangeSerializations != avatarChangeDeserializations)
        {
            avatarChangeDeserializations = avatarChangeSerializations;
            string[] namesSplit = displayName.Split('\n');
            string[] idsSplit = avatarID.Split('\n');
            for (int i=0; i<namesSplit.Length; i++)
                _u_ReceiveAvatarChange(namesSplit[i], idsSplit[i]);
        }
    }

    bool _u_PlayerIsOnlineAndAvailable(VRCPlayerApi player)
    {
        UdonSharpBehaviour[] usbs = pool._u_GetPlayerData(player);
        if (usbs == null) 
            return false; // case where pool is not initialized yet

        SlotDataOnlineStatus playerOnlineStatus = (SlotDataOnlineStatus)usbs[onlineDataIndexInPoolSlots];
        return playerOnlineStatus.online && playerOnlineStatus.connectionsOpen < 255;
    }

    void _u_PickNewBroker()
    {
        // Pick a new broker for chat, only switch owners if necessary
        if (!_u_PlayerIsOnlineAndAvailable(Networking.GetOwner(gameObject)) && webManager.online)
        {
            // Go through synced order list of players so web connected connected clients
            // agree on who should be the new broker.
            // Could improve this by prioritizing the first online person with the fewest connections open
            VRCPlayerApi[] players = pool._u_GetPlayersOrdered();
            if (players == null)
            {
                debug._u_Log("[AvatarChangeBroadcaster] Error: ordered players array not initialized yet");
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
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        if (requestingOwnership)
            requestingOwnership = false;
        else if (player == Networking.LocalPlayer)
            _u_PickNewBroker(); // Master reset
    }

    public void OnPostSerialization(VRC.Udon.Common.SerializationResult result)
    {
        displayName = "";
        avatarID = "";
    }

    public void _u_OnUdonMIDIWebHandlerOnlineChanged()
    {
        _u_PickNewBroker();
    }

    public void _u_OnAvatarChanged(/* string avatarChangeDisplayName, string avatarChangeAvatarID */)
    {
        if (Networking.IsOwner(gameObject))
        {
            // Separate strings by a reserved character because string[] sync is broken
            displayName += avatarChangeDisplayName + "\n";
            avatarID += avatarChangeAvatarID + "\n";
            avatarChangeSerializations++;
            RequestSerialization();
            _u_ReceiveAvatarChange(avatarChangeDisplayName, avatarChangeAvatarID);
        }
    }

    void _u_ReceiveAvatarChange(string dn, string id)
    {
        debug._u_Log("[AvatarChangeBroadcaster] " + displayName + " changed into " + avatarID);
    }
}
