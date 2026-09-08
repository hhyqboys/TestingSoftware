using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Protocols.Diag
{
    /// <summary>
    /// The diagnostic service is used read data by identifier in periodically.
    /// </summary>
    public class Service2a : DiagService
    {
        /// <summary>
        /// Send the data at slow rate.
        /// </summary>
        public const byte SLOW_RATE = 1;

        /// <summary>
        /// Send the data at medium rate.
        /// </summary>
        public const byte MEDIUM_RATE = 2;

        /// <summary>
        /// Send the data at fast rate.
        /// </summary>
        public const byte FAST_RATE = 3;

        /// <summary>
        /// Stop sending.
        /// </summary>
        public const byte STOP = 4;

        /// <summary>
        /// The transmitting mode.
        /// </summary>
        public byte Mode { get; set; }

        /// <summary>
        /// All available id (The last byte of the identifier.) list 
        /// </summary>
        public byte[]? Ids { get; set; }

        /// <summary>
        /// The default constructor
        /// </summary>
        public Service2a() : base(0x2A) { }

        /// <summary>
        /// Build a valid 2a request with the setting.
        /// </summary>
        /// <param name="writer"></param>
        /// <returns></returns>
        protected override int BuildRequest(BinaryWriter writer)
        {
            int result = base.BuildRequest(writer);

            // Valid the result
            if (0 == result)
            {
                // Serialize the transmitting mode.
                writer.Write(Mode);

                // Valid the id list
                if (null != Ids)
                {
                    // Serialize the identifier list
                    foreach (var id in Ids)
                    {
                        writer.Write(id);
                    }
                }
                else
                {
                    result = -1;
                }
            }
            else
            {
                result = -1;
            }

            return result;
        }

        /// <summary>
        /// Try to parse the response for normal and periodic response.
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        protected override int ParseResponse(BinaryReader reader)
        { 
            // Try to parse the response with normal 2A.
            int result = base.ParseResponse(reader);

            // Failed
            if (result != 0)
            {
                // Reset the stream.
                reader.BaseStream.Seek(0, SeekOrigin.Begin);

                // Try to read the id to decide the response is a valid periodic response or not.
                byte id = reader.ReadByte();

                // Valid id?
                if (Ids != null && Ids.Contains(id))
                {
                    result = ParsePayload?.Invoke(reader)??-1;
                }
            }

            return result;
        }
    }
}
