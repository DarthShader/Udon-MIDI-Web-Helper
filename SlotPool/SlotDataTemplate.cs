using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;

// by Kaj
// originally released as part of https://github.com/DarthShader/Udon-MIDI-Web-Helper

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class SlotDataTemplate : UdonSharpBehaviour
{
    #region BaseClass

    Slot slot;
    bool requestingOwnership;
    bool bufferedDeserializationFinished;

    void Start()
    {
        slot = transform.parent.GetComponent<Slot>();

        // First person to join the instance dummy serializes
        // the data so the parent slot can known when all child
        // data objects have been fully deserialized.
        if (Networking.IsMaster)
        {
            bufferedDeserializationFinished = true;
            slot._u_ReportBufferedDeserialization();
            _u_InitializeSyncedArrays();
            RequestSerialization();
        }
    }

    // Called locally by the parent slot after the slot has been claimed
    public void _u_TakeOwnership()
    {
        if (Networking.IsMaster)
            _u_Initialize();
        else
        {
            requestingOwnership = true;
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }
    }

    public override bool OnOwnershipRequest(VRCPlayerApi requester, VRCPlayerApi newOwner)
    {
        VRCPlayerApi currentOwner = Networking.GetOwner(gameObject);
        if (currentOwner.isLocal)
        {
            // Anti-cheat behavior
            if (!Networking.IsMaster || requester != newOwner)
                return false;
        }
        return true;
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        if (requestingOwnership)
        {
            requestingOwnership = false;
            _u_Initialize();
        }
        else if (player.isMaster && Networking.IsMaster)
            _u_Reset(); // Master has reclaimed a slot and some data objects after a player left
    }

    // Called for remote players only
    // cnlohr says deserialization is UNREILABLE, contrary to the docs
    public override void OnDeserialization()
    {
        if (!bufferedDeserializationFinished)
        {
            bufferedDeserializationFinished = true;
            _u_OnBufferedDeserialization();
            slot._u_ReportBufferedDeserialization();
        }
        else _u_OnDeserialization();
    }

    #endregion
    
    // ===================================================================================================
    // Sub-class

    // Your variables here
    // Requires at least one synced variable!

    // Currently in Udon, deserialization of a behavior will fail to call (or fail to transmit?) if
    // the serialized data contains a null array.  Therefore, to make sure all data is successfully
    // deserialized at least once to count buffered deserialization, the first master calls this function
    // on Start to initialze any synced arrays to zero length.
    // https://feedback.vrchat.com/udon-networking-update/p/null-arrays-breaks-syncing
    void _u_InitializeSyncedArrays()
    {
         
    }

    // Full deserialization of all objects in the pool is guaranteed to be finished, this 
    // data and its parent slot have been claimed by a player.  Initialize any custom 
    // per-player variable data and activate local events that won't be activated by remote 
    // deserialization.  Also activate any events that need to be run once post buffered deserialization.
    // Called locally by the new owner of the slot.
    void _u_Initialize()
    {
        
    }

    // This slot has been unclaimed by the master after the owner left the world.  
    // Reset any custom variables that need to be serialized and activate any events that 
    // won't be activated by remote deserialization.
    // Called locally by the instance master.
    void _u_Reset()
    {
        
    }

    // Variables have been deserialized for a late joiner.  Pass along any data
    // or activate any triggers.  Also update deserialization count variables so triggers using
    // those counts aren't mistakenly actvivated twice.
    // Called locally by late joiners receiving the first deserialiation for the behavior.
    void _u_OnBufferedDeserialization()
    {
        
    }

    // Regular variable deserialization, received by everyone but the object owner
    void _u_OnDeserialization()
    {
        
    }

    // Other added events need to drive change of the data and do RequestSerialization()

}