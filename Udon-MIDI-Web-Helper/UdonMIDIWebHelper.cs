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
            Console.WriteLine("Udon-MIDI-Web-Helper v11 Ready.  Press Escape to end the program.");

            Thread logParserThread = new Thread(new ThreadStart(new LogParser().Run));
            logParserThread.Start();

            while (Console.ReadKey().Key != ConsoleKey.Escape)
                ;
            logParserThread.Abort();
        }
    }
}
