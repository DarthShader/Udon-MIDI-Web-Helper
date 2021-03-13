using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;

public class UdonWebRequestExample : UdonSharpBehaviour
{
    byte[] receivedData = null;
    int currentOffset = 0;

    bool firstNoteOffFlag = true;
    int NoteOffLen = 0;

    float startTime = 0;

    void MidiNoteOn(int channel, int number, int velocity)
    {
        // MIDI command data will be appended
        // to receivedData until the expected length array has been filled.
        // Each MIDI command effectively only has 19 usable bits, so this multiplexer 
        // uses 16 of those to assemble two bytes per command - the
        // lowest two bits of the channel are placed into the unused highest two
        // bits of number and velocity to make two full bytes.

        // Emergency escapes because VRChat's MIDI implementation will re-fire
        // previously activated NoteOn events from other worlds/on a world rejoin
        if (receivedData == null)
            return;
        if (currentOffset >= receivedData.Length)
            return;

        byte byteA = (byte)(((channel & 0x1) << 7) | number);
        byte byteB = (byte)(((channel & 0x2) << 6) | velocity);

        receivedData[currentOffset] = byteA;
        currentOffset++;
        if (currentOffset == receivedData.Length)
            WebRequestReceived();
        else
        {
            receivedData[currentOffset] = byteB;
            currentOffset++;
            if (currentOffset == receivedData.Length)
                WebRequestReceived();
        }
    }

    void MidiNoteOff(int channel, int number, int velocity)
    {
        // NoteOff is used to declare that a new web request result is being received,
        // along with the length of its data, as multiplexed bytes in a way
        // similar to NoteOn. Two sets of NoteOff commands are send to get a total of
        // 31 bits for the length of the data that is being received.
        int byteA = (int)(((channel & 0x1) << 7) | number);
        int byteB = (int)(((channel & 0x2) << 6) | velocity);
        int bits = (byteA << 8) | byteB;

        if (firstNoteOffFlag)
        {
            // Benchmarking tool
            startTime = Time.realtimeSinceStartup;

            // Receive highest 16 bits of the length
            NoteOffLen = bits << 16;
            firstNoteOffFlag = false;
        }
        else
        {
            // Receive lowest 16 bits of the length
            NoteOffLen |= bits;
            receivedData = new byte[NoteOffLen];
            currentOffset = 0;
            firstNoteOffFlag = true;
        }
    }

    void WebRequestReceived()
    {
        // Benchmarking tool
        float totalTime = Time.realtimeSinceStartup - startTime;

        // use receivedData
        //Texture2D tex = null;
        //ImageConversion.LoadImage(tex, receivedData, false);  // Not exposed in Udon, could be used to directly load pngs
        //output.text = System.Text.Encoding.Default.GetString(receivedData); // Not exposed in Udon
        char[] characters = new char[receivedData.Length];
        for (int i=0; i<characters.Length; i++)
            characters[i] = (char)receivedData[i];
        string result = new string(characters);
    }

    void SendWebRequest(string url)
    {
        Debug.Log("[Udon-MIDI-HTTP-Helper] " + url);
    }

    void Interact()
    {
        SendWebRequest(input.text);
    }
}
