using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Xml.Serialization;
using TestingSoftware.Utilities;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
namespace TestingSoftware.Protocols.Bluetoolth
{
    public class BluetoothDataLink : Datalink
    {

        /// <summary>
        /// The last start time.
        /// </summary>
        protected Int64 _startTime = 0;



        /// <summary>
        /// The callcellation token source used to terminate the task.
        /// </summary>
        [XmlIgnore]
        protected CancellationTokenSource? _cts;

        /// <summary>
        /// The transmitting task.
        /// </summary>
        [XmlIgnore]
        protected Task<int>? _txTask;

        /// <summary>
        /// Use to cache the transmitting pdus.
        /// </summary>
        [XmlIgnore]
        protected Channel<Pdu> _txChannel;

        /// <summary>
        /// Only used to cached the transmitted pdus.
        /// </summary>
        [XmlIgnore]
        protected Channel<Pdu> _fdbkChannel;

        /// <summary>
        /// The received message cached here.
        /// </summary>
        [XmlIgnore]
        protected Channel<Pdu> _rxChannel;


        /// <summary>
        /// The receive buffer.
        /// </summary>
        [XmlIgnore]
        protected PduReceiveBuffer _rxBuffer = new PduReceiveBuffer();
        /// <summary>
        /// The message head.
        /// </summary>
        public const UInt16 MSG_HEAD = 0x55AA;

        /// <summary>
        /// The maximum size for receive buffer.
        /// </summary>
        public const int RX_BUF_SIZE = 32 * 1024;

        [XmlIgnore]
        public static GattCharacteristic? _writeCharacteristic;
        [XmlIgnore]
        public static GattCharacteristic? _notifyCharacteristic;

        /// <summary>
        /// The receive parsing task.
        /// </summary>
        [XmlIgnore]
        protected Task? _rxParseTask;
        public BluetoothDataLink() : base(Layer.Datalink, "BluetoothDataLink")
        {
            // Create the transmitting and receiving channels.
            _txChannel = Channel.CreateBounded<Pdu>(5);
            _fdbkChannel = Channel.CreateBounded<Pdu>(5);
            _rxChannel = Channel.CreateBounded<Pdu>(100);
        }

