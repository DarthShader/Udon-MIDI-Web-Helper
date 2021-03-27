using System;
using TobiasErichsen.teVirtualMIDI;

namespace Udon_MIDI_Web_Helper
{
    class MIDIFrame
    {
        // This object represents a buffer of 3-byte MIDI commands holding up to the maximum
        // amount of MIDI commands VRChat can receive in a single game tick without crashing the game.
        // MIDI commands are guaranteed in-order and error free, but whole frames are not reliably transmissable due to 
        // VRChat's buggy MIDI implementation.
        // Each frame needs to be verified as received by Udon, and Udon should notify that a full game tick has
        // passed before sending a new frame.  Otherwise two simple MIDI frames could crash the game.

        public const int VRC_MAX_BYTES_PER_UPDATE = 255;
        public const int MIDI_BYTES_PER_COMMAND = 3;
        public const int COMMANDS_PER_FRAME = VRC_MAX_BYTES_PER_UPDATE / MIDI_BYTES_PER_COMMAND; // 85
         // 2 bytes per command (170) + 1 extra byte every 4 commands (max 21) + 10 command type bytes - 1 connectionID byte
        public const int USABLE_BYTES_PER_FRAME = 200;

        byte[] buffer;
        int currentOffset = 0;

        public enum MIDICommandType
        {
            NoteOff,
            NoteOn,
            ControlChange
        }

        public MIDIFrame()
        {
            buffer = new byte[VRC_MAX_BYTES_PER_UPDATE];
        }

        public void AddHeader2(byte connectionID, byte a, bool flipFlop)
        {
            // Each MIDI frame's first command is a header command with a byte specifying
            // which connection this data is for.  MIDI frame headers should be sent
            // with an alternating bit to prevent the single race condition this system is prone to.
            if (flipFlop)
                Add(connectionID, a, 0, MIDICommandType.ControlChange);
            else
                Add(connectionID, a, 1, MIDICommandType.ControlChange);
        }

        public void Add199Bytes(byte[] bytes)
        {
            // The last 199 bytes in a frame should be added with this method, the first
            // byte should be used with AddHeader

            if (bytes.Length != 199)
                throw new ArgumentOutOfRangeException();

            // Encode 8 bytes into the channel, note, and velocity of each command.
            // Every 8 bytes, an extra byte can be packed in the 4 command's utilBits (2 high bits of the channel)
            // Every 18 bytes (8 commands: 16 bytes + 2 utilBits bytes), an extra byte can be added by using the commandType as a bit slot
            for (int i=0; i<190; i+=19)
            {
                Add(bytes[i],    bytes[i+1],   bytes[i+8] & 0x3,         (bytes[i+18] & 0x1)  == 0x0 ? MIDICommandType.NoteOn : MIDICommandType.NoteOff);
                Add(bytes[i+2],  bytes[i+3],  (bytes[i+8] & 0xC) >> 2,   (bytes[i+18] & 0x2)  == 0x0 ? MIDICommandType.NoteOn : MIDICommandType.NoteOff);
                Add(bytes[i+4],  bytes[i+5],  (bytes[i+8] & 0x30) >> 4,  (bytes[i+18] & 0x4)  == 0x0 ? MIDICommandType.NoteOn : MIDICommandType.NoteOff);
                Add(bytes[i+6],  bytes[i+7],  (bytes[i+8] & 0xC0) >> 6,  (bytes[i+18] & 0x8)  == 0x0 ? MIDICommandType.NoteOn : MIDICommandType.NoteOff);

                Add(bytes[i+9],  bytes[i+10],  bytes[i+17] & 0x3,        (bytes[i+18] & 0x10) == 0x0 ? MIDICommandType.NoteOn : MIDICommandType.NoteOff);
                Add(bytes[i+11], bytes[i+12], (bytes[i+17] & 0xC) >> 2,  (bytes[i+18] & 0x20) == 0x0 ? MIDICommandType.NoteOn : MIDICommandType.NoteOff);
                Add(bytes[i+13], bytes[i+14], (bytes[i+17] & 0x30) >> 4, (bytes[i+18] & 0x40) == 0x0 ? MIDICommandType.NoteOn : MIDICommandType.NoteOff);
                Add(bytes[i+15], bytes[i+16], (bytes[i+17] & 0xC0) >> 6, (bytes[i+18] & 0x80) == 0x0 ? MIDICommandType.NoteOn : MIDICommandType.NoteOff);
            }

            // Last 9 bytes
            const int j = 190;
            Add(bytes[j],   bytes[j+1],  bytes[j+8] & 0x3);
            Add(bytes[j+2], bytes[j+3], (bytes[j+8] & 0xC) >> 2);
            Add(bytes[j+4], bytes[j+5], (bytes[j+8] & 0x30) >> 4);
            Add(bytes[j+6], bytes[j+7], (bytes[j+8] & 0xC0) >> 6);
        }

        private void Add(byte a, byte b, int extraBits = 0, MIDICommandType type = MIDICommandType.NoteOn)
        {
            // Since MIDI commands' note and velocity bytes only have 7 usable bits,
            // the highest bits of each two bytes are moved into the lowest two bits
            // of the MIDI channel.
            int channelA = (a & 0x80) >> 7;
            int channelB = (b & 0x80) >> 6;
            int channelHigh = (extraBits & 0x3) << 2;
            int channel = channelHigh | channelA | channelB;
            AddMIDICommand(type, channel, a & 0x7F, b & 0x7F);
        }

        private void AddMIDICommand(MIDICommandType type, int channel, int note, int velocity)
        {
            // Each of the three MIDI commands supported by VRChat is composed of three bytes.
            // The first four bits of the first byte determine the command type (NoteOn/NoteOff/ControlChange).
            // The last four bits of the first byte determine the channel (instrument) the command is targeting.
            // The second byte's 7 primary bits determine the note to be played.
            // The last byte's 7 primary bits detemrine the velocity of the command.

            if (currentOffset > buffer.Length - MIDI_BYTES_PER_COMMAND)
                throw new InvalidOperationException("MIDICommandChunk is full");

            switch (type)
            {
                case MIDICommandType.NoteOff:
                    buffer[currentOffset] = 0x80;
                    break;
                case MIDICommandType.NoteOn:
                    buffer[currentOffset] = 0x90;
                    break;
                case MIDICommandType.ControlChange:
                    buffer[currentOffset] = 0xB0;
                    break;
            }
            buffer[currentOffset++] |= (byte)channel;
            buffer[currentOffset++] = (byte)note;
            buffer[currentOffset++] = (byte)velocity;
        }

        public void Send(TeVirtualMIDI port)
        {
            port.sendCommand(buffer);
        }
    }
}
