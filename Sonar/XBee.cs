using System;
using System.Collections.Generic;
using System.Text;
using System.IO.Ports;
using System.Threading;

namespace XBeeControl
{
    delegate void XBeeDataReceivedEventHandler(object sender, XBeeBinaryDataRecevedEventArgs e);
    delegate void XBeeComandResponceReceivedEventHandler (object sender, XBeeComandResponceReceivedEventArgs e);
    class XBee
    {
        public event XBeeDataReceivedEventHandler RF_Data_Received;
        public event XBeeComandResponceReceivedEventHandler RF_Command_Responce_Received;

        int _UART_ReadTimeout = 1000;


        SerialPort UART;
        Thread Receiver;
        bool ReceiveData = true;
        byte PackageCounter = 1;
        public int Address_Lenght = 2;

        public byte Received_RSSI = 0;
        byte Received_Options = 0;

        TransmitStatus TX_status;
        bool CommandMode = false;
        public int CheckSummErrors = 0;
        public int BytesSent = 0;
        public int BytesReceived = 0;
        public int Packets_Received = 0;


        public XBee(string port_name, int baud_rate)
        {
            UART = new SerialPort(port_name, baud_rate);
            UART.ReadTimeout = _UART_ReadTimeout;
            Receiver = new Thread(DataReceiver);
            Receiver.Name = "Receiver thread";
        }
        ~XBee()
        {
            ClosePort();
        }
        public void ClosePort()
        {
            ReceiveData = false;
            if (Receiver.IsAlive ) Receiver.Join();
            UART.Close();
            UART.Dispose();
        }
        public string OpenPort ()
        {
            try
            {
                UART.Open();
                Receiver.Start();
                return "OK";
            }
            catch (Exception ex)
            {
                return (ex.Message);
            }
        }
        public void EnterCommandMode()
        {
            try
            {
                Console.Write("Entering command mode...");
                CommandMode = true;
                UART.Write ("+++");
                Thread.Sleep(2000);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        public void ExitCommandMode()
        {
            if (UART.IsOpen)
            {
                Console.Write("Exiting command mode...");
                char[] tx_buffer = new char[5] { 'A', 'T', 'C', 'N', (char)13 };
                UART.Write(tx_buffer, 0,5);
                Thread.Sleep(1000);
                CommandMode = false;

            }
        }
        public void SendData(Int16 adr, string str)
        {
            Send(adr, str, 0x01, null);
        }
        public void SendCommand(string cmd)
        {
            Send(0, cmd, 0x08, null);
        }
        public void SendRemoteCommand(Int16 dev_adr, string cmd)
        {
            Send(dev_adr, cmd, 0x17, null);
        }
        private void Send(Int16 adr, string str, byte API_Command_ID, byte[] API_Command_Value)
        {
            byte[] frame;
            int tx_packet_pointer = 0;
            int tx_packet_data_lenght = 0;
            int frame_lenght = 0;
            byte[] data_bytes = Encoding.UTF8.GetBytes(str);
            for (int i = 0; i < data_bytes.Length; i += 100)
            {
                //Определяем размер текущего пакета
                if (API_Command_ID == 0x01)
                {
                    //Если передаем данные
                    if (data_bytes.Length - i >= 100) tx_packet_data_lenght = 100;
                    else tx_packet_data_lenght = data_bytes.Length - i;
                    frame_lenght = tx_packet_data_lenght + 5;
                }
                else if (API_Command_ID == 0x08)
                {
                    frame_lenght = tx_packet_data_lenght + 4;
                }
                else if (API_Command_ID == 0x17)
                {
                    frame_lenght = tx_packet_data_lenght + 15;
                }

                if ((API_Command_Value != null) && (API_Command_ID != 0x01))
                {
                    //Если команда с параметром, добавляем необходимое кол-во байт
                    frame_lenght += API_Command_Value.Length;
                }


                frame = new byte[frame_lenght + 4];

                frame[tx_packet_pointer++] = 0x7E;
                frame[tx_packet_pointer++] = 0;
                frame[tx_packet_pointer++] = (byte)(frame_lenght);
                frame[tx_packet_pointer++] = API_Command_ID;
                frame[tx_packet_pointer++] = GetNextFrameID();

                if (API_Command_ID == 0x17) // Если это команда для удаленного устройства, забиваем поле 64  битного адреса нулями
                //Если будет использоваться 64 битная адресация, нужно подставить адрес вместо нулей
                {
                    //Array.Copy(new byte[8], 0, frame, tx_packet_pointer, 8);
                    //for (int x = tx_packet_pointer; x < tx_packet_pointer + 8; x++)
                    //{
                    //    frame[tx_packet_pointer++] = 0;
                    //}
                    tx_packet_pointer += 8;
                }

                if ((API_Command_ID == 0x01) || (API_Command_ID == 0x17))
                {
                    frame[tx_packet_pointer++] = (Byte)(0xFF & (adr >> 8)); //Address MSB
                    frame[tx_packet_pointer++] = (Byte)(0xFF & (adr));      //Address LSB
                    frame[tx_packet_pointer++] = 0;                         //Options
                }
                //Пишем данные или строку с командой
                Array.Copy(data_bytes, i, frame, tx_packet_pointer, tx_packet_data_lenght);
                tx_packet_pointer += tx_packet_data_lenght;
                
                //Пишем параметры команды
                if ((API_Command_ID != 0x01) && (API_Command_Value != null))
                {
                    Array.Copy(API_Command_Value, 0, frame, tx_packet_pointer, API_Command_Value.Length);
                    tx_packet_pointer += API_Command_Value.Length;
                }
                
                frame[tx_packet_pointer] = CalcCheckSumm(ref frame);

                TX_status.FrameID = 0;
                TX_status.Status = 255;

                //if (API_Command_ID == 0x01)
                //{
                //    int retries = 0;
                //    int waitTime = 0;
                //    while ((TX_status.Status != 0) && (retries < 3))
                //    {
                //        UART.Write(frame, 0, frame.Length);

                //        while ((TX_status.FrameID != frame[4]) && (waitTime <3))
                //        {
                //            Thread.Sleep(100);
                //            waitTime++;
                //        }
                //        if (waitTime > 2) Console.WriteLine("No responce to sent packet");
                //        if (TX_status.Status == 1) Console.WriteLine("No Acknowledgement from remote device");
                //        retries++;
                //    }
                //    if (retries > 2)
                //    {
                //        Console.WriteLine("Sent packet error!");
                //    }
                //    else BytesSent += frame.Length;

                //}
                //else UART.Write(frame, 0, frame.Length);


                tx_packet_pointer = 0;
                tx_packet_data_lenght = 0;
                frame_lenght = 0;
                //Console.WriteLine(UART.BytesToWrite.ToString());
                UART.Write(frame, 0, frame.Length);
            }
            

        }
        byte CalcCheckSumm(ref byte[] frame)
        {
            Int16 res = 0;
            for (int i = 3; i < frame.Length-1; i++)
            {
                res = (Int16) (0xFF & (res + frame [i]));
            }
            return (byte) (0xFF - res);
        }
        bool IsCheckSummCorrect(ref byte [] frame)
        {
            Int16 res = 0;
            for (int i = 0; i < frame.Length; i++)
            {
                res = (Int16)(0xFF & (res + frame[i]));
            }
            if (res == 0xFF) return true;
            else return false;
        }

        byte GetNextFrameID()
        {
            PackageCounter++;
            if (PackageCounter == 0) PackageCounter++;
            return PackageCounter;
        }
        void DataReceiver()
        {
            int adr = 0;
            string r_data = "";
            byte [] rx_buffer;
            int message_lenght = 0;
            int read_count = 0;
            string CommandName;
            AT_Command_Status CommandStatus;
            byte [] cmdResultValue;

            while (ReceiveData)
            {
                rx_buffer = new byte [1];
                try
                {
                    read_count = UART.Read(rx_buffer, 0, 1);
                    Packets_Received++;
                }
                catch
                {
                }

                if ((read_count > 0) && (rx_buffer[0] == 126) && (!CommandMode)) //Ловим начало блока данных
                {
                    rx_buffer = new byte[2];
                    UART.Read(rx_buffer, 0, 2); //Считываем длину сообщения
                    message_lenght = rx_buffer[0] * 256 + rx_buffer[1] + 1;//В последнем байте контрольная сумма

                    rx_buffer = new byte[message_lenght];
                    read_count = UART.Read(rx_buffer, 0, message_lenght);

                    BytesReceived += message_lenght + 3;
                    if (IsCheckSummCorrect(ref rx_buffer))
                    {
                        int data_pointer = 0;
                        switch (rx_buffer[data_pointer++])
                        {
                            case 0x81: // Для 64 битных адресов ИД команды 0x80
                                #region //Блок данных
                                if (Address_Lenght == 2) //Тип адреса 16 бит
                                {
                                    adr = rx_buffer[data_pointer++] << 8;
                                    adr += rx_buffer[data_pointer++];
                                }
                                else if (Address_Lenght == 4) //Тип адреса 64 бит
                                {
                                    adr = rx_buffer[data_pointer++] << 56;
                                    adr += rx_buffer[data_pointer++] << 48;
                                    adr += rx_buffer[data_pointer++] << 40;
                                    adr += rx_buffer[data_pointer++] << 32;
                                    adr += rx_buffer[data_pointer++] << 24;
                                    adr += rx_buffer[data_pointer++] << 16;
                                    adr += rx_buffer[data_pointer++] << 8;
                                    adr += rx_buffer[data_pointer++];
                                }

                                Received_RSSI = rx_buffer[data_pointer++];
                                Received_Options = rx_buffer[data_pointer++];

                                byte[] r_data_array = new byte[rx_buffer.Length - data_pointer - 1];
                                Array.Copy(rx_buffer, data_pointer, r_data_array, 0, rx_buffer.Length - data_pointer - 1);
                                //r_data = new String(System.Text.Encoding.UTF8.GetChars(r_data_array));

                                onXbeeDataReceived(new XBeeBinaryDataRecevedEventArgs(adr, r_data_array));

                                #endregion
                                break;
                            case 0x89:
                                #region //Подтверждение отправки пакета
                                TX_status.FrameID = rx_buffer[data_pointer++];
                                TX_status.Status = rx_buffer[data_pointer++];
                                #endregion
                                break;
                            case 0x88:
                                #region //Результат выполнения АТ команды
                                data_pointer++;

                                byte[] cn_array = new byte[2];
                                Array.Copy(rx_buffer, data_pointer, cn_array, 0, 2);
                                CommandName = new String(System.Text.Encoding.UTF8.GetChars(cn_array));

                                data_pointer += 2;
                                CommandStatus = (AT_Command_Status)rx_buffer[data_pointer++];

                                //Command Value
                                //Value (Byte(s) 9-n) -- The HEX (non-ASCII) value of the requested register
                                cmdResultValue = null;
                                if (rx_buffer.Length - data_pointer - 1 > 0)
                                {
                                    cmdResultValue = new byte[rx_buffer.Length - data_pointer - 1];
                                    Array.Copy(rx_buffer, data_pointer, cmdResultValue, 0, rx_buffer.Length - data_pointer - 1);
                                }

                                onXbeeCommandResponceReceived(new XBeeComandResponceReceivedEventArgs(0, 0, CommandName, CommandStatus, cmdResultValue));
                                #endregion
                                break;
                            case 0x97:
                                #region //Результат выполнения AT команды на удаленном устройстве
                                data_pointer += 4; //Смещаемся на младшие 32 бита адреса удаленного устройства (64 битные адреса скорее всего не поддерживаются контроллером)
                                byte[] adrArr = new byte[4];
                                Array.Copy(rx_buffer, data_pointer, adrArr, 0, 4);
                                Int32 ResponderAddress = Array2Int32(adrArr);

                                data_pointer += 4; //Смещаемся на адрес сети
                                Int16 ResponderNetworkAddress = (Int16)(rx_buffer[data_pointer++] * 256 + rx_buffer[data_pointer++]);

                                byte[] cn1_array = new byte[2];
                                Array.Copy(rx_buffer, data_pointer, cn1_array, 0, 2);
                                CommandName = new String(System.Text.Encoding.UTF8.GetChars(cn1_array));

                                data_pointer += 2;
                                CommandStatus = (AT_Command_Status)rx_buffer[data_pointer++];

                                //Command Value

                                cmdResultValue = null;
                                if (rx_buffer.Length - data_pointer - 1 > 0)
                                {
                                    cmdResultValue = new byte[rx_buffer.Length - data_pointer - 1];
                                    Array.Copy(rx_buffer, data_pointer, cmdResultValue, 0, rx_buffer.Length - data_pointer - 1);
                                }

                                onXbeeCommandResponceReceived(new XBeeComandResponceReceivedEventArgs(ResponderAddress, ResponderNetworkAddress, CommandName, CommandStatus, cmdResultValue));
                                #endregion
                                break;
                            default:
                                break;
                        }
                    }
                    else
                    {
                        CheckSummErrors++;
                        //Console.WriteLine("Check summ error No:" + CheckSummErrors.ToString());

                        
                    }

                    
                }
                else if (CommandMode)
                {
                    Console.Write((char)rx_buffer[0]);
                }
            }


            
        }
        protected virtual void onXbeeDataReceived(XBeeBinaryDataRecevedEventArgs e)
        {
            if (RF_Data_Received != null)
            {
                RF_Data_Received(this, e);
            }
        }
        protected virtual void onXbeeCommandResponceReceived(XBeeComandResponceReceivedEventArgs e)
        {
            if (RF_Command_Responce_Received != null)
            {
                RF_Command_Responce_Received(this, e);
            }
        }

        public byte API_Mode
        {
            get
            {
                EnterCommandMode();
                if (UART.IsOpen)
                {
                    Console.Write("API mode = ");
                    string cmd = "ATAP" + (char)13;
                    byte[] tx_buffer = System.Text.Encoding.ASCII.GetBytes(cmd);

                    UART.Write(tx_buffer, 0, cmd.Length);
                    Thread.Sleep(1000);
                    ExitCommandMode();
                }
                

                return 0;
            }
            set
            {
                EnterCommandMode();
                if (UART.IsOpen)
                {
                    Console.Write("Setting API mode = " + value.ToString());
                    string cmd = "ATAP"+ value.ToString() +",WR" + (char) 13;
                    byte[] tx_buffer = System.Text.Encoding.ASCII.GetBytes(cmd);
                    UART.Write(tx_buffer, 0, cmd.Length);
                    Thread.Sleep(1000);

                    cmd = "ATAP" + (char)13;
                    tx_buffer = System.Text.Encoding.ASCII.GetBytes(cmd);
                    UART.Write(tx_buffer, 0, cmd.Length);
                    Console.Write("API mode = ");
                    Thread.Sleep(1000);
                    ExitCommandMode();
                }
            }
        }
        public void ClearBuffer()
        {
            UART.DiscardOutBuffer();
            UART.DiscardInBuffer();
        }
        Int32 Array2Int32(byte[] ar)
        {
            Int32 mult = 1;
            Int32 rez = 0;
            for (int i = ar.Length - 1; i >= ar.Length - 4; i--)
            {
                rez += ar[i] * mult;
                mult = mult << 8;

            }
            return rez;
        }
        public int WriteBufferSize
        {
            get { return UART.WriteBufferSize; }
        }
    }

    class XBeeDataRecevedEventArgs : EventArgs
    {
        int _Address;
        string _data;
        public XBeeDataRecevedEventArgs(int adr, string data_str)
        {
            _Address = adr;
            _data = data_str;
        }
        public int Address
        {
            get { return _Address; }
        }
        public string DataString
        {
            get { return _data; }
        }
    }

    class XBeeBinaryDataRecevedEventArgs : EventArgs
    {
        int _Address;
        byte [] _data;
        public XBeeBinaryDataRecevedEventArgs(int adr, byte []  data_str )
        {
            _data = new byte[20];
            _Address = adr;
            _data = data_str;
        }
        public int Address
        {
            get { return _Address; }
        }
        public byte [] DataArray
        {
            get { return _data; }
        }
    }
    class XBeeComandResponceReceivedEventArgs : EventArgs
    {
        int _Address = 0;
        int _NetAddr = 0;
        string _cmd = "";
        AT_Command_Status _cmd_status;
        byte[] _cmd_result = null;
        public XBeeComandResponceReceivedEventArgs(int devAdr, int netAdr, string cmd, AT_Command_Status cmd_status, byte[] cmd_result)
        {
            _Address = devAdr;
            _NetAddr = netAdr;
            _cmd = cmd;
            _cmd_status = cmd_status;
            _cmd_result = cmd_result;
        }
        public int DeviceAddress
        {
            get { return _Address; }
        }
        public int NetAddress
        {
            get { return _NetAddr; }
        }
        public string Command
        {
            get { return _cmd; }
        }
        public AT_Command_Status CommandStatus
        {
            get { return _cmd_status; }
        }
        public byte[] CommandResult
        {
            get { return _cmd_result; }
        }

    }
    struct TransmitStatus
    {
        public byte FrameID;
        public byte Status;
    }
    public enum TransmitSatuses
    {
        Success = 0,
        No_ASK_Received = 1,
        CCA_Falture = 2,
        Purged = 3
    }
    public enum AT_Command_Status
    {
        OK = 0,
        ERROR = 1,
        INVALID_COMMAND = 2,
        INVALID_PARAMETER = 3,
        NA = 255
    }
}
