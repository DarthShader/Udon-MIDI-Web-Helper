using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;

public class UdonWebRequestExample : UdonSharpBehaviour
{
    byte[] receivedData = null;
    int currentOffset = 0;

    void MidiNoteOn(int channel, int number, int velocity)
    {
        // MIDI command data will be appended
        // to receivedData until the expected length array has been filled.
        // Each MIDI command effectively only has 19 usable bits, so this multiplexer 
        // uses 16 of those to assemble two bytes per command - the
        // lowest two bits of the channel are placed into the unused highest two
        // bits of number and velocity to make two full bytes.

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
        // similar to NoteOn. Both bytes are combined to form a 0-65k value.

        int byteA = (int)(((channel & 0x1) << 7) | number);
        int byteB = (int)(((channel & 0x2) << 6) | velocity);
        int receivedDataLen = (byteA << 8) | byteB;
        receivedData = new byte[receivedDataLen];
        currentOffset = 0;
    }

    void WebRequestReceived()
    {
        // use receivedData somehow
        // string result = System.Text.Encoding.Default.GetString(receivedData); // Not exposed in Udon
        char[] characters = new char[receivedData.Length];
        for (int i=0; i<characters.Length; i++)
            characters[i] = (char)receivedData[i];
        string result = new string(characters);

    }

    void SendWebRequest(string url)
    {
        Debug.Log("[Udon-MIDI-HTTP-Helper] " + url);
    }
}
