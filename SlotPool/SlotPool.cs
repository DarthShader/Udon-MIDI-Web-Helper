using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using System;
using UnityEngine.UI;

// by Kaj
// originally released as part of https://github.com/DarthShader/Udon-MIDI-Web-Helper

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class SlotPool : UdonSharpBehaviour
{
    Slot[] slots;
    VRCPlayerApi[] slotPlayers;
    VRCPlayerApi[] orderedPlayers;
    int bufferedDeserializationCount;
    bool startFinished;
    bool bufferedDeserializationFinished;

    public DebugLogger debug;

    // Super quick lookup of player to data objects
    public UdonSharpBehaviour[] _u_GetPlayerData(VRCPlayerApi player)
    {
        debug._u_Log("[SlotPool] _u_GetPlayerData player:" + player.displayName);

        if (slotPlayers == null)
        {
            debug._u_Log("[SlotPool] Error: slotPlayers was not initialized yet");
            return null;
        }
        int index = Array.IndexOf(slotPlayers, player);
        if (index == -1)
        {
            debug._u_Log("[SlotPool] Error: player not in slotPlayers");
            return null;
        }
        return slots[index].dataObjects;
    }

    // Synced-order list of players, alternative to VRCPlayerApi.GetPlayers()
    // Good for making deterministic decisions across clients
    public VRCPlayerApi[] _u_GetPlayersOrdered()
    {
        debug._u_Log("[SlotPool] _u_GetPlayersOrdered");

        return orderedPlayers;
    }

    // Good for providing consistent indexes for players for managed pools
    // of unsynced per-player gameObjects, like custom nameplates that follow
    // individual players' heads.  Pairs well with the _u_OnPoolSlotsChanged callback.
    public int _u_GetPlayerSlotIndex(VRCPlayerApi player)
    {
        debug._u_Log("[SlotPool] _u_GetPlayerSlotIndex");

        if (slotPlayers == null)
        {
            debug._u_Log("[SlotPool] Error: slotPlayers was not initialized yet");
            return -1;
        }
        return Array.IndexOf(slotPlayers, player);
    }

    void Start()
    {
        debug._u_Log("[SlotPool] Start");

        slots = new Slot[transform.childCount];
        slotPlayers = new VRCPlayerApi[slots.Length];
        for (int i=0; i<slots.Length; i++)
            slots[i] = (Slot)(transform.GetChild(i).GetComponent(typeof(UdonSharpBehaviour)));

        // Since the order of Start() between this pool and the slot gameobject's isn't currently deterministic
        // in Udon, check to see if _u_BufferedDeserializationComplete needs to be called.
        startFinished = true;
        _u_TryBufferedDeserializationComplete();
    }
    
    // Called by slots once on first deserialization.  First instance master
    // is supposed to request serialization on all slots so fully buffered
    // deserialization can be counted to and reported.
    public void _u_ReportBufferedDeserialization()
    {
        debug._u_Log("[SlotPool] _u_ReportBufferedDeserialization");

        bufferedDeserializationCount++;
        _u_TryBufferedDeserializationComplete();
    }

    void _u_TryBufferedDeserializationComplete()
    {
        debug._u_Log("[SlotPool] _u_TryBufferedDeserializationComplete");

        if (!bufferedDeserializationFinished && startFinished && bufferedDeserializationCount == slots.Length)
        {
            debug._u_Log("[SlotPool] Buffered Deserialization Complete");
            // It's finally safe for the current player to try to claim a slot
            bufferedDeserializationFinished = true;
            _u_SendCallback("_u_OnPoolDeserializationComplete");
            _u_GetNextFreeSlot();
        }
    }

    public void _u_GetNextFreeSlot()
    {
        debug._u_Log("[SlotPool] _u_GetNextFreeSlot");
        // When a new player joins, they go through the slot list in order
        // and try to take ownership of the first one with "claimed" set to false.
        // If a player loses in a race condition, they will repeat this process until
        // they claim a slot.
        foreach (Slot s in slots)
            if (!s.claimed && Networking.GetOwner(s.gameObject).isMaster)
            {
                s._u_TakeOwnership();
                break;
            }
    }

    // Called locally when a slot's claim status has changed.
    public void _u_RebuildSlotsMap()
    {
        // Ownership of objects gets transferred back to master before a player leaves, and thus
        // GetPlayerCount reports that nothing has changed.
        // Manually counting claims should represent the accurate new player count
        int claimsCount = 0;
        foreach (Slot s in slots)
            if (s.claimed)
                claimsCount++;
        debug._u_Log("[SlotPool] _u_RebuildSlotsMap VRCPlayerCount: " + VRCPlayerApi.GetPlayerCount() + " claimsCount: " + claimsCount);

        orderedPlayers = new VRCPlayerApi[claimsCount];
        int j = 0;
        for (int i=0; i<slots.Length; i++)
        {
            if (slots[i].claimed)
            {
                VRCPlayerApi owner = Networking.GetOwner(slots[i].gameObject);
                slotPlayers[i] = owner;
                orderedPlayers[j++] = owner;
            }
            else slotPlayers[i] = null;
        }
        _u_SendCallback("_u_OnPoolSlotsChanged");
    }

    // Anti-cheat utility for object ownership, used by Slot class
    public bool _u_PlayerOwnsSlot(VRCPlayerApi player)
    {
        debug._u_Log("[SlotPool] _u_PlayerOwnsSlot player:" + player.displayName);

        foreach (Slot s in slots)
            if (Networking.GetOwner(s.gameObject) == player)
                return true;
        return false;
    }

// Callbacks:
// _u_OnPoolDeserializationComplete: all slot claims and their data objects have been deserialized.
// _u_OnPoolSlotsChanged: a player has joined or left.  Slot claims and player data has changed.
#region Callback Receivers
// Shamelessly taken from USharpVideoPlayer.  Thanks Merlin.  
// Edited to be copy/pastable between any UdonSharp behavior.
        UdonSharpBehaviour[] _registeredCallbackReceivers;
        public void _u_RegisterCallbackReceiver(UdonSharpBehaviour callbackReceiver)
        {
            if (!Utilities.IsValid(callbackReceiver))
                return;
            if (_registeredCallbackReceivers == null)
                _registeredCallbackReceivers = new UdonSharpBehaviour[0];
            foreach (UdonSharpBehaviour currReceiver in _registeredCallbackReceivers)
                if (callbackReceiver == currReceiver)
                    return;
            UdonSharpBehaviour[] newControlHandlers = new UdonSharpBehaviour[_registeredCallbackReceivers.Length + 1];
            _registeredCallbackReceivers.CopyTo(newControlHandlers, 0);
            _registeredCallbackReceivers = newControlHandlers;
            _registeredCallbackReceivers[_registeredCallbackReceivers.Length - 1] = callbackReceiver;
        }
        public void _u_UnregisterCallbackReceiver(UdonSharpBehaviour callbackReceiver)
        {
            if (!Utilities.IsValid(callbackReceiver))
                return;
            if (_registeredCallbackReceivers == null)
                _registeredCallbackReceivers = new UdonSharpBehaviour[0];
            int callbackReceiverCount = _registeredCallbackReceivers.Length;
            for (int i = 0; i < callbackReceiverCount; ++i)
            {
                UdonSharpBehaviour currHandler = _registeredCallbackReceivers[i];
                if (callbackReceiver == currHandler)
                {
                    UdonSharpBehaviour[] newCallbackReceivers = new UdonSharpBehaviour[callbackReceiverCount - 1];
                    for (int j = 0; j < i; ++j)
                        newCallbackReceivers[j] = _registeredCallbackReceivers[j];
                    for (int j = i + 1; j < callbackReceiverCount; ++j)
                        newCallbackReceivers[j - 1] = _registeredCallbackReceivers[j];
                    _registeredCallbackReceivers = newCallbackReceivers;
                    return;
                }
            }
        }
        void _u_SendCallback(string callbackName)
        {
            if (_registeredCallbackReceivers == null) 
                return;
            foreach (UdonSharpBehaviour callbackReceiver in _registeredCallbackReceivers)
                if (Utilities.IsValid(callbackReceiver))
                    callbackReceiver.SendCustomEvent(callbackName);
        }
#endregion

}