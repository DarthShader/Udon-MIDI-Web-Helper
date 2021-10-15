using System;
using System.Text;
using System.IO;

namespace Udon_MIDI_Web_Helper
{
    // https://stackoverflow.com/questions/420429/mirroring-console-output-to-a-file
    class ConsoleCopy : IDisposable
    {

        FileStream fileStream;
        StreamWriter fileWriter;
        TextWriter doubleWriter;
        TextWriter oldOut;

        class DoubleWriter : TextWriter
        {

            TextWriter one;
            TextWriter two;

            public DoubleWriter(TextWriter one, TextWriter two)
            {
                this.one = one;
                this.two = two;
            }

            public override Encoding Encoding
            {
                get { return one.Encoding; }
            }

            public override void Flush()
            {
                one.Flush();
                two.Flush();
            }

            public override void Write(char value)
            {
                one.Write(value);
                two.Write(value);
            }

            public override void WriteLine(string value)
            {
                var time = DateTime.Now.ToString();
                one.WriteLine(time + " " + value);
                two.WriteLine(time + " " + value);
            }

        }

        public ConsoleCopy(string path)
        {
            oldOut = Console.Out;

            try
            {
                fileStream = File.Create(path);

                fileWriter = new StreamWriter(fileStream);
                fileWriter.AutoFlush = true;

                doubleWriter = new DoubleWriter(fileWriter, oldOut);
            }
            catch (Exception e)
            {
                Console.WriteLine("Cannot open file for writing");
                Console.WriteLine(e.Message);
                return;
            }
            Console.SetOut(doubleWriter);
            Console.SetError(doubleWriter);
        }

        public void Dispose()
        {
            Console.SetOut(oldOut);
            if (fileWriter != null)
            {
                fileWriter.Flush();
                fileWriter.Close();
                fileWriter = null;
            }
            if (fileStream != null)
            {
                fileStream.Close();
                fileStream = null;
            }
        }

    }
}