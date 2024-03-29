﻿using System;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace Udon_MIDI_Web_Helper
{
    class LogParser
    {
        const int MAX_BYTES_PER_LINE = 100000;
        byte[] lineSeparator = Encoding.UTF8.GetBytes("\n\n\r\n");
        const int WEB_REQUEST_FAILED_ERROR_CODE = 111;
        const int EXTENDED_LOGGING_UNAVAILABLE_ERROR_CODE = 112;
        const int MAX_HOSTS_PER_WORLD = 100;
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
        string instanceID;
        string lastReadWorldID;
        string lastReadInstanceID;
        int hostsThisWorld = 0;
        string userID;
        string displayName;
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
        Dictionary<string, string> avatarChanges;
        string connectionKey;

        struct AvatarChange
        {
            public string steamID;
            public string avatarID;
            public string avatarName;
            public string avatarDescription;
            public string avatarAuthor;
            public string avatarAuthorUserID;
            public bool avatarCloning;
            public string modTag;
            public bool inVRMode;
            public string statusDescription;
            public string userID;
            public string displayName;
        }

        public LogParser()
        {
            midiManager = new MIDIManager();
            webManager = new WebManager(midiManager);
            localStorage = new Dictionary<string, Dictionary<string, LocalStorageWorldValue>>();
            avatarChanges = new Dictionary<string, string>();
            GenerateConnectionKey();
        }

        void GenerateConnectionKey()
        {
            byte[] bytes = new byte[32];
            new System.Security.Cryptography.RNGCryptoServiceProvider().GetBytes(bytes);
            connectionKey = BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }

        void OnLogCreated(object sender, FileSystemEventArgs e)
        {
            Console.WriteLine("Output log switched: " + e.FullPath);
            FileStream previousLog = currentLog;
            currentLog = File.Open(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            ResetLogParser();

            // Should probably mutex this, but it should be safe to assume the old log isn't being used if a new one is opening
            if (previousLog != null) previousLog.Close();
        }

        void ResetLogParser()
        {
            previousFileLength = 0;
            microphoneFingerprint = null;
            micsToRead = 0;
            readingMicFingerprint = false;
            worldID = null;
            hostsThisWorld = 0;
            lastReadWorldID = null;
            userID = null;
            displayName = null;
            passwords.Clear();
            localStorage.Clear();
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

                // Load or create passwords file
                passwords = new Dictionary<string, string>();
                if (File.Exists(STORAGE_FOLDER + "\\" + KEYS_FILENAME))
                {
                    var fs = new FileStream(STORAGE_FOLDER + "\\" + KEYS_FILENAME, FileMode.Open);
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
                int bytesOffset = 0;
                while (true)
                {
                    if (currentLog != null && currentLog.Length > previousFileLength)
                    {
                        currentLog.Seek(previousFileLength, SeekOrigin.Begin);
                        previousFileLength = currentLog.Length;

                        // Read bytes from the log.  Do not assume all lines are completely flushed at once
                        while (currentLog.Position < previousFileLength)
                        {
                            bytes[bytesOffset] = (byte)currentLog.ReadByte();
                            if (bytes[bytesOffset] == lineSeparator[lineSeparator.Length - 1])
                            {
                                int tempOffset = bytesOffset;
                                bool separatorMatch = true;
                                for (int i = lineSeparator.Length - 1; i >= 0; i--)
                                    if (tempOffset < 0 || bytes[tempOffset--] != lineSeparator[i])
                                    {
                                        separatorMatch = false;
                                        break;
                                    }

                                if (separatorMatch)
                                {
                                    ProcessLogLine(Encoding.UTF8.GetString(bytes, 0, bytesOffset - lineSeparator.Length + 1));
                                    //Array.Clear(bytes, 0, bytes.Length);
                                    bytesOffset = 0;
                                    continue;
                                }
                            }
                            bytesOffset++;
                        }

                        // old
                        /*
                        while (currentLog.Position < previousFileLength)
                        {
                            // Read log byte-by-byte to more easily match the log's unique 4 byte line separator sequence.
                            // Individual log lines need to be isolated so individual Debug.Log calls from Udon can be securely and reliably found
                            // to be sure the [Udon-MIDI-Web-Helper] tag wasn't spoofed by something else.
                            bytes[bytesOffset] = (byte)currentLog.ReadByte();
                            if (bytes[bytesOffset] == lineSeparator[lineSeparator.Length - 1])
                            {
                                // Go through line separator from end to start, checking the previous bytes
                                int tempByteArrayOffset = bytesOffset;
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
                                    if (!lineTooLong && bytesOffset > lineSeparator.Length)
                                        ProcessLogLine(Encoding.UTF8.GetString(bytes, 0, bytesOffset - lineSeparator.Length + 1));
                                    bytesOffset = 0;
                                    continue;
                                }
                            }

                            bytesOffset++;
                        }*/
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
                string k, v;
                if (args[0] == "=") // again with the zero length string b64 support bc oversimplified file format
                    k = "";
                else k = Encoding.Unicode.GetString(Convert.FromBase64String(args[0]));
                if (args[1] == "=")
                    v = "";
                else v = Encoding.Unicode.GetString(Convert.FromBase64String(args[1]));
                localStorage[key].Add(k, new LocalStorageWorldValue(v, args[2] == "public"));
            }
            sr.Close();
            fs.Close();

            return true;
        }

        void SaveLocalStorage(string key)
        {
            // Export a simple, obscured key/value pair file format
            // Prevents basic editing, but is easy to read/write (though it could be faster without b64)

            string filename = STORAGE_FOLDER + "\\" + key + ".savedata";
            var fs = new FileStream(filename, FileMode.Create);
            var sw = new StreamWriter(fs);
            foreach (var item in localStorage[key])
            {
                string k, v;
                if (item.Key == "")
                    k = "=";
                else k = Convert.ToBase64String(Encoding.Unicode.GetBytes(item.Key));
                if (item.Value.value == "")
                    v = "=";
                else v = Convert.ToBase64String(Encoding.Unicode.GetBytes(item.Value.value));
                string valueIsPublic = item.Value.valueIsPublic ? " public" : " private";
                sw.WriteLine(k + " " + v + valueIsPublic);
            }
            sw.Close();
            fs.Close();
        }

        void CheckForGameStateData(string line)
        {
            // Display name is logged first, in a series for 5 lines separated by \r\n
            // 2021.09.30 17:17:42 Log -  [Behaviour] User Authenticated: Kaj
            // - hasEmail: True
            // - hasBirthday: True
            // - tos: 7
            // - avatar: avtr_5091cf4a-1579-4312-aa61-20f82b974e3f
            if (userID == null && displayName == null && line.Length > 66 && line.Substring(34, 32) == "[Behaviour] User Authenticated: ")
            {
                string[] args = line.Substring(66).Split('\r');
                Console.WriteLine("Displayname found: " + args[0]);
                displayName = args[0];
            }

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
            /*
             *  2021.10.23 02:07:12 Log        -  [Behaviour] Joining wrld_ca5610af-6855-4568-864a-836f721d989b:77249
                2021.10.23 02:07:12 Log        -  [Behaviour] Joining or Creating Room: A Rainy Day
             * */
            if (line.Length > 60 && line.Substring(34, 20) == "[Behaviour] Joining ")
            {
                string[] splitByColon = line.Substring(54).Split(':');
                if (splitByColon[0] == "or Creating Room")
                    return;
                lastReadWorldID = splitByColon[0];
                // Emergency sanitization in case a malicious world spoofs the correct microphone list and
                // provides a relative filepath where the worldID log line should be.
                lastReadWorldID = lastReadWorldID.Replace("\\", "").Replace(".", "");
                lastReadInstanceID = splitByColon[1].Split('~')[0];
            }


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
                                    Console.WriteLine("Warning: UserID could not be found.  Secure connections to servers won't be possible.  You probably didn't launch VRChat with --log-debug-levels=API");

                                readingMicFingerprint = true;
                                microphoneFingerprint = new string[micsToRead];
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
                                    instanceID = lastReadInstanceID;
                                    Console.WriteLine("Verified world change to: " + worldID + ":" + instanceID);
                                    hostsThisWorld = 0;
                                    avatarChanges.Clear();
                                }
                            }
                            else if (microphoneFingerprint[microphoneFingerprint.Length - micsToRead] == micInfo)
                            {
                                micsToRead--;
                                if (micsToRead == 0)
                                {
                                    worldID = lastReadWorldID;
                                    instanceID = lastReadInstanceID;
                                    Console.WriteLine("Verified world change to: " + worldID + ":" + instanceID);
                                    hostsThisWorld = 0;
                                    avatarChanges.Clear();
                                }
                            }
                            else
                            {
                                // Failed to read the same mic info
                                // This is either because a world is trying to spoof a worldID change using a known
                                // microphone list OR because the microphone list changed.  It is safest to stop
                                // the program entirely to let people know something is wrong.
                                Console.WriteLine("Error: Microphones list was different than first read microphone list.  Either your microphones changed or the current world is trying to do something malicious.  Aborting current session.");
                                currentLog.Close();
                                currentLog = null;
                                ResetLogParser();
                            }
                        }
                    }
                }
            }
        }

        void ProcessLogLine(string line)
        {
            CheckForGameStateData(line);
            CheckForAvatarChanges(line);

            // Log lines are expected to be in this format, separated by spaces, with arbitrary data arguments in base64
            // 2021.03.16 20:00:01 Log        -  [Udon-MIDI-Web-Helper] RESET
            // 2021.03.16 20:00:01 Log        -  [Udon-MIDI-Web-Helper] READY
            // 2021.03.16 20:00:01 Log        -  [Udon-MIDI-Web-Helper] ACK
            // 2021.01.01 00:00:00 Log        -  [Udon-MIDI-Web-Helper] PING
            // 2021.01.01 00:00:00 Log        -  [Udon-MIDI-Web-Helper] connectionKey GET 0 https://www.vrchat.com UTF16
            // 2021.01.01 00:00:00 Log        -  [Udon-MIDI-Web-Helper] connectionKey POST 0 https://www.vrchat.com UTF16 key1 value1 key2 value2
            // 2021.03.16 20:00:01 Log        -  [Udon-MIDI-Web-Helper] connectionKey WSOPEN 1 wss://echo.websocket.org UTF16
            // 2021.03.16 20:00:01 Log        -  [Udon-MIDI-Web-Helper] connectionKey WSMESSAGE 1 txt MessageText true UTF16
            // 2021.03.16 20:00:01 Log        -  [Udon-MIDI-Web-Helper] connectionKey WSMESSAGE 1 bin binaryblob true
            // 2021.03.16 20:00:01 Log        -  [Udon-MIDI-Web-Helper] connectionKey WSCLOSE 1
            // 2021.03.16 20:00:01 Log        -  [Udon-MIDI-Web-Helper] connectionKey CLEAR 1
            // 2021.01.01 00:00:00 Log        -  [Udon-MIDI-Web-Helper] connectionKey STORE key value public global
            // 2021.01.01 00:00:00 Log        -  [Udon-MIDI-Web-Helper] connectionKey RETRIEVE 3 key wrld_9f212814-2234-4d53-905b-736a84895bc5
            // 2021.01.01 00:00:00 Log        -  [Udon-MIDI-Web-Helper] connectionKey OPENBROWSER https://www.github.com
            if (line.Length > 58 && line.Substring(34, 22) == "[Udon-MIDI-Web-Helper]")
            {
                string[] args = line.Substring(57).Split(' ');
                if (args[0] == connectionKey)
                {
                    switch (args[1])
                    {
                        case "GET":
                            Get(args);
                            break;
                        case "POST":
                            Post(args);
                            break;
                        case "WSOPEN":
                            WebSocketOpen(args);
                            break;
                        case "WSCLOSE":
                            WebSocketClose(args);
                            break;
                        case "CLEAR":
                            ClearConnection(args);
                            break;
                        case "WSMESSAGE":
                            WebSocketMessage(args);
                            break;
                        case "STORE":
                            Store(args);
                            break;
                        case "RETRIEVE":
                            Retrieve(args);
                            break;
                        case "OPENBROWSER":
                            Open(args);
                            break;
                    }
                }
                else
                {
                    switch (args[0])
                    {
                        case "PING":
                            Ping();
                            break;
                        case "READY":
                            Ready();
                            break;
                        case "ACK":
                            Acknowledge();
                            break;
                        case "RESET":
                            Reset();
                            break;
                    }
                }
            }
        }

        void CheckForAvatarChanges(string line)
        {
            // Process PLAYER 253/255 and SYSTEM 253/255 json messages if --log-debug-levels=NetworkTransport is enabled
            AvatarChange avc;
            avc.avatarID = null;
            avc.displayName = null;
            
            if (line.Length > 68 && line.Substring(34, 34) == "[Network Data] OnEvent: SYSTEM 253")
                avc = ParseAvatarChange(line, 68, "251");
            else if (line.Length > 68 && line.Substring(34, 34) == "[Network Data] OnEvent: SYSTEM 255")
                avc = ParseAvatarChange(line, 68, "249");
            else if (line.Length > 70 && line.Substring(34, 36) == "[Network Data] OnEvent: PLAYER:  253")
                avc = ParseAvatarChange(line, 70, "251");
            else if (line.Length > 70 && line.Substring(34, 36) == "[Network Data] OnEvent: PLAYER:  255")
                avc = ParseAvatarChange(line, 70, "249");
            if (avc.avatarID != null)
            {
                // Don't report duplicate avatar changes.  This list is reset when world changes are verified.
                if (avatarChanges.ContainsKey(avc.displayName) && avatarChanges[avc.displayName] == avc.avatarID)
                    return;
                avatarChanges[avc.displayName] = avc.avatarID;

                string data = avc.displayName + "\n" + avc.avatarID;
                byte[] bytes = Encoding.Unicode.GetBytes(data);
                webManager.AddGenericResponse(1, bytes, 255); // Send on reserved response code & loopback connection
                Console.WriteLine("Player " + avc.displayName + " changed into " + avc.avatarID);
            }
        }

        AvatarChange ParseAvatarChange(string line, int offset, string dataCode)
        {
            string json = line.Substring(offset);
            json = json.Replace("{{", "{").Replace("}}", "}");

            var jsonDoc = JsonDocument.Parse(json);
            AvatarChange avc = new AvatarChange();

            try
            {
                JsonElement twoFiftyOne = jsonDoc.RootElement
                            .GetProperty("Parameters")
                            .GetProperty(dataCode);
                JsonElement user = twoFiftyOne.GetProperty("user");

                JsonElement jsonOut;
                if (twoFiftyOne.TryGetProperty("steamUserID", out jsonOut))
                    avc.steamID = jsonOut.GetString();
                if (twoFiftyOne.TryGetProperty("avatarVariations", out jsonOut))
                    avc.avatarID = jsonOut.GetString();
                avc.displayName = user.GetProperty("displayName").GetString();
                avc.userID = user.GetProperty("id").GetString();
                avc.avatarCloning = user.GetProperty("allowAvatarCopying").GetBoolean();
                if (twoFiftyOne.TryGetProperty("inVRMode", out jsonOut))
                    avc.inVRMode = jsonOut.GetBoolean();

                JsonElement avatarDict;
                if (twoFiftyOne.TryGetProperty("avatarDict", out avatarDict))
                {
                    if (avc.avatarID == null && avatarDict.TryGetProperty("id", out jsonOut))
                        avc.avatarID = jsonOut.GetString();
                    if (avatarDict.TryGetProperty("name", out jsonOut))
                        avc.avatarName = jsonOut.GetString();
                    if (avatarDict.TryGetProperty("description", out jsonOut))
                        avc.avatarDescription = jsonOut.GetString();
                    if (avatarDict.TryGetProperty("authorName", out jsonOut))
                        avc.avatarAuthor = jsonOut.GetString();
                    if (avatarDict.TryGetProperty("authorId", out jsonOut))
                        avc.avatarAuthorUserID = jsonOut.GetString();
                }
                /*
                public string modTag;
                public string statusDescription;
                 */
            }
            catch (Exception e)
            {
                Console.Write("Error parsing avatar change!");
            }
            
            return avc;
        }

        void Reset()
        {
            // This should only be called once on world change

            Console.WriteLine("Connections reset.");
            webManager.Reset();
            midiManager.Reset();
            GenerateConnectionKey();
            byte[] bytes = Encoding.Unicode.GetBytes(connectionKey);
            webManager.AddGenericResponse(2, bytes, 255); // Send on reserved response code & loopback connection
        }

        void Ping()
        {
            midiManager.SendPingResponse();
        }

        void Get(string[] args)
        {
            // new http get request with conntionID, uri, and optional "auto-convert response from UTF8 to UTF16" arguments
            try
            {
                int connectionID = Int32.Parse(args[2]);
                // Un-base64 the uri into a byte array, then convert it from Unicode to a string
                string uriDecoded = Encoding.Unicode.GetString(Convert.FromBase64String(args[3]));
                Uri webUri;
                try
                {
                    webUri = new Uri(uriDecoded);
                }
                catch (UriFormatException e)
                {
                    Console.WriteLine("URI incorrectly formatted: " + e.Message);
                    midiManager.SendWebRequestFailedResponse(connectionID, WEB_REQUEST_FAILED_ERROR_CODE);
                    return;
                }
                bool autoConvertResponse = false;
                if (args.Length > 4)
                    autoConvertResponse = args[4] == "UTF16";

                Console.WriteLine("Performing web request (" + connectionID + "): " + uriDecoded);
                webManager.GetWebRequest(connectionID, webUri, autoConvertResponse);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error parsing web request: " + e.Message);
            }
        }

        void Post(string[] args)
        {
            // new http post request with conntionID, uri, "auto-convert response from UTF8 to UTF16", and any number of key/value pair arguments
            try
            {
                if (args.Length % 2 == 0 || args.Length < 5)
                {
                    Console.WriteLine("Error: Incorrect number of arguments in web post request.");
                    return;
                }

                int connectionID = Int32.Parse(args[2]);
                // Un-base64 the uri into a byte array, then convert it from Unicode to a string
                string uriDecoded = Encoding.Unicode.GetString(Convert.FromBase64String(args[3]));
                Uri webUri;
                try
                {
                    webUri = new Uri(uriDecoded);
                }
                catch (UriFormatException e)
                {
                    Console.WriteLine("URI incorrectly formatted: " + e.Message);
                    midiManager.SendWebRequestFailedResponse(connectionID, WEB_REQUEST_FAILED_ERROR_CODE);
                    return;
                }

                bool autoConvertResponse = false;
                autoConvertResponse = args[4] == "UTF16";

                int argCount = (args.Length - 4) / 2;
                var dict = new Dictionary<string, string>(argCount);
                for (int i = 4; i < args.Length; i += 2)
                {
                    string key, value;
                    if (args[i] == "=") key = ""; // Placeholder non-standard base64 character for empty strings
                    else key = Encoding.Unicode.GetString(Convert.FromBase64String(args[i]));
                    if (args[i + 1] == "=") value = "";
                    else value = Encoding.Unicode.GetString(Convert.FromBase64String(args[i + 1]));

                    // Replace post variables reserved for securely verified information
                    if (key == "q0vB-6zRlh0") // worldID
                        value = worldID;
                    else if (key == "5EwkJvkFgaQ") // instanceID
                        value = instanceID;
                    else if (key == "5GZGUM6j9tQ") // userID
                    {
                        if (userID != null)
                            value = userID;
                        else
                        {
                            Console.WriteLine("Error: Web request needs verified userID.  Launch VRChat with --log-debug-levels=API");
                            midiManager.SendWebRequestFailedResponse(connectionID, EXTENDED_LOGGING_UNAVAILABLE_ERROR_CODE);
                            return;
                        }
                    }
                    else if (key == "7yzNonZ5up8") // displayname
                        value = displayName;
                    else if (key == "qtkmKZtltyI") // per-hostname generated keys
                    {
                        if (userID == null)
                        {
                            Console.WriteLine("Error: Web request needs verified userID.  Launch VRChat with --log-debug-levels=API");
                            midiManager.SendWebRequestFailedResponse(connectionID, EXTENDED_LOGGING_UNAVAILABLE_ERROR_CODE);
                            return;
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
                                var fs = new FileStream(STORAGE_FOLDER + "\\" + KEYS_FILENAME, FileMode.Create);
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
                                midiManager.SendWebRequestFailedResponse(connectionID, EXTENDED_LOGGING_UNAVAILABLE_ERROR_CODE);
                                return;
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
        }

        void Ready()
        {
            // RDY meesages are sennt from Udon to signal that the game is ready to receive a new frame.
            // A RDY can also mean the previously sent frame was not received.
            midiManager.GameIsReady = true;
        }

        void Acknowledge()
        {
            // Acknowledge that the previously sent frame was received AND that a new frame
            // is ready to be received.
            midiManager.GameIsReady = true;
            midiManager.SendFrameIfDataAvailable(true);
        }

        void WebSocketOpen(string[] args)
        {
            // new websocket connection with conntionID, uri, and optional UTF16 arguments
            try
            {
                int connectionID = Int32.Parse(args[2]);
                string uriDecoded = Encoding.Unicode.GetString(Convert.FromBase64String(args[3]));
                Uri webUri;
                try
                {
                    webUri = new Uri(uriDecoded);
                }
                catch (UriFormatException e)
                {
                    Console.WriteLine("URI incorrectly formatted: " + e.Message);
                    Console.WriteLine("Closing websocket connection " + connectionID);
                    midiManager.SendWebSocketClosedResponse(connectionID);
                    return;
                }
                bool autoConvertResponse = false;
                if (args.Length > 4)
                    autoConvertResponse = args[4] == "UTF16";
                Console.WriteLine("Opening websocket (" + connectionID + "): " + uriDecoded);
                webManager.OpenWebSocketConnection(connectionID, webUri, autoConvertResponse);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error parsing web request: " + e.Message);
            }
        }

        void WebSocketClose(string[] args)
        {
            // close existing websocket connection with connectionID argument
            try
            {
                int connectionID = Int32.Parse(args[2]);
                Console.WriteLine("Closing websocket connection " + connectionID);
                midiManager.ClearQueuedResponses(connectionID);
                midiManager.SendWebSocketClosedResponse(connectionID);
                webManager.CloseWebSocketConnection(connectionID);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error parsing connection ID: " + e.Message);
            }
        }

        void ClearConnection(string[] args)
        {
            // Clear unsent midi messages for given connection
            try
            {
                int connectionID = Int32.Parse(args[2]);
                Console.WriteLine("Clearing connection " + connectionID);
                midiManager.ClearQueuedResponses(connectionID);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error parsing connection ID: " + e.Message);
            }
        }

        void WebSocketMessage(string[] args)
        {
            // Send websocket message with connectionID, text/bin flag, data, and optional UTF16 arguments
            try
            {
                int connectionID = Int32.Parse(args[2]);
                bool textMessage = args[3] == "txt";
                byte[] data = Convert.FromBase64String(args[4]);
                bool autoConvertMessage = false;
                bool endOfMessage = args[5] == "true";
                if (args.Length > 6)
                    autoConvertMessage = args[6] == "UTF16";
                Console.WriteLine("Sending websocket message: " + Encoding.Unicode.GetString(data));
                webManager.SendWebSocketMessage(connectionID, data, textMessage, endOfMessage, autoConvertMessage);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error parsing web request: " + e.Message);
            }
        }

        void Store(string[] args)
        {
            // Store key/value pair in local file tied to worldID (.savedata file)
            try
            {
                string saveDataWorldID = worldID;
                if (args.Length == 6 && args[5] == "global")
                    saveDataWorldID = "global";

                string key = Encoding.Unicode.GetString(Convert.FromBase64String(args[2]));
                string value = Encoding.Unicode.GetString(Convert.FromBase64String(args[3]));

                LoadLocalStorage(saveDataWorldID);

                if (localStorage[saveDataWorldID].ContainsKey(key))
                    localStorage[saveDataWorldID][key] = new LocalStorageWorldValue(value, args[4] == "public" || saveDataWorldID == "global");
                else localStorage[saveDataWorldID].Add(key, new LocalStorageWorldValue(value, args[4] == "public" || saveDataWorldID == "global"));

                SaveLocalStorage(saveDataWorldID);
                Console.WriteLine("Stored value:" + value + " to key:" + key + " for " + saveDataWorldID + ".savedata");
            }
            catch (Exception e)
            {
                Console.WriteLine("Error parsing store request: " + e.Message);
            }
        }

        void Retrieve(string[] args)
        {
            // Retrieve value given a key and worldID  (.savedata file)
            try
            {
                int connectionID = Int32.Parse(args[2]);
                string saveDataWorldID = Encoding.Unicode.GetString(Convert.FromBase64String(args[4]));
                saveDataWorldID = saveDataWorldID.Replace("\\", "").Replace(".", "");

                string key = Encoding.Unicode.GetString(Convert.FromBase64String(args[3]));
                Console.WriteLine("Retrieving (" + connectionID + ") " + key + " from " + saveDataWorldID);

                if (LoadLocalStorage(saveDataWorldID)) // worldID save data found
                {
                    if (localStorage[saveDataWorldID].ContainsKey(key)) // key found
                    {
                        if (localStorage[saveDataWorldID][key].valueIsPublic || saveDataWorldID == worldID)
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

        void Open(string[] args)
        {
            Uri webUri;
            try
            {
                string uriDecoded = Encoding.Unicode.GetString(Convert.FromBase64String(args[2]));
                webUri = new Uri(uriDecoded);
            }
            catch (UriFormatException e)
            {
                Console.WriteLine("URI incorrectly formatted: " + e.Message);
                return;
            }

            webManager.OpenWebPage(webUri);
        }

    }
}
