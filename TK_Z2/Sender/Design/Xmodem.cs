using System;
using System.Threading;
using System.IO.Ports;

namespace Design
{
    public class Xmodem
    {
        private enum Stances
        {
            SenderAwaitingFileInitiation,       // Program czeka na potwierdzenie rozpoczecia transmisji
            SenderPacketSent,                   // Program wyslal pakiet i czeka na odp.
            SenderAwaitingEndOfFileConfirmation,// Program wyslal bajt konca pliku i oczekuje od odbiornika potwierdzenia odbioru
            Idle                                // Program jest w stanie spoczynku
        }

        ManualResetEvent WaitForResponseFromReceiver = new ManualResetEvent(false);
        Timer SenderPacketResponseWatchdog;
        byte[] SenderDataPacketMasterTemplate;
        byte[] DataPacketToSend;
        bool TerminateSend;
        int NumUserDataBytesAddedToCurrentPacket = 0;
        public byte FillBit;
        public byte EndOfFileByteToSend;

        //CONST
        private const int PacketSize = 128;    // Rozmiar pakietu
        public const byte C = 43;     // Inicjalizacja transferu
        public const byte SOH = 1;    // Początek nagłówka pakietu
        public const byte NAK = 15;   // Odebrano z niepowodzeniem
        public const byte ACK = 6;    // Odebrano
        public const byte EOT = 4;    // Koniec transmisji

        public Xmodem(SerialPort port, byte fullfillbit = 26, byte endOfFileByteToSend = 4)
        {
            Port = port;
            FillBit = fullfillbit;
            EndOfFileByteToSend = endOfFileByteToSend;            
        }

        
        private Stances CurrentStance = Stances.Idle;
        public SerialPort Port;
        private int _NumCancellationBytesReceived = 0;
        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort sp = sender as SerialPort;
            int numBytes = sp.BytesToRead;
            byte[] recv = new byte[numBytes];
            sp.Read(recv, 0, numBytes);

            if (numBytes > 0)
            {
                switch (CurrentStance)
                {
                    case Stances.SenderAwaitingFileInitiation:
                            if (Array.IndexOf(recv, C) > -1 || Array.IndexOf(recv, NAK) > -1)  
                            {
                                // C lub NAK odebrany
                                WaitForResponseFromReceiver.Set();
                            }
                        break;
                    case Stances.SenderPacketSent:
                            if (Array.IndexOf(recv, ACK) > -1)
                            {
                                // ACK odebrany
                              
                                PacketSuccessfullySent = true;
                                _BlockNumToSend += 1;
                                WaitForResponseFromReceiver.Set();
                            }
                            else if (Array.IndexOf(recv, NAK) > -1)
                            {
                                // NAK odebrany
                                WaitForResponseFromReceiver.Set();
                            }
                        break;
                    case Stances.SenderAwaitingEndOfFileConfirmation:
                            if (Array.IndexOf(recv, ACK) > -1)
                            {
                                //odebranie ACK oznaczajacy odebranie znaku konca pliku
                                EndOfFileAcknowledgementReceived = true;        
                                Abort();
                            }
                        
                        break;
                }
            }
        }
        private CRC16 CRC = new CRC16(CRC16.InitialCrcValue.Zeros);  // CRC-16 CCITT przy użyciu wielomian (X^16 + X^12 + X^5	+ 1)
        private byte[] UShortToBytes(ushort val)
        {
            byte highByte = (byte)(val / 256);
            byte lowByte = (byte)(val % 256);
            return new byte[] { highByte, lowByte };
        }
        private bool Aborted = false;
        private void Abort()
        {
            CurrentStance = Stances.Idle;
            TerminateSend = true;
            SenderInitialized = false;
            Aborted = true;
            Port.DataReceived -= Port_DataReceived;                  // Usuniecie zbednego eventu        
            WaitForResponseFromReceiver.Set();                       // Jesli dane sa wysylane, przestajemy oczekiwac na odpowiedzi odbiornika
            SenderPacketResponseWatchdog?.Change(Timeout.Infinite, Timeout.Infinite);
            Port.DiscardInBuffer();                                 // Wyczysc dane portu
            Port.DiscardOutBuffer();
        }
        
        private void SendInit()
        {
            int dataPacketSize;
            dataPacketSize = PacketSize;

            SenderDataPacketMasterTemplate = new byte[dataPacketSize];
            for (int k = 0; k < SenderDataPacketMasterTemplate.Length; k++)  // wypelnienie bitami dopelnienia
                SenderDataPacketMasterTemplate[k] = FillBit;
        }

