using System;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Collections.Generic;

namespace Udon_MIDI_Web_Helper
{
    class LogParser
    {
        const int MAX_BYTES_PER_LINE = 100000;
        byte[] lineSeparator = Encoding.UTF8.GetBytes("\n\n\r\n");
        const int MAX_HOSTS_PER_WORLD = 10;
        const string STORAGE_FOLDER = "Udon-MIDI-Web-Helper_data";
        const string KEYS_FILENAME = "Udon-MIDI-Web-Helper.keys";

        MIDIManager midiManager;
        WebManager webManager;
        FileStream currentLog;
        long previousFileLength = 0;

        string[] microphoneFingerprint;
        int micsToRead = 0;
        bool readingMicFingerprint = false;
        string worldID;
        string lastReadWorldID;
        int hostsThisWorld = 0;
        string userID;
        Dictionary<string, string> passwords; // hostname | password
        struct LocalStorageWorldValue
        {
            public LocalStorageWorldValue(string v, bool p)
            {
                value = v;
                valueIsPublic = p;
            }
            public string value;
            public bool valueIsPublic;
        }
        Dictionary<string, Dictionary<string, LocalStorageWorldValue>> localStorage; // worldID | (key | value)

        public LogParser()
        {
            midiManager = new MIDIManager();
            webManager = new WebManager(midiManager);
            localStorage = new Dictionary<string, Dictionary<string, LocalStorageWorldValue>>();
        }

