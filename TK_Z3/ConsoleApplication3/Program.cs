using System;
using System.IO.Ports;
using System.Text;

namespace ConsoleApplication3
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("ATChat");
            Console.WriteLine("Coded by Dawid Michałowski and Robert Radczyc");
            Console.WriteLine("Avaiable ports:");
            foreach(var x in SerialPort.GetPortNames())
            {
                Console.WriteLine(x);
            }
            Console.Write("Provide port: ");
            var input = Console.ReadLine();
            foreach (var x in SerialPort.GetPortNames())
            {
                if(x == input)
                {
                    Console.Clear();
                    Console.WriteLine($"Connected to {input}.");
                    PortLoop(input);
                }
            }
            Console.WriteLine("Nie znaleziono portu.");
            Console.ReadLine();
           
        }

        public static void PortLoop(string com)
        {
            SerialPort _serialPort = new SerialPort(com, 9600);
            _serialPort.DtrEnable = true;
            _serialPort.RtsEnable = true;
            _serialPort.DataBits = 8;
            _serialPort.Handshake = Handshake.None;
            _serialPort.Parity = Parity.None;
            _serialPort.StopBits = StopBits.One;
            _serialPort.NewLine = System.Environment.NewLine;
            _serialPort.DataReceived += (s, e) => {
                SerialPort sp = (SerialPort)s;
                byte[] packOfBytes = new byte[sp.BytesToRead];
                Console.Write(Encoding.ASCII.GetString(packOfBytes, 0, sp.Read(packOfBytes, 0, packOfBytes.Length)));
            };
            _serialPort.Open();

            while (true)
            {
                var msg = Console.ReadLine();
                if (msg == "quit")
                {
                    _serialPort.Close();
                    System.Environment.Exit(1);
                }
                _serialPort.WriteLine(msg);
            }
        }
    }
}