using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;

// by Kaj
// originally released as part of https://github.com/DarthShader/Udon-MIDI-Web-Helper

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class Slot : UdonSharpBehaviour
{
    // readyonly
    [HideInInspector]
    public SlotPool pool;
    // readyonly
    [HideInInspector]
    public UdonSharpBehaviour[] dataObjects;
    int dataObjectDeserializationCount;
    bool startFinished;
    bool bufferedDeserializationFinished;
    bool dataObjectsDeserializationFinished;
    bool requestingOwnership;

    [UdonSynced, HideInInspector]
    public bool claimed;
    [UdonSynced]
    uint claimedSerializationCount; // for OnDeserialization to determine if the var changed
    uint claimedDeserializationCount; // "check new data against old data and make specific updates"

    public DebugLogger debug;

    void Start()
    {
        // Extra logging is enabled because, as of build 1159, there is a NEW bug with object ownership transfer
        // that is still causing players to not properly transfer ownership.  Repro: a lot of players join a world an once with this pool
        debug._u_Log("[Slot] "  + gameObject.name + " Start");

        pool = transform.parent.GetComponent<SlotPool>();
        dataObjects = new UdonSharpBehaviour[transform.childCount];
        for (int i=0; i<dataObjects.Length; i++)
            dataObjects[i] = (UdonSharpBehaviour)(transform.GetChild(i).GetComponent(typeof(UdonSharpBehaviour)));
        startFinished = true;

        // If you're the first person in the instance, dummy serialize once so buffered deserializations can be
        // counted by slots for all late joiners to know when full deserialization is complete.
        if (Networking.IsMaster)
        {
            bufferedDeserializationFinished = true;
            dataObjectsDeserializationFinished = true;
            pool._u_ReportBufferedDeserialization();
            RequestSerialization();
        }
        else _u_TryReportBufferedDeserialization();
    }

    // Called locally from PlayerObjectPool by the player who wants to be the new owner.
    public void _u_TakeOwnership()
    {
        debug._u_Log("[Slot] "  + gameObject.name + " _u_TakeOwnership");

        // Special case for the first player to join an instance.
        // They already own everything so ownership is never transferred.
        if (Networking.IsMaster)
            _u_ChangeClaimAndSerialize(true);
        else
        {
            requestingOwnership = true;
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
            // https://feedback.vrchat.com/udon-networking-update/p/please-provide-an-onownershiptransferfailed-or-the-equivalent-information
            // Failed to take ownership due to a race condition, try agian
            if (!Networking.IsOwner(gameObject))
            {
                debug._u_Log("[Slot] "  + gameObject.name + " Take ownership failed!");
                requestingOwnership = false;
                pool._u_GetNextFreeSlot();
            }
            debug._u_Log("[Slot] "  + gameObject.name + " _u_TakeOwnership completed");
        }
    }

    // There is currently a bug where serialized data isn't guaranteed to be delivered/received after
    // a player takes ownership of an object and immediately serializes, but only if OnOwnershipRequest is implemented.
    // Confirmed by Phasedragon.
    // https://feedback.vrchat.com/vrchat-udon-closed-alpha-bugs/p/setting-ownership-to-someone-else-in-the-same-tick-as-requestserialization-cause
    // Fixed(?) as of build 1159
    public override bool OnOwnershipRequest(VRCPlayerApi requester, VRCPlayerApi newOwner)
    {
        VRCPlayerApi currentOwner = Networking.GetOwner(gameObject);
        debug._u_Log("[Slot] " + gameObject.name + " OnOwnershipRequest - owner: " + currentOwner.playerId + " requester: " + requester.playerId + " newOwner: " + newOwner.playerId);
        if (currentOwner.isLocal)
        {
            // This blocking behavior ensures that only the first requester gets slot ownership from the master
            if (claimed || requestingOwnership)
                return false;

            // Anti-cheat behavior
            // Idk how the ownership transfer netcode works, but assuming current owner denying a transfer request 
            // isn't just a gesture of good faith that a malicious requester can ignore, this should act as a form of anti-cheat
            if (!Networking.IsMaster || pool._u_PlayerOwnsSlot(newOwner) || requester != newOwner)
                return false;
        }
        return true;
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        debug._u_Log("[Slot] " + gameObject.name + " OnOwnershipTransferred to player " + player.displayName);
        if (requestingOwnership && player.isLocal)
        {
            claimed = true;
            requestingOwnership = false;
            _u_ChangeClaimAndSerialize(true);
        }
        // If this slot was previously claimed, a slot is being reclaimed by the instance master
        else if (claimed && Networking.IsMaster)
            _u_ChangeClaimAndSerialize(false);
    }

    void _u_ChangeClaimAndSerialize(bool claim)
    {
        debug._u_Log("[Slot] " + gameObject.name + " _u_ChangeClaimAndSerialize");
        claimed = claim;
        claimedSerializationCount++;
        RequestSerialization();
        //debug._u_Log("[Slot] " + gameObject.name + " Requested Serialization " + claimedSerializationCount + " " + claimedDeserializationCount);
        pool._u_RebuildSlotsMap();
        // Since this object is now under the local player's control,
        // it is safe to take ownership of all child data objects.
        foreach (UdonSharpBehaviour usb in dataObjects)
            usb.SendCustomEvent("_u_TakeOwnership");
    }

    public override void OnPreSerialization()
    {
        debug._u_Log("[Slot] " + gameObject.name + " OnPreSerialization");
    }

    public void OnPostSerialization(VRC.Udon.Common.SerializationResult result)
    {
        debug._u_Log("[Slot] " + gameObject.name + " OnPostSerialization: " + result.success + " " + result.byteCount);
    }

    // Called for remote players only
    public override void OnDeserialization()
    {
        debug._u_Log("[Slot] " + gameObject.name + " OnDeserialization: " 
        + bufferedDeserializationFinished + " " + claimedSerializationCount + " " + claimedDeserializationCount);

        if (!bufferedDeserializationFinished)
        {
            bufferedDeserializationFinished = true;
            _u_TryReportBufferedDeserialization();
            claimedDeserializationCount = claimedSerializationCount;
            return;
        }

        // Only perform claim related updates if claim was changed
        if (claimedSerializationCount != claimedDeserializationCount)
        {
            debug._u_Log("[Slot] " + gameObject.name + " claim deserialized");
            claimedDeserializationCount = claimedSerializationCount;
            pool._u_RebuildSlotsMap();
        }
    }

    // Called by all child data objects so this slot knows when full deserialization
    // is complete, so the pool as a whole knows when full deserialization is complete,
    // so a free slot can be claimed, so that slot's data can be initialized.
    public void _u_ReportBufferedDeserialization()
    {
        debug._u_Log("[Slot] " + gameObject.name + " _u_ReportBufferedDeserialization");

        dataObjectDeserializationCount++;
        _u_TryReportBufferedDeserialization();
    }

    void _u_TryReportBufferedDeserialization()
    {
        debug._u_Log("[Slot] " + gameObject.name + " _u_TryReportBufferedDeserialization");

        if (!dataObjectsDeserializationFinished && startFinished && bufferedDeserializationFinished && dataObjectDeserializationCount == dataObjects.Length)
        {
            dataObjectsDeserializationFinished = true;
            //debug._u_Log("[Slot] " + gameObject.name + " reporting buffered deserialization to pool");
            pool._u_ReportBufferedDeserialization();
        }
    }
}