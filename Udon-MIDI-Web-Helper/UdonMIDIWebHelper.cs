using System;
using System.Threading;
using System.IO;
using TobiasErichsen.teVirtualMIDI;

namespace Udon_MIDI_Web_Helper
{
    class UdonMIDIWebHelper
    {
        static void Main(string[] args)
        {
            const string STORAGE_FOLDER = "Udon-MIDI-Web-Helper_data";
            const string LOG_FILE_PREFIX = "Udon-MIDI-Web-Helper_";
            const string LOG_FILE_SUFFIX = ".log";
            const int MAX_SAVED_LOG_FILES = 5;

            if (!Directory.Exists(STORAGE_FOLDER))
            {
                Console.WriteLine("Warning: Storage folder not found.  Creating new folder " + STORAGE_FOLDER);
                Directory.CreateDirectory(STORAGE_FOLDER);
            }

            // Delete old logs if there are more than MAX_SAVED_LOG_FILES logs
            string[] logFilenames = Directory.GetFiles(STORAGE_FOLDER, "*.log");
            if (logFilenames.Length >= MAX_SAVED_LOG_FILES)
            {
                DateTime[] creationDates = new DateTime[logFilenames.Length];
                for (int i = 0; i < logFilenames.Length; i++)
                    creationDates[i] = File.GetCreationTime(logFilenames[i]);
                Array.Sort(creationDates, logFilenames);
                for (int i = 0; i <= logFilenames.Length - MAX_SAVED_LOG_FILES; i++)
                    File.Delete(logFilenames[i]);
            }

            DateTime now = DateTime.Now;
            string fileDate = now.Day + "-" + now.Month + "-" + now.Year + "_" + now.Hour + "-" + now.Minute + "-" + now.Second;
            string logFilename = LOG_FILE_PREFIX + fileDate + LOG_FILE_SUFFIX;
            ConsoleCopy cc = new ConsoleCopy(STORAGE_FOLDER + "\\" + logFilename);

            Console.WriteLine("TeVirtualMIDI started");
            Console.WriteLine("using dll-version:    " + TeVirtualMIDI.versionString);
            Console.WriteLine("using driver-version: " + TeVirtualMIDI.driverVersionString);
            Console.WriteLine("Udon-MIDI-Web-Helper v15 Ready.  Press Escape to end the program.");

            Thread logParserThread = new Thread(new ThreadStart(new LogParser().Run));
            logParserThread.Start();

            while (Console.ReadKey().Key != ConsoleKey.Escape)
                ;
            logParserThread.Abort();
        }
    }
}
