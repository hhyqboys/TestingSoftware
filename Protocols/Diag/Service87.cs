using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace TestingSoftware.Protocols.Diag
{
    /// <summary>
    /// The diagnostic service is used to start/stop the routine.
    /// </summary>
    public class Service87 : SubfuncService
    {
        /// <summary>
        /// This parameter specifies that the server shall request to change the baudrate.
        /// </summary>
        public const byte REQUEST = 0x01;

        /// <summary>
        /// This parameter specifies that the server shall start to execute the change.
        /// </summary>
        public const byte EXECUTE = 0x02;

        /// <summary>
        /// The request messages.
        /// </summary>
        public byte[]? ReqData { get; set; }

        /// <summary>
        /// The response message.
        /// </summary>
        public byte Resp { get; set; }

        /// <summary>
        /// The default constructor.
        /// </summary>
        public Service87() : base(0x87) { }

        /// <summary>
        /// Build a valid 31 request.
        /// </summary>
        /// <param name="writer"></param>
        /// <returns></returns>
        protected override int BuildRequest(BinaryWriter writer)
        {
            int result = base.BuildRequest(writer);

            // Valid result
            if (0 == result)
            {
                // Valid the ReqData
                if (ReqData != null && ReqData.Length > 0)
                {
                    writer.Write(ReqData, 0, ReqData.Length);
                }
                else
                {
                    result = -1;
                }
            }

            return result;
        }

        /// <summary>
        /// Parse out a valid 31 response.
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        protected override int ParseResponse(BinaryReader reader)
        {
            int result = base.ParseResponse(reader);

            // Valid the result
            if (0 == result)
            {
                // Read the feedback.
                if(reader.BaseStream.Length - reader.BaseStream.Position == 1)
                {
                    Resp = reader.ReadByte();
                }
                else
                {
                    result = -1;
                }
            }

            return result;
        }
    }
}
