using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using System;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class UintScoreboardExample : UdonSharpBehaviour
{
    // This would have been a more modular IComparable scoreboard but
    // that doesn't work in Udon

    public InputField output;
    public SlotPool pool;
    public int dataObjectIndexInSlot;
    public string dataVariableName;

    VRCPlayerApi[] players;
    uint[] scores;
    VRCPlayerApi[] playersSorted;
    uint[] scoresSorted;
    bool[] maxUsedSortFlag;

    public DebugLogger debug;

    void Start()
    {
        pool._u_RegisterCallbackReceiver(this);
    }

    public void _u_OnPoolSlotsChanged()
    {
        players = pool._u_GetPlayersOrdered();
        playersSorted = new VRCPlayerApi[players.Length];
        Array.Copy(players, playersSorted, players.Length);
        scores = new uint[players.Length];
        scoresSorted = new uint[players.Length];
        maxUsedSortFlag = new bool[players.Length];

        debug._u_Log("[SlotDataUintScoreboardExample] _u_OnPoolSlotsChanged: " + players.Length);

        _u_RebuildScores();
    }

    public void _u_RebuildScores()
    {
        for (int i=0; i<players.Length; i++)
            scores[i] = (uint)((UdonSharpBehaviour)pool._u_GetPlayerData(players[i])[dataObjectIndexInSlot]).GetProgramVariable(dataVariableName);

        // Udon more like Udon't
        // https://feedback.vrchat.com/vrchat-udon-closed-alpha-feedback/p/arraysort
        //Array.Copy(players, playersSorted, players.Length);
        //Array.Copy(scores, scoresSorted, scores.Length);
        //Array.Sort(scoresSorted, playersSorted);
        //Array.Sort(scoresSorted);

        // Manually n^2 sort players and scores into playersSorted and scoresSorted
        Array.Clear(maxUsedSortFlag, 0, maxUsedSortFlag.Length);
        for (int i=0; i<players.Length; i++)
        {
            int maxScoreIndex = 0;
            uint maxScore = 0;
            for (int j=0;j<players.Length;j++)
                if (scores[j] >= maxScore && !maxUsedSortFlag[j])
                {
                    maxScoreIndex = j;
                    maxScore = scores[j];
                }
            maxUsedSortFlag[maxScoreIndex] = true;
            playersSorted[i] = players[maxScoreIndex];
            scoresSorted[i] = maxScore;
        }

        output.text = "";
        for (int i=0; i<players.Length; i++)
            output.text += playersSorted[i].displayName + ": " + scoresSorted[i] + "\n";
    }
}