        void OnLogCreated(object sender, FileSystemEventArgs e)
        {
            Console.WriteLine("Output log switched: " + e.FullPath);
            FileStream previousLog = currentLog;
            currentLog = File.Open(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Reset();

            // Should probably mutex this, but it should be safe to assume the old log isn't being used if a new one is opening
            if (previousLog != null) previousLog.Close();
        }

        void Reset()
        {
            previousFileLength = 0;
            microphoneFingerprint = null;
            micsToRead = 0;
            readingMicFingerprint = false;
            worldID = null;
            hostsThisWorld = 0;
            lastReadWorldID = null;
            userID = null;
        }

        public void Run()
        {
            try
            {
                // Watch for newly opened output logs
                string logFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low\\VRChat\\VRChat";
                var watcher = new FileSystemWatcher(logFolder);
                watcher.NotifyFilter = NotifyFilters.FileName;
                watcher.Created += OnLogCreated;
                watcher.Filter = "output_log_*.txt";
                watcher.EnableRaisingEvents = true;

                if (!Directory.Exists(STORAGE_FOLDER))
                {
                    Console.WriteLine("Warning: Storage folder not found.  Creating new folder " + STORAGE_FOLDER);
                    Directory.CreateDirectory(STORAGE_FOLDER);
                }

                // Load or create passwords file
                if (File.Exists(STORAGE_FOLDER + "\\" + KEYS_FILENAME))
                {
                    var fs = new FileStream(KEYS_FILENAME, FileMode.Open);
                    var sr = new StreamReader(fs);
                    string s;
                    while ((s = sr.ReadLine()) != null)
                    {
                        string[] args = s.Split(' ');
                        passwords.Add(args[0], args[1]);
                    }
                    sr.Close();
                    fs.Close();
                }

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

        bool LoadLocalStorage(string key)
        {
            if (localStorage.ContainsKey(key)) // local data has already been loaded
                return true;
            else localStorage.Add(key, new Dictionary<string, LocalStorageWorldValue>());

            string filename = STORAGE_FOLDER + "\\" + key + ".savedata";
            if (!File.Exists(filename))
                return false;

            var fs = new FileStream(filename, FileMode.Open);
            var sr = new StreamReader(fs);
            string s;
            while ((s = sr.ReadLine()) != null)
            {
                string[] args = s.Split(' ');
                string k = Encoding.Unicode.GetString(Convert.FromBase64String(args[0]));
                string v = Encoding.Unicode.GetString(Convert.FromBase64String(args[1]));
                localStorage[key].Add(k, new LocalStorageWorldValue(v, args[2] == "public"));
            }
            sr.Close();
            fs.Close();

            return true;
        }

        void SaveLocalStorage(string key)
        {
            string filename = STORAGE_FOLDER + "\\" + key + ".savedata";
            var fs = new FileStream(filename, FileMode.Create);
            var sw = new StreamWriter(fs);
            foreach (var item in localStorage[key])
            {
                string k = Convert.ToBase64String(Encoding.Unicode.GetBytes(item.Key));
                string v = Convert.ToBase64String(Encoding.Unicode.GetBytes(item.Value.value));
                string valueIsPublic = item.Value.valueIsPublic ? " public" : " private";
                sw.WriteLine(k + " " + v + valueIsPublic);
            }
            sw.Close();
            fs.Close();
        }

        void ProcessLogLine(string line)
        {
            // This line appears long before you enter the first world and only when you have --log-debug-levels=API in your launch options
            // It's possible this line only appears when API request retrys are necessary, so it may not always exist
            // 2021.09.25 17:36:28 Log        -  [API] Requesting Get message/usr_9a88296f-e5ce-4274-9e43-688338fe9d31/message {{}} disableCache: True retryCount: 2
            if (microphoneFingerprint == null && userID == null && line.Length > 40 && line.Substring(34, 5) == "[API]")
            {
                string[] args = line.Substring(40).Split('/');
                if (args[0] == "Requesting Get message")
                {
                    Console.WriteLine("User ID found: " + args[1]);
                    userID = args[1];
                }
            }

            // This line appears shortly before microphone lines, and serves as a verifiable world/instance id log line
            // 2021.09.25 01:36:42 Log        -  [Behaviour] Joining wrld_9f212814-2234-4d53-905b-736a84895bc5:15028~private(usr_9a88296f-e5ce-4274-9e43-688338fe9d31)~nonce(781FAF7D26A06AA4F752312F133CABC5AE7A77DAF13C0905)
            if (line.Length > 60 && (line.Substring(34, 25) == "[Behaviour] Joining wrld_" || line.Substring(34, 24) == "[Behaviour] Joining wld_"))
                lastReadWorldID = line.Substring(54).Split(':')[0];
            

            // Securely verify the current world ID by using the logged microphones as a fingerprint
            // If the microphone lines match, the last read 
            /*
             *  2021.09.25 01:36:30 Log        -  [Behaviour] uSpeak [1][2123635102]: Microphones installed (3 total)
                2021.09.25 01:36:30 Log        -  [Behaviour] uSpeak [1][2123635103]: -- [0] device name = 'Microphone (3- Logitech USB Microphone)' min/max freq = 48000 / 48000
                2021.09.25 01:36:30 Log        -  [Behaviour] uSpeak [1][2123635104]: -- [1] device name = 'Microphone (Rift Audio)' min/max freq = 48000 / 48000
                2021.09.25 01:36:30 Log        -  [Behaviour] uSpeak [1][2123635105]: -- [2] device name = 'Headset Microphone (Oculus Virtual Audio Device)' min/max freq = 48000 / 48000
             */
            if (line.Length > 58 && line.Substring(34, 20) == "[Behaviour] uSpeak [")
            {
                string[] args = line.Substring(57).Split(':');
                if (args.Length >= 2)
                {
                    string micInfo = args[1];
                    args = micInfo.Split('(');
                    if (args.Length >= 2)
                    {
                        if (args[0] == " Microphones installed ")
                        {
                            if (Int32.TryParse(args[1].Split(' ')[0], out micsToRead) && microphoneFingerprint == null)
                            {
                                // If microphone information is found before a user ID, the user probably
                                // doesn't have API level logging enabled in VRChat.  POST requsts using
                                // the reserved variable names will be aborted.
                                if (userID == null)
                                    Console.WriteLine("Warning: UserID could not be found.  Secure connections to servers won't be possible.  You probably didn't run VRChat with --log-debug-levels=API");
                                else
                                {
                                    readingMicFingerprint = true;
                                    microphoneFingerprint = new string[micsToRead];
                                }
                            }
                        }
                        else if (micsToRead > 0)
                        {
                            if (readingMicFingerprint)
                            {
                                microphoneFingerprint[microphoneFingerprint.Length - micsToRead] = micInfo;
                                micsToRead--;
                                if (micsToRead == 0)
                                {
                                    readingMicFingerprint = false;
                                    worldID = lastReadWorldID;
                                    Console.WriteLine("Verified world change to: " + worldID);
                                    hostsThisWorld = 0;
                                }
                            }
                            else if (microphoneFingerprint[microphoneFingerprint.Length - micsToRead] == micInfo)
                            {
                                micsToRead--;
                                if (micsToRead == 0)
                                {
                                    worldID = lastReadWorldID;
                                    Console.WriteLine("Verified world change to: " + worldID);
                                    hostsThisWorld = 0;
                                }
                            }
                            else micsToRead = 0; // Failed to read the same mic info, fail to change world ID
                        }
                    }
                }
            }


            // Log lines are expected to be in this format, separated by spaces, with arbitrary data arguments in base64
            // 2021.03.16 20:00:01 Log        -  [Udon-MIDI-Web-Helper] RST
            // 2021.03.16 20:00:01 Log        -  [Udon-MIDI-Web-Helper] RDY
            // 2021.03.16 20:00:01 Log        -  [Udon-MIDI-Web-Helper] ACK
            // 2021.01.01 00:00:00 Log        -  [Udon-MIDI-Web-Helper] GET 0 https://www.vrchat.com UTF16
            // 2021.01.01 00:00:00 Log        -  [Udon-MIDI-Web-Helper] PST 0 https://www.vrchat.com UTF16 key1 value1 key2 value2
            // 2021.03.16 20:00:01 Log        -  [Udon-MIDI-Web-Helper] WSO 1 wss://echo.websocket.org UTF16
            // 2021.03.16 20:00:01 Log        -  [Udon-MIDI-Web-Helper] WSM 1 txt MessageText true UTF16
            // 2021.03.16 20:00:01 Log        -  [Udon-MIDI-Web-Helper] WSM 1 bin binaryblob true
            // 2021.03.16 20:00:01 Log        -  [Udon-MIDI-Web-Helper] WSC 1
            // 2021.01.01 00:00:00 Log        -  [Udon-MIDI-Web-Helper] STORE key value public global
            // 2021.01.01 00:00:00 Log        -  [Udon-MIDI-Web-Helper] RETRIEVE 3 key wrld_9f212814-2234-4d53-905b-736a84895bc5
            // 2021.01.01 00:00:00 Log        -  [Udon-MIDI-Web-Helper] 
            // 2021.01.01 00:00:00 Log        -  [Udon-MIDI-Web-Helper] 
            // 2021.01.01 00:00:00 Log        -  [Udon-MIDI-Web-Helper] 
            // 2021.01.01 00:00:00 Log        -  [Udon-MIDI-Web-Helper] 
            // 2021.01.01 00:00:00 Log        -  [Udon-MIDI-Web-Helper] 
            if (line.Length > 58 && line.Substring(34, 22) == "[Udon-MIDI-Web-Helper]")
            {
                string[] args = line.Substring(57).Split(' ');
                switch (args[0])
                {
                    case "RST":
                        Console.WriteLine("Connections reset.");
                        webManager.Reset();
                        midiManager.Reset();
                        break;
                    case "GET": // new http get request with conntionID, uri, and optional "auto-convert response from UTF8 to UTF16" arguments
                        {
                            try
                            {
                                int connectionID = Int32.Parse(args[1]);
                                // Un-base64 the uri into a byte array, then convert it from Unicode to a string
                                string uriDecoded = Encoding.Unicode.GetString(Convert.FromBase64String(args[2]));
                                Uri webUri;
                                try
                                {
                                    webUri = new Uri(uriDecoded);
                                }
                                catch (UriFormatException e)
                                {
                                    Console.WriteLine("URI incorrectly formatted: " + e.Message);
                                    midiManager.SendWebRequestFailedResponse(connectionID);
                                    break;
                                }
                                bool autoConvertResponse = false;
                                if (args.Length > 3)
                                    autoConvertResponse = args[3] == "UTF16";

                                Console.WriteLine("Performing web request (" + connectionID + "): " + uriDecoded);
                                webManager.GetWebRequest(connectionID, webUri, autoConvertResponse);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Error parsing web request: " + e.Message);
                            }
                            break;
                        }
                    case "PST": // new http post request with conntionID, uri, "auto-convert response from UTF8 to UTF16", and any number of key/value pair arguments
                        {
                            try
                            {
                                if (args.Length % 2 == 1 || args.Length < 4)
                                {
                                    Console.WriteLine("Error: Incorrect number of arguments in web post request.");
                                    break;
                                }

                                int connectionID = Int32.Parse(args[1]);
                                // Un-base64 the uri into a byte array, then convert it from Unicode to a string
                                string uriDecoded = Encoding.Unicode.GetString(Convert.FromBase64String(args[2]));
                                Uri webUri;
                                try
                                {
                                    webUri = new Uri(uriDecoded);
                                }
                                catch (UriFormatException e)
                                {
                                    Console.WriteLine("URI incorrectly formatted: " + e.Message);
                                    midiManager.SendWebRequestFailedResponse(connectionID);
                                    break;
                                }

                                bool autoConvertResponse = false;
                                autoConvertResponse = args[3] == "UTF16";

                                int argCount = (args.Length - 3) / 2;
                                var dict = new Dictionary<string, string>(argCount);
                                for (int i=4; i<args.Length; i+=2)
                                {
                                    string key, value;
                                    if (args[i] == "=") key = ""; // Placeholder non-standard base64 character for empty strings
                                    else key = Encoding.Unicode.GetString(Convert.FromBase64String(args[i]));
                                    if (args[i + 1] == "=") value = "";
                                    else value = Encoding.Unicode.GetString(Convert.FromBase64String(args[i+1]));

                                    // Sanitize post variables reserved for securely verified information
                                    if (key == "q0vB-6zRlh0") // worldID
                                        value = worldID;
                                    else if (key == "5GZGUM6j9tQ") // userID
                                    {
                                        if (userID != null)
                                            value = userID;
                                        else
                                        {
                                            Console.WriteLine("Error: Web request needs verified userID.  Run VRChat with --log-debug-levels=API");
                                            goto breakOutOfPost;
                                        }
                                    }
                                    else if (key == "qtkmKZtltyI") // per-hostname generated keys
                                    {
                                        if (userID == null)
                                        {
                                            Console.WriteLine("Error: Web request needs verified userID.  Run VRChat with --log-debug-levels=API");
                                            goto breakOutOfPost;
                                        }
                                        else
                                        {
                                            string host = webUri.Host;
                                            if (passwords.ContainsKey(host))
                                                value = passwords[host];
                                            else if (hostsThisWorld < MAX_HOSTS_PER_WORLD)
                                            {
                                                // generate & add a new key to dictionary
                                                byte[] bytes = new byte[32];
                                                new System.Security.Cryptography.RNGCryptoServiceProvider().GetBytes(bytes);
                                                string hash = BitConverter.ToString(bytes).Replace("-", "").ToLower();
                                                passwords.Add(host, hash);

                                                // save keys dictionary to file
                                                var fs = new FileStream(KEYS_FILENAME, FileMode.Create);
                                                var sw = new StreamWriter(fs);
                                                foreach (var item in passwords)
                                                    sw.WriteLine(item.Key + " " + item.Value);
                                                sw.Close();
                                                fs.Close();

                                                value = passwords[host];
                                                hostsThisWorld++;
                                            }
                                            else
                                            {
                                                Console.WriteLine("Error: VRChat world attempted to register too many (" + MAX_HOSTS_PER_WORLD + ") different passwords.");
                                                goto breakOutOfPost;
                                            }
                                        }
                                    }

                                    dict.Add(key, value);
                                }

                                Console.WriteLine("Performing web post request (" + connectionID + "): " + uriDecoded);
                                webManager.PostWebRequest(connectionID, webUri, autoConvertResponse, dict);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Error parsing web request: " + e.Message);
                            }
                            breakOutOfPost:
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
                            try
                            {
                                int connectionID = Int32.Parse(args[1]);
                                string uriDecoded = Encoding.Unicode.GetString(Convert.FromBase64String(args[2]));
                                Uri webUri;
                                try
                                {
                                    webUri = new Uri(uriDecoded);
                                }
                                catch (UriFormatException e)
                                {
                                    Console.WriteLine("URI incorrectly formatted: " + e.Message);
                                    midiManager.SendWebSocketClosedResponse(connectionID);
                                    return;
                                }
                                bool autoConvertResponse = false;
                                if (args.Length > 3)
                                    autoConvertResponse = args[3] == "UTF16";
                                Console.WriteLine("Opening websocket (" + connectionID + "): " + uriDecoded);
                                webManager.OpenWebSocketConnection(connectionID, webUri, autoConvertResponse);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Error parsing web request: " + e.Message);
                            }
                            break;
                        }
                    case "WSC": // close existing websocket connection with connectionID argument
                        {
                            try
                            {
                                int connectionID = Int32.Parse(args[1]);
                                Console.WriteLine("Closing websocket connection " + connectionID);
                                webManager.CloseWebSocketConnection(connectionID);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Error parsing connection ID: " + e.Message);
                            }
                            break;
                        }
                    case "WSM": // Send websocket message with connectionID, text/bin flag, data, and optional UTF16 arguments
                        {
                            try
                            {
                                int connectionID = Int32.Parse(args[1]);
                                bool textMessage = args[2] == "txt";
                                byte[] data = Convert.FromBase64String(args[3]);
                                bool autoConvertMessage = false;
                                bool endOfMessage = args[4] == "true";
                                if (args.Length > 5)
                                    autoConvertMessage = args[5] == "UTF16";
                                Console.WriteLine("Sending websocket message: " + Encoding.Unicode.GetString(data));
                                webManager.SendWebSocketMessage(connectionID, data, textMessage, endOfMessage, autoConvertMessage);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Error parsing web request: " + e.Message);
                            }
                            break;
                        }
                    case "STORE": // Store key/value pair in local file tied to worldID (.savedata file)
                        {
                            try
                            {
                                string saveDataWorldID = worldID;
                                if (args.Length == 5 && args[4] == "global")
                                    saveDataWorldID = "global";

                                string key = Encoding.Unicode.GetString(Convert.FromBase64String(args[1]));
                                string value = Encoding.Unicode.GetString(Convert.FromBase64String(args[2]));

                                LoadLocalStorage(saveDataWorldID);

                                if (localStorage[saveDataWorldID].ContainsKey(key))
                                    localStorage[saveDataWorldID][key] = new LocalStorageWorldValue(value, args[3] == "public" || saveDataWorldID == "global");
                                else localStorage[saveDataWorldID].Add(key, new LocalStorageWorldValue(value, args[3] == "public" || saveDataWorldID == "global"));

                                SaveLocalStorage(saveDataWorldID);
                                Console.WriteLine("Stored value:" + value + " to key:" + key + " for " + saveDataWorldID + ".savedata");
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Error parsing store request: " + e.Message);
                            }
                        }
                        break;
                    case "RETRIEVE": // Retrieve value given a key and worldID  (.savedata file)
                        {
                            try
                            {
                                int connectionID = Int32.Parse(args[1]);
                                string saveDataWorldID = args[3];

                                string key = Encoding.Unicode.GetString(Convert.FromBase64String(args[2]));
                                Console.WriteLine("Retrieving " + key + " from " + saveDataWorldID);

                                if (LoadLocalStorage(saveDataWorldID)) // worldID save data found
                                {
                                    if (localStorage[saveDataWorldID].ContainsKey(key)) // key found
                                    {
                                        if (localStorage[saveDataWorldID][key].valueIsPublic)
                                            webManager.AddGenericResponse(200, Encoding.Unicode.GetBytes(localStorage[saveDataWorldID][key].value), connectionID);
                                        else webManager.AddGenericResponse(403, Encoding.Unicode.GetBytes(""), connectionID);
                                    }
                                    else webManager.AddGenericResponse(404, Encoding.Unicode.GetBytes(""), connectionID);
                                }
                                else webManager.AddGenericResponse(404, Encoding.Unicode.GetBytes(""), connectionID);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Error parsing retrieve request: " + e.Message);
                            }
                        }
                        break;
                }
            }
        }
    }
}
