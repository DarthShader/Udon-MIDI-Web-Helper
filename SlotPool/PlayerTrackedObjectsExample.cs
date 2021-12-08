
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class PlayerTrackedObjectsExample : UdonSharpBehaviour
{
    public SlotPool pool;
    GameObject[] trackedGameobjects;
    VRCPlayerApi[] players;
    int[] claimedSlots;

    public DebugLogger debug;

    void Start()
    {
        debug._u_Log("[PlayerTrackedObjectsExample] Start");

        trackedGameobjects = new GameObject[transform.childCount];
        for (int i=0; i<trackedGameobjects.Length; i++)
            trackedGameobjects[i] = transform.GetChild(i).gameObject;
        claimedSlots = new int[0];
        pool._u_RegisterCallbackReceiver(this);
    }

    void Update()
    {
        for (int i=0; i<claimedSlots.Length; i++)
        {
            if (Utilities.IsValid(players[i]))
            {
                VRCPlayerApi.TrackingData td = players[i].GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                trackedGameobjects[claimedSlots[i]].transform.position = td.position + new Vector3(0, 1, 0);
            }
        }
    }

    public void _u_OnPoolSlotsChanged()
    {
        debug._u_Log("[PlayerTrackedObjectsExample] _u_OnPoolSlotsChanged");

        // Ideally only the gameobjects necessary should be toggled
        // on/off as to not interrupt udon scripts on them.  However,
        // syncing slots/claims is not currently reliable so a full reset
        // every time is safest

        // Turn all gameobjects off
        foreach (GameObject go in trackedGameobjects)
            go.SetActive(false);

        // Turn on gameobjects corresponding to claimed slots
        players = pool._u_GetPlayersOrdered();
        claimedSlots = new int[players.Length];
        for (int i=0; i<players.Length; i++)
        {
            int index = pool._u_GetPlayerSlotIndex(players[i]);
            claimedSlots[i] = index;
            trackedGameobjects[index].SetActive(true);
        }
    }
}
