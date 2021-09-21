using System;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;

namespace Udon_MIDI_Web_Helper
{
    class LogParser
    {
        const int MAX_BYTES_PER_LINE = 100000;
        byte[] lineSeparator = Encoding.UTF8.GetBytes("\n\n\r\n");

        MIDIManager midiManager;
        WebManager webManager;
        FileStream currentLog;
        long previousFileLength = 0;

        public LogParser()
        {
            midiManager = new MIDIManager();
            webManager = new WebManager(midiManager);
        }

        void OnLogCreated(object sender, FileSystemEventArgs e)
        {
            Console.WriteLine("Output log switched: " + e.FullPath);
            FileStream previousLog = currentLog;
            currentLog = File.Open(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            previousFileLength = 0;

            // Should probably mutex this, but it should be safe to assume the old log isn't being used if a new one is opening
            if (previousLog != null) previousLog.Close();
        }

        public void Run()
        {
            try
            {
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
                catch (Exception) { }

                // Watch for newer output log
                var watcher = new FileSystemWatcher(logFolder);
                watcher.NotifyFilter = NotifyFilters.FileName;
                watcher.Created += OnLogCreated;
                watcher.Filter = "output_log_*.txt";
                watcher.EnableRaisingEvents = true;

                // Watch for changes in log length
                byte[] bytes = new byte[MAX_BYTES_PER_LINE];
                while (true)
                {
                    if (currentLog != null && currentLog.Length > previousFileLength)
                    {
                        currentLog.Seek(previousFileLength, SeekOrigin.Begin);
                        previousFileLength = currentLog.Length;
                        int byteArrayOffset = 0;
                        while (currentLog.Position < previousFileLength)
                        {
                            // Read log byte-by-byte to more easily match the log's unique 4 byte line separator sequence.
                            // Individual log lines need to be isolated so individual Debug.Log calls from Udon can be securely and reliably found
                            // to be sure the [Udon-MIDI-Web-Helper] tag wasn't spoofed by something else.
                            bytes[byteArrayOffset] = (byte)currentLog.ReadByte();
                            if (bytes[byteArrayOffset] == lineSeparator[lineSeparator.Length - 1])
                            {
                                // Go through line separator from end to start, checking the previous bytes
                                int tempByteArrayOffset = byteArrayOffset;
                                bool separatorMatch = true;
                                bool lineTooLong = false;
                                for (int i = lineSeparator.Length - 1; i >= 0; i--)
                                {
                                    // Loop back to the end of bytes if a massive line is being scanned.
                                    // 'lineTooLong' is a safety net in case someone somehow aligned an
                                    // arbitrary output log message - with unescaped characters - that
                                    // is at the exact length of MAX_BYTES_PER_LINE with a spoofed [Udon-MIDI-Web-Helper] line afterwards.
                                    if (tempByteArrayOffset < 0)
                                    {
                                        lineTooLong = true;
                                        tempByteArrayOffset = bytes.Length - 1;
                                    }

                                    if (bytes[tempByteArrayOffset--] != lineSeparator[i])
                                    {
                                        separatorMatch = false;
                                        break;
                                    }
                                }

                                if (separatorMatch)
                                {
                                    // Single log line found
                                    if (!lineTooLong && byteArrayOffset > lineSeparator.Length)
                                        ProcessLogLine(Encoding.UTF8.GetString(bytes, 0, byteArrayOffset - lineSeparator.Length + 1));
                                    byteArrayOffset = 0;
                                    continue;
                                }
                            }

                            byteArrayOffset++;
                        }
                    }

                    // Only send midi frames on this thread to prevent race conditions
                    if (midiManager.GameIsReady)
                        midiManager.SendFrameIfDataAvailable(false);
                }
            }
            catch (ThreadAbortException)
            {
                if (currentLog != null) currentLog.Close();
                webManager.CTSource.Cancel();
            }
        }

        void ProcessLogLine(string line)
        {
            // Log lines are expected to be in this format, with arbitrary data arguments in base64
            // 2021.01.01 00:00:00 Log        -  [Udon-MIDI-Web-Helper] GET 0 https://www.vrchat.com UTF16
            // 2021.03.16 20:00:01 Log        -  [Udon-MIDI-Web-Helper] RDY
            // 2021.03.16 20:00:01 Log        -  [Udon-MIDI-Web-Helper] ACK
            // 2021.03.16 20:00:01 Log        -  [Udon-MIDI-Web-Helper] WSO 1 wss://echo.websocket.org UTF16
            // 2021.03.16 20:00:01 Log        -  [Udon-MIDI-Web-Helper] WSM 1 txt MessageText true UTF16
            // 2021.03.16 20:00:01 Log        -  [Udon-MIDI-Web-Helper] WSM 1 bin binaryblob true
            // 2021.03.16 20:00:01 Log        -  [Udon-MIDI-Web-Helper] WSC 1
            if (line.Length > 58 && line.Substring(34, 22) == "[Udon-MIDI-Web-Helper]")
            {
                string[] args = line.Substring(57).Split(' ');
                switch (args[0])
                {
                    case "RST":
                        Console.WriteLine(line);
                        webManager.Reset();
                        midiManager.Reset();
                        break;
                    case "GET": // new http get request with conntionID, uri, and optional "auto-convert response from UTF8 to UTF16" arguments
                        {
                            Console.WriteLine(line);
                            try
                            {
                                int connectionID = Int32.Parse(args[1]);
                                // Un-base64 the uri into a byte array, then convert it from Unicode to a string
                                string uriDecoded = Encoding.Unicode.GetString(Convert.FromBase64String(args[2]));
                                bool autoConvertResponse = false;
                                if (args.Length > 3)
                                    autoConvertResponse = args[3] == "UTF16";
                                webManager.GetWebRequest(connectionID, uriDecoded, autoConvertResponse);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Error parsing web request: " + e.Message);
                            }
                            break;
                        }
                    case "RDY":
                        // RDY meesages are sennt from Udon to signal that the game is ready to receive a new frame.
                        // A RDY can also mean the previously sent frame was not received.
                        midiManager.GameIsReady = true;
                        break;
                    case "ACK":
                        // Acknowledge that the previously sent frame was received AND that a new frame
                        // is ready to be received.
                        midiManager.GameIsReady = true;
                        midiManager.SendFrameIfDataAvailable(true);
                        break;
                    case "WSO": // new websocket connection with conntionID, uri, and optional UTF16 arguments
                        {
                            Console.WriteLine(line);
                            try
                            {
                                int connectionID = Int32.Parse(args[1]);
                                string uriDecoded = Encoding.Unicode.GetString(Convert.FromBase64String(args[2]));
                                bool autoConvertResponse = false;
                                if (args.Length > 3)
                                    autoConvertResponse = args[3] == "UTF16";
                                webManager.OpenWebSocketConnection(connectionID, uriDecoded, autoConvertResponse);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Error parsing web request: " + e.Message);
                            }
                            break;
                        }
                    case "WSC": // close existing websocket connection with connectionID argument
                        {
                            Console.WriteLine(line);
                            try
                            {
                                webManager.CloseWebSocketConnection(Int32.Parse(args[1]));
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Error parsing connection ID: " + e.Message);
                            }
                            break;
                        }
                    case "WSM": // Send websocket message with connectionID, text/bin flag, data, and optional UTF16 arguments
                        {
                            Console.WriteLine(line);
                            try
                            {
                                int connectionID = Int32.Parse(args[1]);
                                bool textMessage = args[2] == "txt";
                                byte[] data = Convert.FromBase64String(args[3]);
                                bool autoConvertMessage = false;
                                bool endOfMessage = args[4] == "true";
                                if (args.Length > 5)
                                    autoConvertMessage = args[5] == "UTF16";
                                webManager.SendWebSocketMessage(connectionID, data, textMessage, endOfMessage, autoConvertMessage);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Error parsing web request: " + e.Message);
                            }
                            break;
                        }
                }
            }
        }
    }
}
