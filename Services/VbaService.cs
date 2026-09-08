using System.Diagnostics;
using System.Runtime.InteropServices;
namespace TestingSoftware.Services
{
    #region ===== 枚举 =====
    public enum VbaStatus : int
    {
        Success = 0,
        QueueIsFull = 1,
        NoLicense = 2,
        InvalidParam = 3,
        InvalidChIndex = 4,
        ChIsOffline = 5,
        NotImplemented = 6,
        CmdTimeout = 7,
        HwNoPresent = 8,
        CannotOpenDriver = 9,
        TrialExpired = 10,
        InvalidAccess = 11,
        ConfigTamperred = 12,
        SdCardNotInserted = 13,
        BadFormat = 14,
        Eof = 15,
        InsufficientSpace = 16,
        Other = 17,
        InvalidDeviceHandle = 18
    }
    public enum VbaChanelMode : byte
    {
        Normal = 0x0,
        ListenOnly = 0x1
    }
    public enum VbaPduTag : byte
    {
        RxMsg = 0,
        TxMsg = 1,
        ChannelState = 3,
        Ign = 4
    }
    #endregion
    #region ===== 结构体（与 vbac.dll 原生布局一致） =====
    /// <summary>CAN 通道配置</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VbaCanChannelConfig
    {
        public byte brp;           // 波特率预分频
        public byte smpl;          // 采样次数 1 或 3
        public byte sjw;           // 同步跳转宽度
        public byte tseg1;
        public byte tseg2;
        public byte use_tr;        // 是否使用终端电阻
        public byte mode;          // VbaChanelMode
        public byte recovery;      // 发送模式
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public uint[] filters;       // 验收码滤波
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct VbaRecordConfig
    {
        public byte format;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public byte[] reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct VbaReplayerConfig
    {
        public byte format;
        public byte loop;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] reserved;
    }
    /// <summary>通道完整配置（VbacOpenChlEx 用）</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VbaChlConfig
    {
        public uint feature;
        public VbaCanChannelConfig canChlConfig;
        public VbaRecordConfig recordConfig;
        public VbaReplayerConfig replayerConfig;
    }

    /// <summary>
    /// VBA 协议数据单元 总大小 28 字节
    /// tag(1) + channel(1) + reserved(2) + timestamp(8) =12；offset12开始CAN消息16字节
    /// Explicit结构体禁止直接使用byte[]托管数组，改用unsafe fixed内嵌数组
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 28)]
    public struct VbaPdu
    {
        public byte tag;
        public byte channel;
        public ushort reserved;
        public ulong timestamp;

        public uint msg_id;
        public ushort msg_flags;
        public ushort msg_dlc;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] msg_data;
    }

    /// <summary>接收到的 CAN 帧（托管类型，方便上层使用）</summary>
    public class CanFrame
    {
        public uint Id { get; set; }
        public byte Channel { get; set; }
        public byte Dir { get; set; }     // 0=接收, 1=发送
        public byte DLC { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public ushort Flags { get; set; }
        public ulong Timestamp { get; set; }     // 10us 分辨率
    }
    #endregion
    #region ===== VbaService =====
    /// <summary>
    /// VBA CAN 设备服务封装
    /// 三个核心函数：OpenDevice / Receive / CloseDevice
    /// </summary>
    public class VbaService : IDisposable
    {
        #region --- P/Invoke (vbac.dll) ---
        // 设备级
        [DllImport("vbac.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern VbaStatus VbacOpen(uint index, ref uint device);
        [DllImport("vbac.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern VbaStatus VbacClose(uint device);
        // 通道级
        [DllImport("vbac.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern VbaStatus VbacOpenChlEx(uint device, uint channel, ref VbaChlConfig config);
        [DllImport("vbac.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern VbaStatus VbacClearChl(uint device, uint channel);
        [DllImport("vbac.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern VbaStatus VbacCloseChl(uint device, uint channel);
        [DllImport("vbac.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern VbaStatus VbacReceive(uint device, uint channel, ref VbaPdu pdu, uint timeout);
        [DllImport("vbac.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern VbaStatus VbacTransmit(uint device, uint channel, ref VbaPdu pdu, uint timeout);
        #endregion
        #region --- 属性 ---
        public uint DeviceHandle { get; private set; }
        public int Channel { get; private set; }
        public bool IsOpen { get; private set; }
        #endregion

        public int OpenDevice(int channel = 0, int baudRate = 500000, uint deviceIndex = 0)
        {
            if (IsOpen) return 0;
            uint handle = 0;
            VbaStatus status = VbacOpen(deviceIndex, ref handle);
            if (status != VbaStatus.Success)
                return (int)status;
            DeviceHandle = handle;
            Channel = channel;

            byte brp = baudRate switch
            {
                500000 => 4,
                250000 => 8,
                125000 => 16,
                100000 => 20,
                50000 => 40,
                _ => 4
            };

            VbaChlConfig cfg = new VbaChlConfig
            {
                feature = 64,
                canChlConfig = new VbaCanChannelConfig
                {
                    brp = brp,
                    smpl = 1,
                    sjw = 1,
                    tseg1 = 9,
                    tseg2 = 2,
                    mode = (byte)VbaChanelMode.Normal,
                    recovery = 1,
                    filters = new uint[20]
                },
                recordConfig = new VbaRecordConfig (),
                replayerConfig = new VbaReplayerConfig()
            };

            status = VbacOpenChlEx(DeviceHandle, (uint)Channel, ref cfg);
            if (status != VbaStatus.Success)
            {
                VbacClose(DeviceHandle);
                DeviceHandle = 0;
                return (int)status;
            }
            IsOpen = true;
            return 0;
        }

        public CanFrame? Receive(int timeoutMs = 1000)
        {
            if (!IsOpen) return null;
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                VbaPdu pdu = default;
                VbaStatus status = VbacReceive(DeviceHandle, (uint)Channel, ref pdu, 100);
                if (status == VbaStatus.Success)
                {
                    if (pdu.tag == (byte)VbaPduTag.RxMsg || pdu.tag == (byte)VbaPduTag.TxMsg)
                    {
                        if (pdu.msg_flags == 0x00 || pdu.msg_flags == 0x04)
                        {
                            byte dlc = (byte)pdu.msg_dlc;
                            byte[] data = new byte[dlc];
                            // fixed数组读取必须unsafe
                                for (int i = 0; i < dlc; i++)
                                {
                                    data[i] = pdu.msg_data[i];
                                }
                            return new CanFrame
                            {
                                Id = pdu.msg_id,
                                Channel = pdu.channel,
                                Dir = pdu.tag,
                                DLC = dlc,
                                Data = data,
                                Flags = pdu.msg_flags,
                                Timestamp = pdu.timestamp
                            };
                        }
                    }
                }
            }
            return null;
        }

        public CanFrame? ReceiveRemote(int timeoutMs = 1000)
        {
            if (!IsOpen) return null;
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                VbaPdu pdu = default;
                VbaStatus status = VbacReceive(DeviceHandle, (uint)Channel, ref pdu, 100);
                if (status == VbaStatus.Success)
                {
                    if ((pdu.tag == (byte)VbaPduTag.RxMsg || pdu.tag == (byte)VbaPduTag.TxMsg) )
                    {
                        return new CanFrame
                        {
                            Id = pdu.msg_id,
                            Channel = pdu.channel,
                            Dir = pdu.tag,
                            DLC = (byte)pdu.msg_dlc,
                            Data = Array.Empty<byte>(),
                            Flags = pdu.msg_flags,
                            Timestamp = pdu.timestamp
                        };
                    }
                }
            }
            return null;
        }

        public int Send(uint id, byte[] data, bool extended = false)
        {
            if (!IsOpen) return -1;
            VbaPdu pdu = default;
            pdu.tag = (byte)VbaPduTag.TxMsg;
            pdu.channel = (byte)Channel;
            pdu.msg_id = id;
            pdu.msg_flags = extended ? (ushort)0x04 : (ushort)0x00;
            pdu.msg_dlc = (ushort)Math.Min(data.Length, 8);

                int copyLen = Math.Min(data.Length, 8);
                for (int i = 0; i < copyLen; i++)
                {
                    pdu.msg_data[i] = data[i];
                }

            VbaStatus status = VbacTransmit(DeviceHandle, (uint)Channel, ref pdu, 100);
            return status == VbaStatus.Success ? 0 : (int)status;
        }


        public void CloseDevice()
        {
            if (!IsOpen) return;
            VbacClearChl(DeviceHandle, (uint)Channel);
            VbacCloseChl(DeviceHandle, (uint)Channel);
            VbacClose(DeviceHandle);
            IsOpen = false;
            DeviceHandle = 0;
        }

        public void Dispose() => CloseDevice();
    }
    #endregion
}
