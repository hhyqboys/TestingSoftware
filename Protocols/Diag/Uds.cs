
using Serilog;
using TestingSoftware.Protocols.Bluetoolth;
using System.Threading.Channels;
using TestingSoftware.Utilities;

namespace TestingSoftware.Protocols.Diag
{
    /// <summary>
    /// The class define the unified diagnostic service application layer protocol
    /// </summary>
    public class Uds : Protocol
    {
        /// <summary>
        /// The parameter name for P2
        /// </summary>
        public const string UDS_P2_MAX = "p2_max";

        /// <summary>
        /// The parameter name for P2*
        /// </summary>
        public const string UDS_P2_STAR = "p2_star";

        /// <summary>
        /// TODO:?
        /// </summary>
        public const string UDS_WAIT_TIMEOUT = "wait_timeout";

        /// <summary>
        /// TODO:?
        /// </summary>
        public const string UDS_FUNCTION_TIMEOUT = "func_timeout";

        /// <summary>
        /// The channel is used to cache all received periodic Pdus.
        /// 用 DropOldest：2A 快速率上报时每秒几十帧，消费端跟不上就丢最旧的，绝不反压到蓝牙解析线程。
        /// </summary>
        private Channel<Pdu> _periodicPdus = Channel.CreateBounded<Pdu>(new BoundedChannelOptions(200)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        /// <summary>
        /// Lower level machine reports message cache
        /// </summary>
        private Channel<Pdu> _readPdus = Channel.CreateBounded<Pdu>(200);
        /// <summary>
        /// Read the message reported by the lower computer
        /// </summary>
        private CancellationTokenSource? Cts = null;
        private static Task? _receiveTask;
        public static bool isPause = false;
        public static CancellationTokenSource? Datacts;
        /// <summary>
        /// The periodical data identifier list for service 2A
        /// </summary>
        public byte Pid { get; set; }

        /// <summary>
        /// 最近一次收到任意 PDU（应答或周期帧）的时刻，用于链路假死看门狗。
        /// </summary>
        public long LastRxMs { get; private set; } = -1;

        /// <summary>
        /// 最近一次下发请求的时刻，用于判断“发出去了但一直没回”。
        /// </summary>
        public long LastRequestMs { get; private set; } = -1;

        /// <summary>
        /// 最近一次正常（非3E）UDS请求的发送时刻（HighResolutionTimer.Milliseconds）。
        /// 用于 3E80 保活发送前检查与正常数据之间 1.5s 的静默间隔。
        /// </summary>
        public long LastNormalRequestMs { get; private set; } = -1;
        /// <summary>
        /// Is supported positive suppressed or not.
        /// </summary>
        private bool is_positive_suppressed { get; set; }

        /// <summary>
        /// Create a new UDS protocol with default parameter setting.
        /// </summary>
        public Uds() : base(Layer.Application, "ISO14229")
        {
            SetParameter<int>(UDS_P2_MAX, 1500, true);
            SetParameter<int>(UDS_P2_STAR, 5000, true);
            SetParameter<int>(UDS_WAIT_TIMEOUT, 5000, true);
            SetParameter<int>(UDS_FUNCTION_TIMEOUT, 155, true);

            Pid = 0xFF;
        }

        /// <summary>
        /// The method is used to create a new protocol stack with the given datalink.
        /// The UDS will used pre-defined program to initialize the protocol stack.
        /// </summary>
        /// <param name="datalink">The sub-class of the datalink. And it is the lowest protocol of the stack.</param>
        /// <returns>The top protocol of the stack</returns>
        public bool BuildStackFromDatalink(Datalink datalink, int? mode = -1)
        {
            // Remove the old one
            if (LowLevelProtocol != null)
            {
                // Remove the high level protocol
                LowLevelProtocol.RemoveHighLevelProtocol(this);
            }

            if (datalink is BluetoothDataLink)
            {
                datalink.AddHighLevelProtocol(this);
            }
            return true;
        }

        /// <summary>
        /// Valid the Session control request format
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate10Request(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x10)
                {
                    ret = 0;
                    if ((pdu.Data[1] & 0x80) == 0x80)
                    {
                        is_positive_suppressed = true;
                    }
                }
            }

            return ret;
        }

