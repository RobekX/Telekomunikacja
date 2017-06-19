
using System;
using System.Threading;
using System.IO;
using System.IO.Ports;

namespace Design
{
    public class Xmodem
    {
        public const byte C = 43;     // Inicjalizacja transferu
        public const byte SOH = 1;    // Początek nagłówka pakietu
        public const byte NAK = 15;   // Odebrano z niepowodzeniem
        public const byte ACK = 6;    // Odebrano
        public const byte EOT = 4;    // Koniec transmisji
        private int PacketSize = 128;
        public int ReceiverFileInitiationRetryMillisec = 250;
        public int ReceiverMaxConsecutiveRetries = 10;

        public Xmodem(SerialPort port, byte paddingByte = 26, byte endOfFileByteToSend = 4)
        {
            PaddingByte = paddingByte;
            EndOfFileByteToSend = endOfFileByteToSend;
            Port = port;
        }
        private enum Stances
        {
            Inactive,                           // The object is neither Sending nor Receiving
            ReceiverFileInitiation,             // Receiver is sending the file initiation byte at regular intervals
            ReceiverHeaderSearch,               // Receiver is expecting SOH or STX packet header
            ReceiverBlockNumSearch,             // Receiver is expecting the block number
            ReceiverBlockNumComplementSearch,   // Receiver is expecting the block number complement
            ReceiverDataBytesSearch,            // Receiver is populating data bytes
            ReceiverErrorCheckSearch,           // Receiver is expecting 1-byte or 2-byte check value(s)
        }
        private Stances CurrentState = Stances.Inactive;
        public delegate void PacketReceivedEventHandler(Xmodem sender, byte[] packet, bool endOfFileDetected);
        public event PacketReceivedEventHandler PacketReceived;
        public SerialPort Port;
        private ManualResetEvent ReceiverUserBlock = new ManualResetEvent(false);
        private MemoryStream AllDataReceivedBuffer;

        public void Receive(MemoryStream allDataReceivedBuffer = null)
        {
            Aborted = false;
            BlockNumExpected = 1;
            Remainder = new byte[0];
            DataPacketNumBytesStored = 0;
            ExpectingFirstPacket = true;
            ValidPacketReceived = false;

            FileInitiationByteToSend = C;
            CurrentState = Stances.ReceiverFileInitiation;
            if (Port.IsOpen == false)
                Port.Open();
            Port.DiscardInBuffer();
            Port.DiscardOutBuffer();
            AllDataReceivedBuffer = allDataReceivedBuffer;
            Port.DataReceived += Port_DataReceived;
            if (ReceiverFileInitiationTimer == null)
                ReceiverFileInitiationTimer = new Timer(ReceiverFileInitiationRoutine, null, 0, ReceiverFileInitiationRetryMillisec);
            else
                ReceiverFileInitiationTimer.Change(0, ReceiverFileInitiationRetryMillisec);
            ReceiverUserBlock.Reset();
            ReceiverUserBlock.WaitOne();
            ReceiverUserBlock.Reset();
        }
        private byte FileInitiationByteToSend;
        private Timer ReceiverFileInitiationTimer;
        private void ReceiverFileInitiationRoutine(object notUsed)
        {
            Port.Write(new byte[] { FileInitiationByteToSend }, 0, 1);
        }
        

        private bool ValidPacketReceived = false;
        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort sp = sender as SerialPort;
            int numBytes = sp.BytesToRead;
            byte[] recv = new byte[numBytes];
            sp.Read(recv, 0, numBytes);
            if (numBytes > 0)
            {
                switch (CurrentState)
                {
                    case Stances.ReceiverFileInitiation:
                        if (ReceiverFileInitiationTimer != null)
                            ReceiverFileInitiationTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        CurrentState = Stances.ReceiverHeaderSearch;
                        ReceiverPacketBuilder(recv);
                        break;
                    case Stances.ReceiverHeaderSearch:
                    case Stances.ReceiverBlockNumSearch:
                    case Stances.ReceiverBlockNumComplementSearch:
                    case Stances.ReceiverDataBytesSearch:
                    case Stances.ReceiverErrorCheckSearch:
                        ReceiverPacketBuilder(recv);
                        break;
                }
            }
        }
        private void SendNAK()
        {
            Port.Write(new [] { NAK }, 0, 1);
            ReceiverNumConsecutiveNAKSent += 1;
        }

