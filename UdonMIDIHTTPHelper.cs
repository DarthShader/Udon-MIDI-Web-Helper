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

		static void Main()
		{
			// Print MIDI driver header
			Console.WriteLine("TeVirtualMIDI started");
			Console.WriteLine("using dll-version:    " + TeVirtualMIDI.versionString);
			Console.WriteLine("using driver-version: " + TeVirtualMIDI.driverVersionString);

			// Instantiate MIDI port
			port = new TeVirtualMIDI("Udon-MIDI-HTTP-Helper", 65535, TeVirtualMIDI.TE_VM_FLAGS_PARSE_TX | TeVirtualMIDI.TE_VM_FLAGS_INSTANTIATE_TX_ONLY);

			// Open latest output log
			string logFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low\\VRChat\\VRChat";
			var directory = new DirectoryInfo(logFolder);
			var logFile = directory.GetFiles().OrderByDescending(x => x.LastWriteTime).First();
			currentLog = File.Open(logFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			previousFileLength = currentLog.Length;
			Console.WriteLine("Output log switched: " + logFile.FullName);

			// Watch for newer output log file in case game was restarted
			var watcher = new FileSystemWatcher(logFolder);
			watcher.NotifyFilter = NotifyFilters.FileName;
			watcher.Created += OnLogCreated;
			watcher.Filter = "output_log_*.txt";
			watcher.EnableRaisingEvents = true;

			// Start thread to read output log for web requests
			Console.WriteLine("Udon-MIDI-HTTP-Helper Ready.  Press any key to close the program.");
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
									// 
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
				using (StreamReader reader = new StreamReader(stream))
				{
					string[] bodyLines = reader.ReadToEnd().Split('\n');
					foreach (string s in bodyLines)
						if (s != "")
							SendMIDICommand(s);
				}
			}
			catch (Exception e)
			{
				Console.WriteLine("Exception making web request: " + e.Message);
			}
		}

		static void SendMIDICommand(string command)
		{
			// Each of the three MIDI commands supported by VRChat is composed of three bytes
			// The first four bits of the first byte determine the command type (NoteOn/NoteOff/ControlChange)
			// The last four bits of the first byte determine the channel (instrument) the command is targeting
			// The second byte's 7 primary bits determine the note to be played
			// The last byte's 7 primary bits detemrine the velocity of the command

			// This program expects MIDI commands received from web requests to be formatted in a specific way: 
			// four integers with spaces in between corresponding to the command/channel/note/velocity, with
			// new lines separating sequential commands.
			// ex. 
			// 0 0 80 127
			// 0 0 74 50
			// 1 0 80 120
			// 0 5 10 2

			byte[] midiCommand = new byte[3];

			string[] commandArgs = command.Split(' ');
			if (commandArgs.Length != 4)
			{
				Console.WriteLine("Malformed MIDI command: not enough arguments");
				return;
			}

			try
			{
				int commandType = Int32.Parse(commandArgs[0]);
				switch (commandType)
				{
					case 0:
						midiCommand[0] = 0x80; // NoteOff header
						break;
					case 1:
						midiCommand[0] = 0x90; // NoteOn header
						break;
					case 2:
						midiCommand[0] = 0xB0; // ControlChange header
						break;
					default:
						Console.WriteLine("Malformed MIDI command: unknown command type: " + commandType);
						return;
				}

				byte channel = Byte.Parse(commandArgs[1]); // (4 bits, 0-15)
				byte note = Byte.Parse(commandArgs[2]); // (7 bits, 0-127)
				byte velocity = Byte.Parse(commandArgs[3]); // (7 bits, 0-127)
				if (channel < 0 || channel > 15)
				{
					Console.WriteLine("Malformed MIDI command: invalid channel: " + channel);
					return;
				}
				else midiCommand[0] |= channel;
				if (note < 0 || note > 127)
				{
					Console.WriteLine("Malformed MIDI command: invalid note: " + note);
					return;
				}
				else midiCommand[1] = note;
				if (velocity < 0 || velocity > 127)
				{
					Console.WriteLine("Malformed MIDI command: invalid velocity: " + velocity);
					return;
				}
				else midiCommand[2] = velocity;
			}
			catch (Exception e)
			{
				Console.WriteLine("Exception parsing MIDI command: " + e.Message);
			}

			port.sendCommand(midiCommand);
		}
	}
}