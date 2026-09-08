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

            _notifyCharacteristic.ValueChanged += OnDataReceived;
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

            return ret;
        }


        public void SetGattCharacteristic(GattCharacteristic gattCharacteristic, GattCharacteristic notifyCharacteristic)
        {
            _writeCharacteristic = gattCharacteristic;
            _notifyCharacteristic = notifyCharacteristic;
        }
        int pduNumber = 0;
        private void OnDataReceived(GattCharacteristic sender, GattValueChangedEventArgs evt)
        {
            byte[] data = evt.CharacteristicValue.ToArray();
            //string formated = BitConverter.ToString(data);
            //Log.Information($"Bluetooth Rx: Data:{formated}");
            _rxBuffer.AddData(data);

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
                    //string formated1 = BitConverter.ToString(pdu.Data);
                    //Log.Information($"PDU.DATA:{formated1}");
                    pdu.Timestamp += pduNumber * 10; // Add the time offset for each pdu.

                    pduNumber++;


                    // Dispatch the received pdu to high level protocol.
                    if (!OnPduReceived(pdu))
                    {
                        _rxChannel.Writer.WriteAsync(pdu);
                    }
                    // Log.Information($"Bluetooth Rx: Data:{formated}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse the PDU from the UART receive buffer");
            }

        }
        ///// <summary>
        ///// 尝试从给定的缓冲区中解析出一个完整的 PDU
        ///// </summary>
        ///// <param name="buffer">接收缓冲区数组</param>
        ///// <param name="length">当前缓冲区中有效数据的长度</param>
        ///// <returns>解析成功返回 Pdu 对象，否则返回 null</returns>
        //private Pdu? ParsePdu(byte[] buffer, int length)
        //{
        //    // 最短合法 PDU 长度：帧头2 + 长度2 + 校验和2 = 6字节
        //    if (length < 6) return null;

        //    int offset = 0;

        //    while (offset + 1 < length)
        //    {
        //        // 第一步：寻找帧头 0x55 0xAA
        //        while (offset + 1 < length)
        //        {
        //            if (buffer[offset] == 0x55 && buffer[offset + 1] == 0xAA)
        //            {
        //                break; // 找到帧头
        //            }
        //            offset++;
        //        }

        //        // 剩余数据里找不到任何帧头
        //        if (offset + 1 >= length)
        //        {
        //            // 丢弃前面所有已扫描的无用数据
        //            if (offset > 0)
        //            {
        //                Array.Copy(buffer, offset, buffer, 0, length - offset);
        //                _rxBufferLength = length - offset;
        //            }
        //            return null;
        //        }

        //        // 记录帧头起始位置
        //        int headerOffset = offset;
        //        offset += 2; // 跳过帧头 0x55 0xAA

        //        // 第二步：判断长度字段是否已经收到完整（2字节）
        //        if (offset + 2 > length)
        //        {
        //            // 长度字段不完整 → 保留从本次帧头开始的数据
        //            if (headerOffset > 0)
        //            {
        //                Array.Copy(buffer, headerOffset, buffer, 0, length - headerOffset);
        //                _rxBufferLength = length - headerOffset;
        //            }
        //            return null;
        //        }

        //        // 第三步：读取有效载荷长度（大端序）
        //        int payloadLength = (buffer[offset] << 8) | buffer[offset + 1];
        //        int pduTotalLength = payloadLength + 6; // 总长度 = 头2 + 长度2 + 载荷 + 校验2

        //        // 第四步：判断整个 PDU 是否已经全部到达
        //        if (headerOffset + pduTotalLength > length)
        //        {
        //            // 数据还不完整 → 保留从本次帧头开始的部分
        //            if (headerOffset > 0)
        //            {
        //                Array.Copy(buffer, headerOffset, buffer, 0, length - headerOffset);
        //                _rxBufferLength = length - headerOffset;
        //            }
        //            return null;
        //        }

        //        // 第五步：校验和验证
        //        byte[] payload = new byte[payloadLength];
        //        Array.Copy(buffer, headerOffset + 4, payload, 0, payloadLength);

        //        // 接收到的校验和（大端序）
        //        UInt16 receivedChecksum = (UInt16)(
        //            (buffer[headerOffset + 4 + payloadLength] << 8) |
        //            buffer[headerOffset + 5 + payloadLength]
        //        );

        //        // 计算校验和（从长度字段开始，到载荷结束，共 payloadLength + 2 字节）
        //        UInt16 calculatedChecksum = CalcChecksum(buffer, headerOffset + 2, payloadLength + 2);

        //        if (receivedChecksum != calculatedChecksum)
        //        {
        //            // 校验失败 → 认为这个 0x55AA 不是有效的帧头，跳过它继续找下一个
        //            offset = headerOffset + 2;
        //            continue;
        //        }

        //        // 第六步：构造有效的 PDU 对象
        //        Pdu pdu = new Pdu
        //        {
        //            Data = payload,
        //            Timestamp = HighResolutionTimer.Milliseconds - _startTime
        //        };

        //        // 第七步：移除已解析完成的 PDU 数据，滑动窗口前进
        //        int remaining = length - (headerOffset + pduTotalLength);
        //        if (remaining > 0)
        //        {
        //            Array.Copy(buffer, headerOffset + pduTotalLength, buffer, 0, remaining);
        //        }
        //        _rxBufferLength = remaining;

        //        return pdu;
        //    }

        //    // 整个缓冲区扫描完毕仍未找到有效 PDU
        //    // （通常意味着全是垃圾数据或只有不完整的残片）
        //    if (length > 0)
        //    {
        //        _rxBufferLength = 0; // 清空缓冲区
        //    }

        //    return null;
        //}
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