        private void SendACK()
        {
            Port.Write(new [] { ACK }, 0, 1);
            ReceiverNumConsecutiveNAKSent = 0;
        }
        public int _ReceiverNumConsecutiveNAKSent = 0;
        public int ReceiverNumConsecutiveNAKSent
        {
            get { return _ReceiverNumConsecutiveNAKSent; }
            set
            {
                _ReceiverNumConsecutiveNAKSent = value;

                if (_ReceiverNumConsecutiveNAKSent >= ReceiverMaxConsecutiveRetries)
                {
                    Abort();
                    ReceiverUserBlock.Set();
                }
            }
        }
        private int ExpectedDataPacketSize;
        private byte BlockNumReceived;
        private byte BlockNumComplementCandidateReceived;
        private bool ExpectingFirstPacket = true;
        private byte BlockNumExpected = 1;
        private byte[] BytesToParse;
        private byte[] Remainder = new byte[0];
        private byte[] DataPacketReceived;
        private int DataPacketNumBytesStored = 0;
        private byte[] ErrorCheck;
        private CRC16 CRC = new CRC16(CRC16.InitialCrcValue.Zeros);

        private void ReceiverPacketBuilder(byte[] freshBytes)
        {
            if (freshBytes.Length == 0)
                return;
            if (Remainder.Length > 0 && CurrentState == Stances.ReceiverHeaderSearch)
                BytesToParse = CombineArrays(Remainder, freshBytes);
            else
                BytesToParse = freshBytes;
            int headerByteSearchStartIndex = 0;
            int searchStartIndex = 0;
            while (searchStartIndex < BytesToParse.Length && headerByteSearchStartIndex < BytesToParse.Length)
            {
                if (CurrentState == Stances.ReceiverHeaderSearch)
                {
                    if (Remainder.Length > 0)
                        Remainder = new byte[0];
                    if (BytesToParse[headerByteSearchStartIndex] == EOT)
                    {
                        SendACK();
                        Abort();
                        if (PacketReceived != null && ValidPacketReceived && DataPacketReceived != null && DataPacketReceived.Length > 0)
                            PacketReceived(this, DataPacketReceived, true);
                        ReceiverUserBlock.Set();
                        return;
                    }
                    else
                    {
                        if (PacketReceived != null && ValidPacketReceived == true && DataPacketReceived != null && DataPacketReceived.Length > 0)
                            PacketReceived(this, DataPacketReceived, false);
                    }
                    ValidPacketReceived = false;
                    int foundIndex = Array.IndexOf(BytesToParse, SOH, headerByteSearchStartIndex);
                    if (foundIndex == -1) // Nie znaleziono naglowka
                    {
                        return;
                    }
                    else if (foundIndex > -1)
                    {
                        headerByteSearchStartIndex = foundIndex + 1;
                        ExpectedDataPacketSize = PacketSize;
                        searchStartIndex = foundIndex + 1;
                        CurrentState = Stances.ReceiverBlockNumSearch;
                        continue;
                    }
                }
                if (CurrentState == Stances.ReceiverBlockNumSearch)
                {
                    BlockNumReceived = BytesToParse[searchStartIndex];
                    if (BlockNumReceived == SOH)
                    {
                        Remainder = new [] { BlockNumReceived };   // Inicjalizacja remindera
                    }
                    searchStartIndex += 1;
                    CurrentState = Stances.ReceiverBlockNumComplementSearch;
                    continue;
                }

                if (CurrentState == Stances.ReceiverBlockNumComplementSearch)
                {
                    BlockNumComplementCandidateReceived = BytesToParse[searchStartIndex];
                    if (BlockNumComplementCandidateReceived == 255 - BlockNumReceived)
                    {
                        if (Remainder.Length > 0)
                            Remainder = new byte[0];
                        DataPacketReceived = new byte[ExpectedDataPacketSize];
                        DataPacketNumBytesStored = 0;
                        searchStartIndex += 1;
                        CurrentState = Stances.ReceiverDataBytesSearch;
                        continue;
                    }
                    else
                    {
                        if (Remainder.Length > 0 && BlockNumReceived == SOH)
                        {
                            Remainder = CombineArrays(Remainder, new [] { BlockNumComplementCandidateReceived });   // Uzupelnianie remindera
                        }
                        CurrentState = Stances.ReceiverHeaderSearch;
                        continue;
                    }
                }

                if (CurrentState == Stances.ReceiverDataBytesSearch)
                {
                    int numUnparsedBytesRemaining = BytesToParse.Length - searchStartIndex;
                    int numDataPacketBytesStillMissing = DataPacketReceived.Length - DataPacketNumBytesStored;
                    int numDataBytesToPull;
                    if (numUnparsedBytesRemaining >= numDataPacketBytesStillMissing)
                        numDataBytesToPull = numDataPacketBytesStillMissing;
                    else
                        numDataBytesToPull = numUnparsedBytesRemaining;
                    Array.Copy(BytesToParse, searchStartIndex, DataPacketReceived, DataPacketNumBytesStored, numDataBytesToPull);
                    DataPacketNumBytesStored += numDataBytesToPull;
                    searchStartIndex += numDataBytesToPull;
                    if (DataPacketNumBytesStored >= ExpectedDataPacketSize)
                    {
                        CurrentState = Stances.ReceiverErrorCheckSearch;
                        ErrorCheck = new byte[0];
                    }
                    continue;
                }

                if (CurrentState == Stances.ReceiverErrorCheckSearch)
                {
                    if (ErrorCheck.Length < 2)
                    {
                        ErrorCheck = CombineArrays(ErrorCheck, new byte[] { BytesToParse[searchStartIndex] });
                    }
                    if (ErrorCheck.Length >= 2)
                    {
                        ValidatePacket();
                        headerByteSearchStartIndex = searchStartIndex + 1;
                        CurrentState = Stances.ReceiverHeaderSearch;
                    }
                    else
                    {
                        searchStartIndex += 1;
                    }
                }
            } 
        }