        /// <summary>
        /// Valid the Session control request format
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate11Request(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x11)
                {
                    ret = 0;
                    if ((pdu.Data[1] & 0x80) == 0x80)
                    {
                        is_positive_suppressed = true;
                    }
                }
            }

            return ret;
        }

        /// <summary>
        /// Valid the Session control response format
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate10Response(Pdu pdu)
        {
            int ret = -1;
            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x50)
                {
                    if (pdu.Data.Length == 6)
                    {
                        ret = 0;
                    }
                }
                else if ((pdu.Data[0] == 0x7f) && (pdu.Data[1] == 0x10))
                {
                    ret = 0;
                }
            }
            return ret;
        }

        public int Validate11Response(Pdu pdu)
        {
            int ret = -1;
            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x51)
                {
                    ret = 0;
                }
                else if ((pdu.Data[0] == 0x7f) && (pdu.Data[1] == 0x10))
                {
                    ret = 0;
                }
            }
            return ret;
        }

        /// <summary>
        /// Valid read data by identifier request format
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate22Request(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x22)
                {
                    ret = 0;
                }
            }

            return ret;
        }

        /// <summary>
        /// Valid read data by identifier response format
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate22Response(Pdu pdu)
        {
            int ret = -1;
            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x62)
                {
                    if (pdu.Data.Length >= 4)
                    {
                        ret = 0;
                    }
                }
                else if ((pdu.Data[0] == 0x7f) && (pdu.Data[1] == 0x22))
                {
                    ret = 0;
                }
            }
            return ret;
        }

        /// <summary>
        /// Valid clear dtc request format
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate14Request(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x14)
                {
                    ret = 0;
                }
            }

            return ret;
        }

        /// <summary>
        /// Valid clear dtc response format
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate14Response(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x54)
                {
                    if (pdu.Data.Length >= 1)
                    {
                        ret = 0;
                    }
                }
                else if ((pdu.Data[0] == 0x7f) && (pdu.Data[1] == 0x14) && (pdu.Data[2] != 0x78))
                {
                    ret = 0;
                }
            }
            return ret;
        }

        /// <summary>
        /// Valid write data request format
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate2ERequest(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x2E)
                {
                    ret = 0;
                }
            }

            return ret;
        }

        /// <summary>
        /// Valid write data response format
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate2EResponse(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x6E)
                {
                    if (pdu.Data.Length >= 3)
                    {
                        ret = 0;
                    }
                }
                else if ((pdu.Data[0] == 0x7f) && (pdu.Data[1] == 0x2E) && (pdu.Data[2] != 0x78))
                {
                    ret = 0;
                }
            }
            return ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate01Request(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x01)
                {
                    ret = 0;
                }
            }

            return ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate03Request(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x03)
                {
                    ret = 0;
                }
            }

            return ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate04Request(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x04)
                {
                    ret = 0;
                }
            }

            return ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate01Response(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x41)
                {
                    if (pdu.Data[1] == 0x00)
                    {
                        if (pdu.Data.Length == 6)
                        {
                            ret = 0;
                        }
                    }
                    else
                    {
                        if (pdu.Data.Length >= 3)
                        {
                            ret = 0;
                        }
                    }
                }
                else if ((pdu.Data[0] == 0x7f) && (pdu.Data[1] == 0x01))
                {
                    ret = 0;
                }
            }
            return ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate03Response(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x43)
                {
                    ret = 0;
                }
                else if ((pdu.Data[0] == 0x7f) && (pdu.Data[1] == 0x03))
                {
                    ret = 0;
                }
            }
            return ret;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate04Response(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x44)
                {
                    ret = 0;
                }
                else if ((pdu.Data[0] == 0x7f) && (pdu.Data[1] == 0x04))
                {
                    ret = 0;
                }
            }
            return ret;
        }

        /// <summary>
        /// Valid the reset session request format
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate3ERequest(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x3E)
                {
                    ret = 0;
                }
                if ((pdu.Data[1] == 0x80))
                {
                    is_positive_suppressed = true;
                }
            }

            return ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate3EResponse(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x7E)
                {
                    if (pdu.Data.Length >= 4)
                    {
                        ret = 0;
                    }
                }
                else if ((pdu.Data[0] == 0x7f) && (pdu.Data[1] == 0x3E))
                {
                    ret = 0;
                }
            }
            return ret;
        }

        /// <summary>
        /// Valid the reset session request format
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate87Request(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x87)
                {
                    ret = 0;

                    if ((pdu.Data[1] & 0x80) == 0x80)
                    {
                        is_positive_suppressed = true;
                    }
                }
            }

            return ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate87Response(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x7E)
                {
                    if (pdu.Data.Length >= 4)
                    {
                        ret = 0;
                    }
                }
                else if ((pdu.Data[0] == 0x7f) && (pdu.Data[1] == 0x3E))
                {
                    ret = 0;
                }
            }
            return ret;
        }

        /// <summary>
        /// Valid the request seed or compary key request format
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate27Request(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x27)
                {
                    ret = 0;
                }
            }

            return ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate27Response(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x67)
                {
                    if (pdu.Data.Length >= 2)
                    {
                        ret = 0;
                    }
                }
                else if ((pdu.Data[0] == 0x7f) && (pdu.Data[1] == 0x27))
                {
                    ret = 0;
                }
            }
            return ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate2aRequest(Pdu pdu)
        {
            return 0;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate2aResponse(Pdu pdu)
        {
            return 0;
        }

        public int Validate2cRequest(Pdu pdu)
        {
            return 0;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate2cResponse(Pdu pdu)
        {
            return 0;
        }

        /// <summary>
        /// Valid the request seed or compary key request format
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate2FRequest(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x2F)
                {
                    ret = 0;
                }
            }

            return ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate2FResponse(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x6F)
                {
                    if (pdu.Data.Length >= 4)
                    {
                        ret = 0;
                    }
                }
                else if ((pdu.Data[0] == 0x7f) && (pdu.Data[1] == 0x2F))
                {
                    ret = 0;
                }
            }
            return ret;
        }

        /// <summary>
        /// Valid the request seed or compary key request format
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate19Request(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x19)
                {
                    ret = 0;
                }
            }

            return ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate19Response(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x59)
                {
                    if (pdu.Data.Length >= 6)
                    {
                        ret = 0;
                    }
                }
                else if ((pdu.Data[0] == 0x7f) && (pdu.Data[1] == 0x19))
                {
                    ret = 0;
                }
            }
            return ret;
        }


        /// <summary>
        /// Valid the request seed or compary key request format
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate31Request(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x31)
                {
                    ret = 0;
                }
            }

            return ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate31Response(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x71)
                {
                    ret = 0;
                }
                else if ((pdu.Data[0] == 0x7f) && (pdu.Data[1] == 031))
                {
                    ret = 0;
                }
            }
            return ret;
        }

        /// <summary>
        /// Valid the request download
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate34Request(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x34)
                {
                    ret = 0;
                }
            }

            return ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate34Response(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x74)
                {
                    ret = 0;
                }
                else if ((pdu.Data[0] == 0x7f) && (pdu.Data[1] == 0x34))
                {
                    ret = 0;
                }
            }
            return ret;
        }

        /// <summary>
        /// Valid the request download
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate36Request(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x36)
                {
                    ret = 0;
                }
            }

            return ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate36Response(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x76)
                {
                    ret = 0;
                }
                else if ((pdu.Data[0] == 0x7f) && (pdu.Data[1] == 0x36))
                {
                    ret = 0;
                }
            }
            return ret;
        }

        /// <summary>
        /// Valid the request download
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate37Request(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x37)
                {
                    ret = 0;
                }
            }

            return ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate37Response(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x77)
                {
                    ret = 0;
                }
                else if ((pdu.Data[0] == 0x7f) && (pdu.Data[1] == 0x37))
                {
                    ret = 0;
                }
            }
            return ret;
        }

        /// <summary>
        /// Valid the request seed or compary key request format
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate09Request(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x09)
                {
                    ret = 0;
                }
            }

            return ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int Validate09Response(Pdu pdu)
        {
            int ret = -1;

            if ((pdu != null) && (pdu.Data != null))
            {
                if (pdu.Data[0] == 0x49)
                {
                    ret = 0;
                }
                else if ((pdu.Data[0] == 0x7f) && (pdu.Data[1] == 0x09))
                {
                    ret = 0;
                }
            }
            return ret;
        }

        /// <summary>
        /// Validate the given UDS PDU is validate or not.
        /// </summary>
        /// <param name="pdu">The diagnostic request</param>
        /// <returns>0 if the given request is valid</returns>
        public int ValidateRequest(Pdu pdu)
        {
            int result = 0;

            // Check the pdu is valid or not.
            if (pdu != null)
            {
                // Check the pdu data is valid or not
                if (pdu.Data != null)
                {
                    // Check the pdu data length is valid or not.
                    if (pdu.Data.Length > 0)
                    {
                        // Call the service request valid method to do more validation.
                        switch (pdu.Data[0])
                        {
                            case 0x01:
                                result = Validate01Request(pdu);
                                break;

                            case 0x03:
                                result = Validate03Request(pdu);
                                break;

                            case 0x04:
                                result = Validate04Request(pdu);
                                break;

                            case 0x09:
                                result = Validate09Request(pdu);
                                break;

                            case 0x10:
                                result = Validate10Request(pdu);
                                break;

                            case 0x11:
                                result = Validate11Request(pdu);
                                break;

                            case 0x14:
                                result = Validate14Request(pdu);
                                break;

                            case 0x19:
                                result = Validate19Request(pdu);
                                break;

                            case 0x22:
                                result = Validate22Request(pdu);
                                break;

                            case 0x27:
                                result = Validate27Request(pdu);
                                break;

                            case 0x2A:
                                result = Validate2aRequest(pdu);
                                break;

                            case 0x2C:
                                result = Validate2cRequest(pdu);
                                break;

                            case 0x2E:
                                result = Validate2ERequest(pdu);
                                break;

                            case 0x2F:
                                result = Validate2FRequest(pdu);
                                break;

                            case 0x31:
                                result = Validate31Request(pdu);
                                break;

                            case 0x34:
                                result = Validate34Request(pdu);
                                break;

                            case 0x36:
                                result = Validate36Request(pdu);
                                break;

                            case 0x37:
                                result = Validate37Request(pdu);
                                break;

                            case 0x3E:
                                result = Validate3ERequest(pdu);
                                break;

                            case 0x87:
                                result = Validate87Request(pdu);
                                break;

                            default:
                                result = -4; // not supported service.
                                break;
                        }
                    }
                    else
                    {
                        result = -3; // bad format
                    }
                }
                else
                {
                    result = -2;// data field null
                }
            }
            else
            {
                result = -1;// Null
            }
            return result;
        }

        /// <summary>
        /// Validate the given UDS PDU is validate response or not.
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public int ValidateResponse(Pdu? pdu)
        {
            int result = 0;

            // Check the pdu is valid or not.
            if (pdu != null)
            {
                // Check the pdu data is valid or not
                if (pdu.Data != null)
                {
                    // Check the pdu data length is valid or not.
                    if (pdu.Data.Length > 0)
                    {
                        // Call the service request valid method to do more validation.
                        switch (pdu.Data[0])
                        {
                            case 0x41:
                                result = Validate01Response(pdu);
                                break;

                            case 0x43:
                                result = Validate03Response(pdu);
                                break;

                            case 0x44:
                                result = Validate04Response(pdu);
                                break;

                            case 0x49:
                                result = Validate09Response(pdu);
                                break;

                            case 0x50:
                                result = Validate10Response(pdu);
                                break;

                            case 0x51:
                                result = Validate11Response(pdu);
                                break;

                            case 0x54:
                                result = Validate14Response(pdu);
                                break;

                            case 0x59:
                                result = Validate19Response(pdu);
                                break;

                            case 0x62:
                                result = Validate22Response(pdu);
                                break;

                            case 0x67:
                                result = Validate27Response(pdu);
                                break;

                            case 0x6A:
                            case 0x6C:
                                result = 0;
                                break;

                            case 0x6E:
                                result = Validate2EResponse(pdu);
                                break;

                            case 0x6F:
                                result = Validate2FResponse(pdu);
                                break;

                            case 0x71:
                                result = Validate31Response(pdu);
                                break;

                            case 0x74:
                                result = Validate34Response(pdu);
                                break;

                            case 0x7E:
                                result = Validate3EResponse(pdu);
                                break;

                            case 0x7F:
                                // 否定响应（7F SID NRC）属合法应答，放行交由上层按服务 ID + NRC 解析
                                result = 0;
                                break;

                            default:
                                // 未知 SID：视为错位/非本服务帧，拒绝放行，避免被当成当前请求应答
                                result = -3;
                                break;
                        }
                    }
                    else
                    {
                        result = -3; // bad format
                    }
                }
                else
                {
                    result = -2;// data field null
                }
            }
            else
            {
                result = -1;// Null
            }
            return result;
        }

        /// <summary>
        /// Determines the given PDU is a negative pending PDU or not.
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public bool IsPendingResponse(Pdu pdu)
        {
            bool result = false;
            if (pdu != null && pdu.Data != null)
            {
                if ((pdu.Data[0] == 0x7f) && (pdu.Data[2] == 0x78))
                {
                    result = true;
                }
            }
            return result;
        }

        /// <summary>
        /// The given Pdu must be UdsPdu.
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public override bool IsPduAccepted(Pdu pdu)
        {
            return pdu is UdsPdu ? true : false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public override async Task<int> TransmitAsync(Pdu pdu, CancellationToken token = default)
        {
            int result = 0;

            if (LowLevelProtocol != null)
            {
                result = await LowLevelProtocol.TransmitAsync(pdu, token);
                if (result != 0)
                {
                    Log.Error("UDS: Failed to transmit request");
                }
            }
            else
            {
                Log.Error("UDS: The low level protocol has not been registered");
                result = -1;
            }

            return result;
        }

        /// <summary>
        /// Try to receive 
        /// </summary>
        /// <param name="timeout"></param>
        /// <returns></returns>
        public override async Task<Pdu?> ReceivedAsync(CancellationToken token = default)
        {


            //return pdu;
            Pdu? pdu = null;

            try
            {
                pdu = await _readPdus.Reader.ReadAsync(token);
                if (pdu != null)
                {
                    LastRxMs = HighResolutionTimer.Milliseconds;
                }
            }
            catch (Exception)
            {
                //Log.Error("pasg uds.cs line:1221 " + ex.Message);
            }

            return pdu;
        }

        /// <summary>排空指定通道内残留的 PDU。</summary>
        private static void DrainChannel(Channel<Pdu> channel)
        {
            while (channel.Reader.TryRead(out _)) { }
        }

        /// <summary>
        /// 清空应答 / 周期两个接收通道，并清掉低层重组缓冲。重连、重试、重新开启监控前调用，
        /// 避免上一轮残留的 burst 周期帧污染下一轮请求应答匹配。
        /// </summary>
        public void ClearBuffers()
        {
            DrainChannel(_readPdus);
            DrainChannel(_periodicPdus);
            if (LowLevelProtocol is BluetoothDataLink link)
            {
                link.ClearRxBuffer();
            }
        }

        /// <summary>
        /// 下发 2A（开启 / 停止主动上传）。本版固件对 2A 开启通常不回 0x6A 正响应，
        /// 下发后直接进入周期上报，因此这里只把 7F 2A 否定响应判为失败；
        /// 等待超时（固件不回 ACK）按指令已成功下发处理。
        /// </summary>
        /// <returns>Result：0=成功（收到 6A 或超时无否定响应）；1=收到否定响应；-1=发送异常。Response 为收到的帧。</returns>
        public async Task<(int Result, Pdu? Response)> Request2aAsync(Pdu pdu, CancellationToken token = default)
        {
            // 无论是否传入外部 token，都必须叠加一个 500ms 超时：
            // 否则断链重启时传入的会话 token 会让等待永不结束（固件本来就不回 ACK）。
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(500);
            CancellationToken ct = timeoutCts.Token;

            DrainChannel(_readPdus);

            int validate = ValidateRequest(pdu);
            if (validate != 0)
            {
                return (-1, null);
            }

            try
            {
                await TransmitAsync(pdu, ct);
                LastRequestMs = HighResolutionTimer.Milliseconds;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to transmit the 2A request");
                return (-1, null);
            }

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    Pdu? p = await ReceivedAsync(ct);
                    if (p == null) break; // 等待超时：固件不回 ACK，按成功处理
                    if (p.Data == null || p.Data.Length == 0) continue;

                    // 否定响应 7F 2A NRC
                    if (p.Data[0] == 0x7F && p.Data.Length >= 3 && p.Data[1] == 0x2A)
                    {
                        return (1, p);
                    }
                    // 正响应 6A
                    if (p.Data[0] == 0x6A)
                    {
                        return (0, p);
                    }

                    // 其余错位帧 / 心跳帧 / 残留周期帧：丢弃，继续等待
                }
            }
            catch
            {
                // 超时按成功处理
            }

            // 外部 token（会话/任务）被取消时如实上报，避免断链重启误判为成功
            if (token.IsCancellationRequested)
            {
                return (-1, null);
            }

            return (0, null);
        }

        public override async Task<int> StartAsync()
        {
            int result = 0;
            result = await base.StartAsync();

            if (result == 0 && _receiveTask == null)
            {
                Log.Debug("UDS: receive task start");
                _receiveTask = Task.Run(async () =>
                {
                    Cts = new CancellationTokenSource();
                    while (!Cts.Token.IsCancellationRequested)
                    {
                        Pdu? pdu = null;
                        if (isPause)
                        {
                            // 避免暂停时 continue 变成死循环空转打满 CPU
                            await Task.Delay(20, Cts.Token);
                            continue;
                        }
                        try
                        {
                            if (LowLevelProtocol != null)
                            {
                                Datacts = new CancellationTokenSource(50);
                                pdu = await LowLevelProtocol.ReceivedAsync(Datacts.Token);
                                if (pdu?.Data != null)
                                {
                                    lock (this)
                                    {
                                        _readPdus.Writer.WriteAsync(pdu);
                                    }

                                }
                            }
                        }
                        catch
                        {
                            // Log.Error("UDS: Failed to receive");
                        }

                    }
                    Log.Error("receiveTask stop");
                });
            }
            else
            {
                Log.Error("dus：result ！= 0 " + result);
            }
            return result;

        }

        public override async Task<int> StopAsync()
        {
            int result = 0;
            result = await base.StopAsync();

            Cts?.Cancel();
            if (_receiveTask != null) await _receiveTask;
            _receiveTask = null;

            return result;
        }
        /// <summary>
        /// The method is used to send a request with phsycial address and wait the its response.
        /// </summary>
        /// <param name="pdu"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public override async Task<Pdu?> Request(Pdu pdu, CancellationToken token = default)
        {
            // Define the return value.
            Pdu? response = null;
            if (token == default)
            {
                var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
                token = cts.Token;
            }
            try
            {
                if (pdu.Data != null && !Service3e.IsUdsTesterPresent(pdu.Data))
                {

                    while (_readPdus.Reader.Count > 0)
                    {
                        var readtoken = new CancellationTokenSource(1);
                        await _readPdus.Reader.ReadAsync(readtoken.Token);
                    }
                }

            }
            catch
            {

            }
            // Validate the requst first.
            int result = ValidateRequest(pdu);

            // Valid request?
            if (result == 0)
            {
                if (pdu.Data != null)
                {
                    string formated = BitConverter.ToString(pdu.Data);
                    Log.Debug("Request: {0}", formated);
                }

                try
                {
                    // Transmit the request.
                    result = await TransmitAsync(pdu, token);
                    LastRequestMs = HighResolutionTimer.Milliseconds;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to transmit the UDS request");

                    return null;
                }

                // 记录最近一次正常（非3E）请求的发送时刻，供 3E80 保活避开该时间窗
                if (result == 0 && pdu.Data != null && !Service3e.IsUdsTesterPresent(pdu.Data))
                {
                    LastNormalRequestMs = HighResolutionTimer.Milliseconds;
                }

                /* Check the request is positive suppressed or not. */
                if (is_positive_suppressed)
                {
                    is_positive_suppressed = false;
                    return null;
                }

                // Initialize the pending flag here.
                bool isPending = false;

                // Save the P2 and P2* here
                int p2 = GetParameter<int>(UDS_P2_MAX) > 0 ? GetParameter<int>(UDS_P2_MAX) : 300;
                int p2star = GetParameter<int>(UDS_P2_STAR) > 0 ? GetParameter<int>(UDS_P2_STAR) : 5000;

                // 按 SID 匹配应答：固件存在"上一轮应答迟到"（实测 2A 的 6A 会晚到 2s 左右），
                // 迟到帧若被当前请求吃掉，后面 2C/22/27 会整串错位（日志里的 -1、2C 定义失败、解锁失败）。
                byte reqSid = pdu.Data![0];
                byte expectedSid = (byte)(reqSid + 0x40);
                long deadline = HighResolutionTimer.Milliseconds + p2star;

                // Wait the response.
                while (true)
                {
                    if (HighResolutionTimer.Milliseconds >= deadline)
                    {
                        response = null;
                        Log.Information("Uds received async is null.");
                        break;
                    }

                    // Create a new cancellation token source for timeout check.
                    using (CancellationTokenSource cts = new CancellationTokenSource(isPending ? p2star : p2))
                    {
                        // Waiting for the service response.
                        response = await ReceivedAsync(cts.Token);
                    }

                    if (response == null)
                    {
                        Log.Information("Uds received async is null.");
                        break;//timeout.
                    }

                    if (response.Data != null && response.Data.Length > 0)
                    {
                        byte sid = response.Data[0];
                        bool positive = sid == expectedSid;
                        bool negative = sid == 0x7F && response.Data.Length > 1 && response.Data[1] == reqSid;
                        if (!positive && !negative)
                        {
                            // 迟到的其它服务应答 / 残留帧：丢弃，继续等本次请求的应答
                            continue;
                        }

                        OnPduReceived(response);
                        isPending = IsPendingResponse(response);
                    }
                    else
                    {
                        isPending = false;
                    }

                    if (!isPending) break;
                }

                // Validate the response here.
                result = ValidateResponse(response);

                if (result != 0)
                {
                    response = null;
                    //Log.Information("Uds response is null.");
                    // TODO: create a new Exception and throw it.
                }
            }
            else
            {
                Log.Information("Uds request frame is invalid.");
                // TODO: create a new Exception and throw it.
            }

            if (response != null)
            {
                if (response.Data != null)
                {
                    string formated = BitConverter.ToString(response.Data);
                    Log.Debug("Response: {0}", formated);
                }
            }

            return response;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public override async Task<List<Pdu>?> Broadcast(Pdu pdu, CancellationToken token = default)
        {
            // Define the return value.
            Pdu? response = null;

            // Validate the requst first.
            int result = ValidateRequest(pdu);

            // Valid request?
            if (result == 0)
            {
                // Set parameters
                //SetParameter(REQ_ADDR_MODE, (byte)2);

                //Log.Information("Uds: transmit async.");
                // Transmit the request.
                result = await TransmitAsync(pdu);

                // Initialize the pending flag here.
                bool isPending = false;

                // Save the P2 and P2* here
                int p2 = GetParameter<int>(UDS_P2_MAX) > 0 ? GetParameter<int>(UDS_P2_MAX) : 50;
                int p2star = GetParameter<int>(UDS_P2_STAR) > 0 ? GetParameter<int>(UDS_P2_STAR) : 5000;

                // Wait the response.
                do
                {
                    // Create a new cancellation token source for timeout check.
                    using (CancellationTokenSource cts = new CancellationTokenSource(isPending ? p2star : p2))
                    {
                        //Log.Information("Uds: received service.");

                        // Waiting for the service response.
                        response = await ReceivedAsync(cts.Token);
                    }

                    // The response is a negative response with code = 78?
                    if (response != null)
                    {
                        isPending = IsPendingResponse(response);
                    }
                    else
                    {
                        Log.Information("Read Uds received async is null.");
                        break;//timeout.
                    }

                } while (isPending);

                // Validate the response here.
                result = ValidateResponse(response);

                if (result != 0)
                {
                    response = null;
                    //Log.Information("Uds response is null.");
                    // TODO: create a new Exception and throw it.
                }
            }
            else
            {
                Log.Information("Uds request frame is invalid.");
                // TODO: create a new Exception and throw it.
            }

            return new List<Pdu>() { response ?? new Pdu() };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdu"></param>
        /// <exception cref="NotImplementedException"></exception>
        protected override bool OnLowLevelPduReceived(Pdu pdu)
        {
            bool accept = false;

            // 任意下行报文都说明链路还活着（周期帧、应答都算）
            LastRxMs = HighResolutionTimer.Milliseconds;

            // Try to protect the _ids in different thread.
            lock (this)
            {
                // Valid the pdu data.
                if (pdu.Data != null)
                {
                    // Periodic data identifier.
                    if (pdu.Data[0] == Pid)
                    {
                        // 用 TryWrite 而不是不 await 的 WriteAsync：周期帧绝不能堆积未完成的写入任务
                        _periodicPdus.Writer.TryWrite(pdu);

                        accept = true;
                    }
                }
            }

            return accept;
        }

        /// <summary>
        /// Try to read the periodic Pdu.
        /// </summary>
        /// <returns></returns>
        public async Task<Pdu?> ReceivePeriodicPduAsync(CancellationToken token)
        {
            Pdu? pdu = null;

            try
            {
                pdu = await _periodicPdus.Reader.ReadAsync(token);
            }
            catch (Exception)
            {
                //Console.WriteLine("pasg uds.cs line:1440 " + ex.Message);
            }

            return pdu;
        }
    }
}
