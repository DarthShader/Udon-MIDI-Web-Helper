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
        public const int USABLE_BYTES_PER_FRAME = 190; // 2 bytes per command (170) + 1 extra byte per 4 commands (max 21) - 1 connectionID byte

        byte[] buffer;
        int currentOffset = 0;
        int spaceAvailable = USABLE_BYTES_PER_FRAME;

        public int SpaceAvailable
        {
            get
            {
                return spaceAvailable;
            }
        }

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

        public void Add9Bytes(byte[] bytes)
        {
            // The last 189 bytes in a frame should be added with this method, the first
            // byte should be used with AddHeader

            if (bytes.Length != 9)
                throw new ArgumentOutOfRangeException();

            // Encode 8 bytes into the channel, note, and velocity
            // The last byte is placed into the high 2 bits of channel across 4 MIDI commands
            Add(bytes[0], bytes[1], bytes[8] & 0x3);
            Add(bytes[2], bytes[3], (bytes[8] & 0xC) >> 2);
            Add(bytes[4], bytes[5], (bytes[8] & 0x30) >> 4);
            Add(bytes[6], bytes[7], (bytes[8] & 0xC0) >> 6);
            spaceAvailable -= 9;
        }

        public void AddHeader(byte connectionID, byte a)
        {
            // Each MIDI frame's first command is a NoteOff command with a byte specifying
            // which connection this data is for.
            Add(connectionID, a, 0, MIDICommandType.NoteOff);
            spaceAvailable -= 1;
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
