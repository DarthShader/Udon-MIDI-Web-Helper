using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using System;

// by Kaj
// originally released as part of https://github.com/DarthShader/Udon-MIDI-Web-Helper

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class SlotDataNetworkEventWithArgsExample : UdonSharpBehaviour
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

    [UdonSynced]
    byte[] networkEventsSerialized;
    int currentOffset;
    [UdonSynced]
    uint networkEventsSerializations;
    uint networkEventsDeserializations;

    public DebugLogger debug;

    // Currently in Udon, deserialization of a behavior will fail to call (or fail to transmit?) if
    // the serialized data contains a null array.  Therefore, to make sure all data is successfully
    // deserialized at least once to count buffered deserialization, the first master calls this function
    // on Start to initialze any synced arrays to zero length.
    // https://feedback.vrchat.com/udon-networking-update/p/null-arrays-breaks-syncing
    void _u_InitializeSyncedArrays()
    {
        networkEventsSerialized = new byte[0];
    }

    // Full deserialization of all objects in the pool is guaranteed to be finished, this 
    // data and its parent slot have been claimed by a player.  Initialize any custom 
    // per-player variable data and activate local events that won't be activated by remote 
    // deserialization.  Also activate any events that need to be run once post buffered deserialization.
    // Called locally by the new owner of the slot.
    void _u_Initialize()
    {
        networkEventsSerialized = new byte[0];
    }

    // This slot has been unclaimed by the master after the owner left the world.  
    // Reset any custom variables that need to be serialized and activate any events that 
    // won't be activated by remote deserialization.
    // Called locally by the instance master.
    void _u_Reset()
    {
        networkEventsSerialized = new byte[0];
    }

    // Variables have been deserialized for a late joiner.  Pass along any data
    // or activate any triggers.  Also update deserialization count variables so triggers using
    // those counts aren't mistakenly actvivated twice.
    // Called locally by late joiners receiving the first deserialiation for the behavior.
    void _u_OnBufferedDeserialization()
    {
        networkEventsDeserializations = networkEventsSerializations;
    }

    // Regular variable deserialization
    void _u_OnDeserialization()
    {
        if (networkEventsDeserializations != networkEventsSerializations)
        { 
            // Extract all events from networkEventsSerialized
            currentOffset = 0;
            while (currentOffset < networkEventsSerialized.Length)
            {
                byte eventID = networkEventsSerialized[currentOffset++];
                int targetPlayersLength = (int)networkEventsSerialized[currentOffset++];
                byte[] targetPlayersBySlotIndex = new byte[targetPlayersLength];
                Array.Copy(networkEventsSerialized, currentOffset, targetPlayersBySlotIndex, 0, targetPlayersLength);
                currentOffset += targetPlayersLength;
                int floatCharsCount = networkEventsSerialized[currentOffset++];
                char[] floatStringChars = new char[floatCharsCount];
                for (int i=0; i<floatStringChars.Length; i++)
                    floatStringChars[i] = (char)networkEventsSerialized[currentOffset++];
                string floatParsed = new string(floatStringChars);
                float argument1 = Single.Parse(floatParsed);
                int argument2 = _u_BitConverterToInt32(networkEventsSerialized, currentOffset);
                currentOffset += 4;

                _u_ReceiveNetworkEventWithArguments(eventID, targetPlayersBySlotIndex, argument1, argument2);
            }
        }
    }

    void _u_ReceiveNetworkEventWithArguments(byte eventID, byte[] targetPlayersBySlotIndex, float argument1, int argument2)
    {
        // Only players who are targets of the event should execute it
        byte localSlotId = (byte)slot.pool._u_GetPlayerSlotIndex(Networking.LocalPlayer);
        if (Array.IndexOf(targetPlayersBySlotIndex, localSlotId) != -1)
        {
            // Execute event eventID with argument1 and argument2
            debug._u_Log("Received event " + eventID + " from " + Networking.GetOwner(gameObject).displayName + " with arguments " + argument1 + " and " + argument2);
        }
    }

    public void _u_SendNetworkEventWithArguments(byte eventID, byte[] targetPlayersBySlotIndex, float argument1, int argument2)
    {
        // Don't serialize anything if you're the only player in the instance.
        // OnPostSerialization will never be called and the network event buffer will never empty
        if (VRCPlayerApi.GetPlayerCount() == 1)
            return;

        // Manually serialize network event and append it to networkEventsSerialized
        // 1 eventID + targetPlayersBySlotIndex + 1 targetPlayersBySlotIndex length + argument1 as string length + 1 byte length + 4 argument2
        string floatString = argument1.ToString();
        byte[] networkEvent = new byte[7 + targetPlayersBySlotIndex.Length + floatString.Length];
        networkEvent[0] = eventID;
        networkEvent[1] = (byte)targetPlayersBySlotIndex.Length;
        Array.Copy(targetPlayersBySlotIndex, 0, networkEvent, 2, targetPlayersBySlotIndex.Length);
        int offset = 2 + targetPlayersBySlotIndex.Length;
        // System.BitConverter is not exposed in Udon, and neither is System.Buffer.BlockCopy
        // For now we have to resort to converting to converting floats to strings for byte array serialization
        networkEvent[offset++] = (byte)floatString.Length; // max 255 length as string
        for (int i=0; i<floatString.Length; i++)
            networkEvent[offset++] = (byte)floatString[i];
        // int conversion to bytes is easy and efficient enough to replicate in Udon
        byte[] intBytes = _u_BitConverterToByteArray(argument2);
        foreach (byte b in intBytes)
            networkEvent[offset++] = b;

        // Extend existing array
        byte[] newNetworkEventsArray = new byte[networkEventsSerialized.Length + networkEvent.Length];
        Array.Copy(networkEventsSerialized, newNetworkEventsArray, networkEventsSerialized.Length);
        networkEventsSerialized = newNetworkEventsArray;
        // Copy to network events array and serialize
        Array.Copy(networkEvent, 0, networkEventsSerialized, currentOffset, networkEvent.Length);
        currentOffset += networkEvent.Length;
        networkEventsSerializations++;
        RequestSerialization();
    }

    public override void OnPreSerialization()
    {
        //debug._u_Log("[SlotDataNetworkEventWithArgsExample] " + gameObject.name + " OnPreSerialization");
    }

    public void OnPostSerialization(VRC.Udon.Common.SerializationResult result)
    {
        //debug._u_Log("[SlotDataNetworkEventWithArgsExample] " + gameObject.name + " OnPostSerialization: " + result.success + " " + result.byteCount);
        networkEventsSerialized = new byte[0];
        currentOffset = 0;
    }

    // SystemBitConverter.__ToInt32__SystemByteArray_SystemInt32__SystemInt32 is not exposed in Udon
    // Buffer.BlockCopy is also not whitelisted.
    int _u_BitConverterToInt32(byte[] data, int startIndex)
    {
        int result = data[startIndex++];
        result |= data[startIndex++] << 8;
        result |= data[startIndex++] << 16;
        result |= data[startIndex++] << 24;
        return result;
    }

    byte[] _u_BitConverterToByteArray(int i)
    {
        byte[] result = new byte[4];
        result[0] = (byte)(i & 0xFF);
        result[1] = (byte)((i >> 8) & 0xFF);
        result[2] = (byte)((i >> 16) & 0xFF);
        result[3] = (byte)((i >> 24) & 0xFF);
        return result;
    }
}