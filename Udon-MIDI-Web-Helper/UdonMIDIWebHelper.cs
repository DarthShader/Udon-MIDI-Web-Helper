using System;
using System.Threading;

namespace Udon_MIDI_Web_Helper
{
    class UdonMIDIWebHelper
    {
        static void Main(string[] args)
        {
            Thread logParserThread = new Thread(new ThreadStart(new LogParser().Run));
            logParserThread.Start();

            Console.WriteLine("Udon-MIDI-Web-Helper v5 Ready.  Press any key to end the program.");

            Console.ReadKey();
            logParserThread.Abort();

            Console.WriteLine("Udon-MIDI-Web-Helper v5 Stopped.  Press any key to close this console.");
            Console.ReadKey();
        }
    }
}
