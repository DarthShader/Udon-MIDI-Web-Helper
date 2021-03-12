using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Net;
using System.Linq;
using TobiasErichsen.teVirtualMIDI;

namespace Udon_MIDI_HTTP_Helper
{
	class UdonMIDIHTTPHelper
	{
		static TeVirtualMIDI port;
		static FileStream currentLog = null;
		static long previousFileLength = 0;
		static byte[] midiBuffer;
		static int midiBufferOffset = 0;

		const int VRC_MAX_MIDI_COMMANDS = 128;
		const int MIDI_BYTES_PER_COMMAND = 3;
		const int MAX_BYTES_PER_CHUNK = VRC_MAX_MIDI_COMMANDS * MIDI_BYTES_PER_COMMAND;
		const int TIMEOUT_BETWEEN_MIDI_CHUNKS_MILLIS = 20;

		enum MIDICommandType
		{
			NoteOff,
			NoteOn,
			ControlChange
		}

		static void Main()
		{
			// Print MIDI driver header
			Console.WriteLine("TeVirtualMIDI started");
			Console.WriteLine("using dll-version:    " + TeVirtualMIDI.versionString);
			Console.WriteLine("using driver-version: " + TeVirtualMIDI.driverVersionString);

			// Instantiate MIDI port
			port = new TeVirtualMIDI("Udon-MIDI-HTTP-Helper", MAX_BYTES_PER_CHUNK, TeVirtualMIDI.TE_VM_FLAGS_PARSE_TX | TeVirtualMIDI.TE_VM_FLAGS_INSTANTIATE_TX_ONLY);

			// Open latest output log
			string logFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low\\VRChat\\VRChat";
			try
			{
				var directory = new DirectoryInfo(logFolder);
				var logFile = directory.GetFiles().OrderByDescending(x => x.LastWriteTime).First();
				currentLog = File.Open(logFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				previousFileLength = currentLog.Length;
				Console.WriteLine("Output log switched: " + logFile.FullName);
			}
			catch (Exception e) { }

			// Watch for newer output log file in case game was restarted
			var watcher = new FileSystemWatcher(logFolder);
			watcher.NotifyFilter = NotifyFilters.FileName;
			watcher.Created += OnLogCreated;
			watcher.Filter = "output_log_*.txt";
			watcher.EnableRaisingEvents = true;

			// Start thread to read output log for web requests
			Console.WriteLine("Udon-MIDI-HTTP-Helper v3 Ready.  Press any key to close the program.");
			Thread thread = new Thread(new ThreadStart(LogParseThread));
			thread.Start();

			// Wait for program termination
			Console.ReadKey();
			thread.Abort();
			port.shutdown();
			if (currentLog != null) currentLog.Close();
		}

		static void OnLogCreated(object sender, FileSystemEventArgs e)
		{
			Console.WriteLine("Output log switched: " + e.FullPath);
			if (currentLog != null) currentLog.Close();
			currentLog = File.Open(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			previousFileLength = 0;
		}

		static void LogParseThread()
		{
			const int MAX_BYTES_PER_LINE = 10000000;
			const int LOG_POLL_MILLISECONDS = 1000;
			byte[] separatorSequence = Encoding.UTF8.GetBytes("\n\n\r\n");
			byte[] bytes = new byte[MAX_BYTES_PER_LINE];

			try
			{
				// loop and read new lines printed to the output log every LOG_POLL_MILLISECONDS
				while (true)
				{
					if (currentLog == null)
						continue;

					if (currentLog.Length != previousFileLength)
					{
						currentLog.Seek(previousFileLength, SeekOrigin.Begin);
						previousFileLength = currentLog.Length;
						int separatorMatchCount = 0;
						int byteArrayOffset = 0;
						while (currentLog.Position < previousFileLength)
						{
							// Read log byte-by-byte to more easily match the log's unique 4 byte line separator sequence
							// Individual log lines need to be isolated so individual Debug.Log calls from Udon can be reliably found
							// and to be sure the [Udon-MIDI-HTTP-Helper] tag wasn't spoofed by something else
							bytes[byteArrayOffset] = (byte)currentLog.ReadByte();
							if (bytes[byteArrayOffset] == separatorSequence[separatorMatchCount])
							{
								separatorMatchCount++;
								if (separatorMatchCount == separatorSequence.Length)
								{
									// Single log line found
									string line = Encoding.UTF8.GetString(bytes, 0, byteArrayOffset - separatorSequence.Length + 1);
									byteArrayOffset = 0;
									separatorMatchCount = 0;
									// Log lines are expected to be in this format:
									// 2021.01.01 00:00:00 Log        -  [Udon-MIDI-HTTP-Helper] https://127.0.0.1/test.html
									if (line.Length > 59 && line.Substring(34, 23) == "[Udon-MIDI-HTTP-Helper]")
									{
										Console.WriteLine(line);
										SendWebRequest(line.Substring(58));
									}
									continue;
								}
							}
							else separatorMatchCount = 0;
							byteArrayOffset++;
						}
					}
					Thread.Sleep(LOG_POLL_MILLISECONDS);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("Exception in work thread: " + ex.Message);
			}
		}

		static void SendWebRequest(string url)
		{
			try
			{
				HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
				request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

				using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
				using (Stream stream = response.GetResponseStream())
				using (StreamReader sr = new StreamReader(stream, Encoding.ASCII))
				{
					string body = sr.ReadToEnd();
					long bodyLen = body.Length;
					byte[] buffer = Encoding.ASCII.GetBytes(body);

					// virtualMIDI can send any number of MIDI commands at a time.
					// However, VRChat appears to break when body size is > 254, so I assume that 
					// means VRC can only receive 128 midi commands (127 data plus one for len) in one frame
					// or in an indeterminant short time span, maybe one frame.
					// Rough benchmarks show it takes 0.007 to 0.008 seconds to receive 254 bytes, so ~30K bytes per second
					// throughput should be possible, if VRC wasn't having issues.

					// Currently (3/12/2021), sending >128 MIDI commands in direct succession causes an IndexOutOfRangeException
					// in VRC.SDK3.Midi.VRCMidiHandler.Update, so a timeout between sending MIDI chunks is currently implemented

					// virtualMIDI allows sending chunks of midi commands in a single buffer, which
					// is faster than sending individual 3-byte commands.
					int bufferLen = 3 + ((int)bodyLen / 2) * 3;
					if (bodyLen % 2 == 1)
						bufferLen += 3; // Add an extra midi command if an odd amoung of bytes need to be sent
					midiBuffer = new byte[bufferLen];
					midiBufferOffset = 0;

					// Data length is sent as a single MIDI command before the actual data
					AddMIDILength((int)bodyLen);
					// Two bytes of data are packed into each MIDI command, since each command
					// effectively only has 19 usable bits
					for (int i = 0; i < buffer.Length - 1; i+=2)
						AddMIDIBytes(MIDICommandType.NoteOn, buffer[i], buffer[i+1]);
					// Send final byte if the stream length is odd
					if (buffer.Length % 2 == 1)
						AddMIDIBytes(MIDICommandType.NoteOn, buffer[buffer.Length - 1], 0);

					// Send midiBuffer in 384 byte chunks, corresponding to 128 midi commands
					int chunks = midiBuffer.Length / MAX_BYTES_PER_CHUNK;
					for (int i=0; i< chunks; i++)
                    {
						byte[] chunkBytes = new byte[MAX_BYTES_PER_CHUNK];
						Array.Copy(midiBuffer, i * MAX_BYTES_PER_CHUNK, chunkBytes, 0, MAX_BYTES_PER_CHUNK);
						port.sendCommand(chunkBytes);

						// Sleep test because VRC's MIDI implementation appears to only be able to receive a cretain amount of data PER FRAME
						Thread.Sleep(TIMEOUT_BETWEEN_MIDI_CHUNKS_MILLIS);
					}
					int remainder = midiBuffer.Length % MAX_BYTES_PER_CHUNK;
					if (remainder != 0)
                    {
						byte[] chunkBytes = new byte[remainder];
						Array.Copy(midiBuffer, midiBuffer.Length - remainder, chunkBytes, 0, remainder);
						port.sendCommand(chunkBytes);
					}

				}
			}
			catch (Exception e)
			{
				Console.WriteLine("Exception making web request: " + e.Message);
			}
		}

		static void AddMIDILength(int len)
		{
			// The NoteOff command is reserved for signaling a new paylod
			// and the length of the data - an unsigned short packed into MIDI data
			int a = (len & 0xFF00) >> 8;
			int b = len & 0xFF;
			AddMIDIBytes(MIDICommandType.NoteOff, a, b);
		}
		static void AddMIDIBytes(MIDICommandType type, int a, int b)
        {
			// Since MIDI commands' note and velocity bytes only have 7 usable bits,
			// the highest bits of each two bytes are packed into the lowest two bits
			// of the MIDI channel
			int channelA = (a & 0x80) >> 7;
			int channelB = (b & 0x80) >> 6;
			int channel = channelA | channelB;
			AddMIDICommand(type, channel, a & 0x7F, b & 0x7F);
		}
		static void AddMIDICommand(MIDICommandType type, int channel, int note, int velocity)
        {
			// Each of the three MIDI commands supported by VRChat is composed of three bytes
			// The first four bits of the first byte determine the command type (NoteOn/NoteOff/ControlChange)
			// The last four bits of the first byte determine the channel (instrument) the command is targeting
			// The second byte's 7 primary bits determine the note to be played
			// The last byte's 7 primary bits detemrine the velocity of the command

			switch (type)
			{
				case MIDICommandType.NoteOff:
					midiBuffer[midiBufferOffset] = 0x80;
					break;
				case MIDICommandType.NoteOn:
					midiBuffer[midiBufferOffset] = 0x90;
					break;
				case MIDICommandType.ControlChange:
					midiBuffer[midiBufferOffset] = 0xB0;
					break;
			}
			if (channel < 0 || channel > 15)
			{
				Console.WriteLine("Malformed MIDI command: invalid channel: " + channel);
				return;
			}
			else midiBuffer[midiBufferOffset] |= (byte)channel;
			if (note < 0 || note > 127)
			{
				Console.WriteLine("Malformed MIDI command: invalid note: " + note);
				return;
			}
			else midiBuffer[midiBufferOffset+1] = (byte)note;
			if (velocity < 0 || velocity > 127)
			{
				Console.WriteLine("Malformed MIDI command: invalid velocity: " + velocity);
				return;
			}
			else midiBuffer[midiBufferOffset+2] = (byte)velocity;

			midiBufferOffset += 3;
		}
	}
}