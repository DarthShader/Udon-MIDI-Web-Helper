using System;
using System.Threading;
using TobiasErichsen.teVirtualMIDI;

namespace Udon_MIDI_Web_Helper
{
    class UdonMIDIWebHelper
    {
        static void Main(string[] args)
        {
            Console.WriteLine("TeVirtualMIDI started");
            Console.WriteLine("using dll-version:    " + TeVirtualMIDI.versionString);
            Console.WriteLine("using driver-version: " + TeVirtualMIDI.driverVersionString);
            Console.WriteLine("Udon-MIDI-Web-Helper v7 Ready.  Press any key to end the program.");

            Thread logParserThread = new Thread(new ThreadStart(new LogParser().Run));
            logParserThread.Start();

            Console.ReadKey();
            logParserThread.Abort();

            Console.WriteLine("Udon-MIDI-Web-Helper v7 Stopped.  Press any key to close this console.");
            Console.ReadKey();
        }
    }
}