        private void ValidatePacket()
        {
            if (BlockNumReceived == BlockNumExpected)
            {
                if (ValidateChecksum() == true)
                {
                    BlockNumExpected += 1;
                    ExpectingFirstPacket = false;
                    if (AllDataReceivedBuffer != null)
                        AllDataReceivedBuffer.Write(DataPacketReceived, 0, DataPacketReceived.Length);
                    ValidPacketReceived = true;
                    SendACK();
                }
                else
                {
                    SendNAK();
                    ValidPacketReceived = false;
                }
            }
            else if (ExpectingFirstPacket == false && BlockNumReceived == (byte)(BlockNumExpected - 1))
            {
                SendACK();
                ValidPacketReceived = false;
            }
            else
            {
                SendNAK();
                ValidPacketReceived = false;
            }
        }
        private bool ValidateChecksum()
        {
            ushort crcChecksumCalculated = CRC.ComputeChecksum(DataPacketReceived);
            ushort crcChecksumReceived = BytesToUShort(ErrorCheck[0], ErrorCheck[1]);
            if (crcChecksumCalculated == crcChecksumReceived)
                return true;
            else
                return false;
        }

        private ushort BytesToUShort(byte highByte, byte lowByte)
        {
            return (ushort)((highByte << 8) + lowByte);
        }
        private bool Aborted = false;
        private void Abort()
        {
            CurrentState = Stances.Inactive;
            Aborted = true;
            Port.DataReceived -= Port_DataReceived;

            if (ReceiverFileInitiationTimer != null)
                ReceiverFileInitiationTimer.Change(Timeout.Infinite, Timeout.Infinite);

            if (ReceiverStillAliveWatchdog != null)
                ReceiverStillAliveWatchdog.Change(Timeout.Infinite, Timeout.Infinite);

            Port.DiscardInBuffer();
            Port.DiscardOutBuffer();
        }

        private Timer ReceiverStillAliveWatchdog;
        public byte PaddingByte;
        public byte EndOfFileByteToSend;
        public byte[] TrimPaddingBytesFromEnd(byte[] input, byte paddingByteToRemove = 26)
        {
            int numBytesToDiscard = 0;
            for (int k = input.Length - 1; k >= 0; k--)
            {
                if (input[k] == paddingByteToRemove)
                    numBytesToDiscard += 1;
                else
                    break;
            }
            int numBytesToKeep = input.Length - numBytesToDiscard;
            byte[] output = new byte[numBytesToKeep];
            Array.Copy(input, output, numBytesToKeep);
            return output;
        }
        private byte[] CombineArrays(byte[] Array1, byte[] Array2)
        {
            int length1 = Array1.Length;
            int length2 = Array2.Length;
            byte[] combinedArray = new byte[length1 + length2];
            Array1.CopyTo(combinedArray, 0);
            Array2.CopyTo(combinedArray, length1);
            return combinedArray;
        }
    }

    public class CRC16
    {
        const ushort poly = 4129;
        ushort[] table = new ushort[256];
        ushort initialValue = 0;
        public ushort ComputeChecksum(byte[] bytes)
        {
            ushort crc = this.initialValue;
            for (int i = 0; i < bytes.Length; ++i)
            {
                crc = (ushort)((crc << 8) ^ table[((crc >> 8) ^ (0xff & bytes[i]))]);
            }
            return crc;
        }
        public byte[] ComputeChecksumBytes(byte[] bytes)
        {
            ushort crc = ComputeChecksum(bytes);
            return BitConverter.GetBytes(crc);
        }
        public enum InitialCrcValue { Zeros, NonZero1 = 0xffff, NonZero2 = 0x1D0F }
        public CRC16(InitialCrcValue initialValue)
        {
            this.initialValue = (ushort)initialValue;
            ushort temp, a;
            for (int i = 0; i < table.Length; ++i)
            {
                temp = 0;
                a = (ushort)(i << 8);
                for (int j = 0; j < 8; ++j)
                {
                    if (((temp ^ a) & 0x8000) != 0)
                    {
                        temp = (ushort)((temp << 1) ^ poly);
                    }
                    else
                    {
                        temp <<= 1;
                    }
                    a <<= 1;
                }
                table[i] = temp;
            }
        }
    }
}
