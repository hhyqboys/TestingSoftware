using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Protocols.Diag
{
    /// <summary>
    /// The diagnostic service is used to request download datas.
    /// </summary>
    public class Service34 : DiagService
    {
        /// <summary>
        /// Set the request address.
        /// </summary>
        public UInt32 Addr { get; set; }

        /// <summary>
        /// The request length.
        /// </summary>
        public UInt32 Length { get; set; }

        /// <summary>
        /// The max number of block length.
        /// </summary>
        public UInt16 BlockLength { get; set; }

        /// <summary>
        /// The default constructor.
        /// </summary>
        public Service34() : base(0x34) { }

        /// <summary>
        /// Build a valid 34 request.
        /// </summary>
        /// <param name="writer"></param>
        /// <returns></returns>
        protected override int BuildRequest(BinaryWriter writer)
        {
            int result = base.BuildRequest(writer);

            // Valid result
            if (0 == result)
            {
                Byte DataId = 0x00;
                Byte Format = 0x44;
                // Serialize the data format identifier
                writer.Write(DataId);

                // Serialize the address and length format identifier
                writer.Write(Format);

                // Serialize the address
                writer.Write(Swap32(Addr));

                // Serialize the length
                writer.Write(Swap32(Length));

                // Try to serialize the recorde data with BuildPayload delegate
                if (BuildPayload != null)
                {
                    result = BuildPayload.Invoke(writer);
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
                /* Drop one byte. */
                reader.ReadByte();
                // Parse the max number of block length
                BlockLength = Swap16(reader.ReadUInt16());
            }

            return result;
        }
    }
}
