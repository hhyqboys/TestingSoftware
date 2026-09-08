using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Protocols.Diag
{
    /// <summary>
    /// 
    /// </summary>
    public class Service3e : DiagService
    {

        /// <summary>
        /// 子功能：0x00 - 需要肯定响应（Normal / Expect Positive Response）
        /// </summary>
        public byte EXPECT_RESPONSE = 0x00;
        /// <summary>
        /// 子功能：0x80 - 抑制肯定响应（Suppress Positive Response，最常用作周期性保活）
        /// </summary>
        public byte SUPPRESS_RESPONSE = 0x80;

        public byte SubFunction { get; set; }

        public Service3e() : base(0x3e)
        {
            SubFunction = SUPPRESS_RESPONSE;
        }
        /// <summary>
        /// Build the request with the current setting.
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        protected override int BuildRequest(BinaryWriter writer)
        {
            // Call the base class to do the basic serialize.
            int result = base.BuildRequest(writer);

            // Write the identifier.
            writer.Write(SubFunction);

            return result;
        }
        /// <summary>
        /// 判断给定的应用层数据是否为 UDS 3E 服务（Tester Present）相关报文
        /// </summary>
        /// <param name="data">应用层数据字节数组（已去除传输层头部）</param>
        /// <returns>如果是 3E 服务相关报文则返回 true，否则返回 false</returns>
        /// <remarks>
        /// 支持判断：
        /// - 请求：SID = 0x3E
        /// - 肯定响应：SID = 0x7E
        /// - 否定响应：7F 3E xx
        /// </remarks>
        public static bool IsUdsTesterPresent(ReadOnlySpan<byte> data)
        {
            if (data.Length < 2)
            {
                return false;
            }

            byte sid = data[0];

            // 请求：SID = 0x3E
            if (sid == 0x3E)
            {
                return true;
            }

            // 肯定响应：SID = 0x7E
            if (sid == 0x7E)
            {
                return true;
            }

            // 否定响应：7F 3E xx
            if (data.Length >= 3 && data[0] == 0x7F && data[1] == 0x3E)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 重载版本：接受 byte[] 数组
        /// </summary>
        public static bool IsUdsTesterPresent(byte[] data)
        {
            if (data == null || data.Length < 2)
            {
                return false;
            }

            return IsUdsTesterPresent(data.AsSpan());
        }
    }
}
