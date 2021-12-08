
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class RandomNetworkEventWithArgsButtonExample : UdonSharpBehaviour
{
    public SlotPool pool;
    public int networkEventExampleIndexInSlots;

    SlotDataNetworkEventWithArgsExample networkEventHandler;

    public DebugLogger debug;

    void Start()
    {
        pool._u_RegisterCallbackReceiver(this);
    }

    public override void Interact()
    {
        byte eventID = (byte)Random.Range(0, 255);
        // Pick one random player to be the target
        byte[] targets = new byte[1];
        VRCPlayerApi[] players = pool._u_GetPlayersOrdered();
        int targetPlayerIndex;
        do
            targetPlayerIndex = Random.Range(0, players.Length);
        while (players[targetPlayerIndex] == Networking.LocalPlayer);
        VRCPlayerApi targetPlayer = players[targetPlayerIndex];
        targets[0] = (byte)pool._u_GetPlayerSlotIndex(targetPlayer);
        float floatArgument = Random.Range(-100f, 100f);
        int intArgument = Random.Range(-100, 100);

        debug._u_Log("Sending event " + eventID + " with arguments " + floatArgument + " and " + intArgument + " to player " + targetPlayer.displayName);
        networkEventHandler._u_SendNetworkEventWithArguments(eventID, targets, floatArgument, intArgument);
    }

    public void _u_OnPoolSlotsChanged()
    {
        networkEventHandler = (SlotDataNetworkEventWithArgsExample)pool._u_GetPlayerData(Networking.LocalPlayer)[networkEventExampleIndexInSlots];
    }
}
