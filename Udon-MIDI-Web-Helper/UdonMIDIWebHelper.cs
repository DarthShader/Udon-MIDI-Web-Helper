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
            const string LOG_FILENAME = "Udon-MIDI-Web-Helper.log";

            if (!Directory.Exists(STORAGE_FOLDER))
            {
                Console.WriteLine("Warning: Storage folder not found.  Creating new folder " + STORAGE_FOLDER);
                Directory.CreateDirectory(STORAGE_FOLDER);
            }
            ConsoleCopy cc = new ConsoleCopy(STORAGE_FOLDER + "\\" + LOG_FILENAME);

            Console.WriteLine("TeVirtualMIDI started");
            Console.WriteLine("using dll-version:    " + TeVirtualMIDI.versionString);
            Console.WriteLine("using driver-version: " + TeVirtualMIDI.driverVersionString);
            Console.WriteLine("Udon-MIDI-Web-Helper v13 Ready.  Press Escape to end the program.");

            Thread logParserThread = new Thread(new ThreadStart(new LogParser().Run));
            logParserThread.Start();

            while (Console.ReadKey().Key != ConsoleKey.Escape)
                ;
            logParserThread.Abort();
        }
    }
}