        private byte _BlockNumToSend = 1;
        public int Send(byte[] dataToSend = null)
        {
            SendInit();     //inicjalizacja danych przed rozpoczeciem wysylania, konstruktor wysylania
            Aborted = false;
            _BlockNumToSend = 1;    // Obecny numer bloku wychodzacego
            PacketSuccessfullySent = false;
            DataPacketToSend = null;
            NumUserDataBytesAddedToCurrentPacket = 0;
            _TotalUserDataBytesSent = 0;
            _NumCancellationBytesReceived = 0;
            TerminateSend = false;
            EndOfFileAcknowledgementReceived = false;
            SenderInitialized = true;   // zabezpiecznie blokujace wywolanie AddToOutboundPacket() przed Send()
            WaitForResponseFromReceiver.Reset();
            CurrentStance = Stances.SenderAwaitingFileInitiation;
            if (Port.IsOpen == false)  // Jesli port jest zamkniety, to otworz
                Port.Open();
            Port.DiscardInBuffer();
            Port.DiscardOutBuffer();
            Port.DataReceived += Port_DataReceived; // dodaj event odbioru
            WaitForResponseFromReceiver.WaitOne(); // oczekiwanie na bit potwierdzenia rozpoczecia transferu od odbiornika
            WaitForResponseFromReceiver.Reset();

            if (dataToSend != null && Aborted == false)
            {
                AddToOutboundPacket(dataToSend);
                if (TerminateSend == false)
                    EndFile();
                return _TotalUserDataBytesSent;
            }
            return 0;
        }

        private int _TotalUserDataBytesSent = 0;
        private bool SenderInitialized = false;
        public int AddToOutboundPacket(byte[] dataToSend)
        {
            int numUnpaddedDataBytesSentThisCall = 0;   // liczba wyslanych bajtow, dzieki temu wywolaniu
            if (SenderInitialized == false)  // zabezpiecznie blokujace wywolanie AddToOutboundPacket() przed Send()
            {
                throw new ArgumentException("[XMODEM CLASS ERROR] Send() method must first be called before AddToOutboundPacket() is used.");
            }
            int dataOffset = 0;
            while (dataOffset < dataToSend.Length && TerminateSend == false)
            {
                if (DataPacketToSend == null) // inicjalizacja pakietu wyjsciowego
                {
                    DataPacketToSend = new byte[PacketSize];  //pakiet do wyslania ma wielkosc 128 bajtow
                    Array.Copy(SenderDataPacketMasterTemplate, DataPacketToSend, DataPacketToSend.Length);
                }
                int numUnparsedDataBytes = dataToSend.Length - dataOffset;
                int numPacketDataBytesNeeded = DataPacketToSend.Length - NumUserDataBytesAddedToCurrentPacket;
                int numBytesToAdd;
                if (numPacketDataBytesNeeded >= numUnparsedDataBytes)
                    numBytesToAdd = numUnparsedDataBytes;
                else
                    numBytesToAdd = numPacketDataBytesNeeded;
                Array.Copy(dataToSend, dataOffset, DataPacketToSend, NumUserDataBytesAddedToCurrentPacket, numBytesToAdd);
                NumUserDataBytesAddedToCurrentPacket += numBytesToAdd;
                dataOffset += numBytesToAdd;
                if (NumUserDataBytesAddedToCurrentPacket >= DataPacketToSend.Length)
                {
                    TransmitPacket();
                        _TotalUserDataBytesSent += NumUserDataBytesAddedToCurrentPacket;
                        numUnpaddedDataBytesSentThisCall += NumUserDataBytesAddedToCurrentPacket;
                        NumUserDataBytesAddedToCurrentPacket = 0;
                        DataPacketToSend = null;

                }
            }
            return numUnpaddedDataBytesSentThisCall;
        }
        private bool PacketSuccessfullySent = false;
        private void TransmitPacket()
        {
            byte[] checkValueBytes;                                             // pojemnik na CRC
            ushort checkValueShort = CRC.ComputeChecksum(DataPacketToSend);
            checkValueBytes = UShortToBytes(checkValueShort);
            byte packetSizeHeader;              // rozmiar pakietu
            packetSizeHeader = SOH;
            PacketSuccessfullySent = false;
            while (PacketSuccessfullySent == false && TerminateSend == false)
            {
                Port.Write(new byte[] { packetSizeHeader }, 0, 1);      // Naglowek
                Port.Write(new byte[] { _BlockNumToSend }, 0, 1);       // liczba bloku
                Port.Write(new byte[] { (byte)(255 - _BlockNumToSend) }, 0, 1);  // Uzupelniona liczba bloku
                Port.Write(DataPacketToSend, 0, DataPacketToSend.Length);  // Wyslij pakiet
                WaitForResponseFromReceiver.Reset();
                CurrentStance = Stances.SenderPacketSent;
                Port.Write(checkValueBytes, 0, checkValueBytes.Length);// Wyslij CRC
                WaitForResponseFromReceiver.WaitOne();// Oczekiwanie na ACK/NAK
                WaitForResponseFromReceiver.Reset();
            }
        }

        private bool EndOfFileAcknowledgementReceived = false;
        public int EndFile()
        {
            if (NumUserDataBytesAddedToCurrentPacket > 0)
            {
                TransmitPacket();
                _TotalUserDataBytesSent += NumUserDataBytesAddedToCurrentPacket;
            }
            CurrentStance = Stances.SenderAwaitingEndOfFileConfirmation;
            int numEndOfFileBytesSent = 0;
            while (EndOfFileAcknowledgementReceived == false )
            {
                WaitForResponseFromReceiver.Reset();
                Port.Write(new byte[] { EndOfFileByteToSend }, 0, 1);
                numEndOfFileBytesSent += 1;
                WaitForResponseFromReceiver.WaitOne();
            }

            if (EndOfFileAcknowledgementReceived)
            {
                Abort();
                return NumUserDataBytesAddedToCurrentPacket;
            }
                Abort();
                return 0;
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