        /// The method is used to open a serial port and start transmitting and receiving data.
        /// </summary>
        /// <returns></returns>
        public override async Task<int> StartAsync()
        {
            int ret = 0;
            if (_notifyCharacteristic == null || _writeCharacteristic == null) return -1;

            // 重连后 GATT 特征对象是新的：先解绑再订阅，保证新特征一定有人监听（否则报文只进 adapter 不进 UDS）
            _notifyCharacteristic.ValueChanged -= OnDataReceived;
            _notifyCharacteristic.ValueChanged += OnDataReceived;
            // 清掉上一轮残留的半帧数据，避免新连接首帧解析错位
            _rxBuffer = new PduReceiveBuffer();
            // Save the start time.
            _startTime = HighResolutionTimer.Milliseconds;
            // Create the transmitting task.
            _cts = new CancellationTokenSource();

            // 
            if (_txTask == null)
            {
                _txTask = Task.Run(async () =>
                {
                    // Exit the task loop if canncellation token set.
                    while (!_cts.Token.IsCancellationRequested)
                    {

                        try
                        {
                            // Read a new PDU from the channel.
                            Pdu? pdu = await _txChannel.Reader.ReadAsync(_cts.Token).ConfigureAwait(false);

                            // Call the adapter transmit method to send the PDU
                            if (pdu != null)
                            {
                                ret = Transmit(pdu);
                            }
                        }
                        catch (OperationCanceledException ex)
                        {
                            Log.Error(ex, "Failed to read pdu from UART transmitting buffer");
                            break;
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Failed to read pdu from UART transmitting buffer");
                            break;
                        }
                    }

                    return ret;
                });
            }

            // Create the receive parsing task.
            if (_rxParseTask == null)
            {
                _rxParseTask = Task.Run(async () =>
                {
                    while (!_cts.Token.IsCancellationRequested)
                    {
                        var number = 0;
                        Int64 _lastTimes = 0;
                        // Parse the pdus in thread pool.
                        try
                        {

                            while (true)
                            {
                                Pdu? pdu = new Pdu();

                                // Try to parse out the pdu from the receiving buffer.
                                pdu.Data = _rxBuffer.ParsePdu();

                                // There is not entire PDU in the receiving buffer. Exit.
                                if (pdu.Data == null) break;
                                if (StartPasePduTime == 0)
                                {
                                    StartPasePduTime = HighResolutionTimer.Milliseconds;
                                    if (LastPasePduTime == 0)
                                    {
                                        LastPasePduTime = StartPasePduTime;
                                    }
                                }
                                //Log.Information($"Bluetooth Rx: Time:{_lastTimes} {number}");
                                if (_lastTimes != 0 && number >= 1)
                                {
                                    pdu.Timestamp = _lastTimes + number * 10; // Add the time offset for each pdu.
                                    number++;
                                }
                                else
                                {
                                    number = 1;
                                    pdu.Timestamp = HighResolutionTimer.Milliseconds - StartPasePduTime;
                                    LastPasePduTime = HighResolutionTimer.Milliseconds;
                                    _lastTimes = pdu.Timestamp;
                                }

                                //Log.Information($"Bluetooth Rx: Time:{HighResolutionTimer.Milliseconds - LastPasePduTime}");

                                // Dispatch the received pdu to high level protocol.
                                if (!OnPduReceived(pdu))
                                {
                                    await _rxChannel.Writer.WriteAsync(pdu);
                                }
                                // Log.Information($"Bluetooth Rx: Data:{formated}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Failed to parse the PDU from the UART receive buffer");
                        }
                    }
                });
            }

            return ret;
        }


        public void SetGattCharacteristic(GattCharacteristic gattCharacteristic, GattCharacteristic notifyCharacteristic)
        {
            _writeCharacteristic = gattCharacteristic;
            _notifyCharacteristic = notifyCharacteristic;
        }
        int pduNumber = 0;
        Int64 LastPasePduTime = 0;
        Int64 StartPasePduTime = 0;
        private void OnDataReceived(GattCharacteristic sender, GattValueChangedEventArgs evt)
        {

            byte[] data = evt.CharacteristicValue.ToArray();

            // 周期上报帧（55-AA + 长度 + 动态 DID 低字节 0xE3）不再打印：2A 主动上传时每秒几十帧，会把日志刷爆
            if (!IsPeriodicUploadFrame(data))
            {
                string formated = BitConverter.ToString(data);
                Log.Information($"Bluetooth Rx: Data:{formated}");
            }

            _rxBuffer.AddData(data);



        }

        /// <summary>
        /// 判定是否为“2A 主动上传的周期数据帧”：
        /// 帧头 55-AA（或反序），payload 首字节为动态 DID 低字节 0xE3，且整包正好是这一帧（未夹带其它 PDU）。
        /// </summary>
        private static bool IsPeriodicUploadFrame(byte[] data)
        {
            if (data == null || data.Length < 5)
            {
                return false;
            }

            bool head = (data[0] == 0x55 && data[1] == 0xAA) || (data[0] == 0xAA && data[1] == 0x55);
            if (!head || data[4] != 0xE3)
            {
                return false;
            }

            // 帧结构：2 字节头 + 2 字节长度 + payload + 2 字节校验
            int payloadLengthA = (data[2] << 8) | data[3];
            int payloadLengthB = (data[3] << 8) | data[2];

            return data.Length == payloadLengthA + 6 || data.Length == payloadLengthB + 6;
        }

        /// <summary>
        /// Stop the transmitting task.
        /// </summary>
        /// <returns></returns>
        public override async Task<int> StopAsync()
        {
            int result = 0;
            _cts?.Cancel();

            try
            {
                if (_notifyCharacteristic != null)
                {

                    await _notifyCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
                    _notifyCharacteristic.ValueChanged -= OnDataReceived;
                }

            }
            catch (Exception)
            {

            }
            // Valid the transmit task.
            if (_txTask != null)
            {
                if (_txTask.Status == TaskStatus.WaitingForActivation || _txTask.Status == TaskStatus.Created)
                {
                    _txTask = null;
                }
                else
                {
                    try
                    {
                        result = await _txTask;
                    }
                    catch (OperationCanceledException)
                    {
                        // 任务被取消
                        result = 0; // 可以返回取消标志或特定的错误码
                    }
                    finally
                    {
                        _txTask = null;
                    }
                }
            }
            // Stop the receive parsing task.
            if (_rxParseTask != null)
            {
                try
                {
                    await _rxParseTask;
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    _rxParseTask = null;
                }
            }

            result = await base.StopAsync();


            return result;
        }

        /// <summary>
        /// The method is used to transmit the data into the real seril port.
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        protected override int Transmit(Pdu pdu)
        {
            int result = 0;
            if (_writeCharacteristic == null)
            {
                result = -1;
                return result;
            }
            // Valid the payload.
            if (pdu.Data != null)
            {
                // 2 bytes head, 2 bytes length and 2 bytes crc.
                byte[] buffer = new byte[pdu.Data.Length + 6];

                // Create a writer to serialize
                using (BinaryWriter writer = new BinaryWriter(new MemoryStream(buffer)))
                {
                    // Write the message head
                    writer.Write(Swap16(MSG_HEAD));

                    /* Save the current offset. */
                    int offset = (int)writer.BaseStream.Position;

                    // Write the message payload length.
                    writer.Write(Swap16((UInt16)pdu.Data.Length));

                    // Write the message payload.
                    writer.Write(pdu.Data);
                    writer.Flush();

                    // The checksum start from the field length.
                    UInt16 checksum = CalcChecksum(buffer, offset, (int)(writer.BaseStream.Position - offset));
                    writer.Write(Swap16(checksum));
                    writer.Flush();
                }
                IBuffer ibuffer = buffer.AsBuffer();

                GattCommunicationStatus status = Task.Run(async () =>
                {
                    //string formated = BitConverter.ToString(buffer);
                    //Log.Information($"Bluetooth Tx: Data:{formated}");
                    GattCommunicationStatus state = await _writeCharacteristic.WriteValueAsync(ibuffer, GattWriteOption.WriteWithResponse);
                    if (state == GattCommunicationStatus.Success)
                    {
                        // Log.Debug("发送成功");
                    }
                    return state;
                }).Result;

                result = status == GattCommunicationStatus.Success ? 0 : -1;
            }

            return result;
        }

        /// <summary>
        /// The method is used to transmit the CAN datalink PDU with async mode
        /// </summary>
        /// <param name="pdu">The CAN datalink PDU</param>
        /// <returns></returns>
        public override async Task<int> TransmitAsync(Pdu pdu, CancellationToken token = default)
        {
            int result = 0;

            // Transmit the given pdu
            try
            {
                // Buffered the PDU.
                await _txChannel.Writer.WriteAsync(pdu, token);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to write the PDU to tx buffer");
            }

            // Wait and receive the transmitted PDU
            //try
            //{
            //    Pdu rxPdu = await _fdbkChannel.Reader.ReadAsync(token);
            //    result = 0;
            //}
            //catch (Exception ex)
            //{
            //    Log.Error(ex, "Failed to read PDUs from the feedback channel");
            //}

            return result;
        }

        /// <summary>
        /// The method try to receive a new PDU from UART.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public override async Task<Pdu?> ReceivedAsync(CancellationToken token = default)
        {
            Pdu? pdu = await _rxChannel.Reader.ReadAsync(token);

            return pdu;
        }


        /// <summary>
        /// Swap byte order the given value
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static UInt16 Swap16(UInt16 value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            return BitConverter.ToUInt16(bytes, 0);
        }

        /// <summary>
        /// Swap byte order the given value
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static UInt32 Swap32(UInt32 value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }

        /// <summary>
        /// Calculate the given data checksum.
        /// </summary>
        /// <param name=""></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static UInt16 CalcChecksum(byte[] data, int offset, int length)
        {
            byte aux = 0;

            // Verify the length.
            if (offset + length < data.Length)
            {
                // Acculate the bytes.
                for (int i = offset; i < (offset + length); i++)
                {
                    aux += data[i];
                }
            }

            // Fix the checksum.
            UInt16 checksum = (UInt16)((aux << 8) | (byte)(~aux));

            return checksum;
        }
        public override bool Available()
        {
            return true;
        }
    }
}
