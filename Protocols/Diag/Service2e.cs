using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Protocols.Diag
{
    /// <summary>
    /// The diagnostic service is used to write data by identifier
    /// </summary>
    public class Service2e : DiagService
    {
        /// <summary>
        /// The identifier of the data.
        /// </summary>
        public UInt16 Id { get; set; }

        /// <summary>
        /// The default construct.
        /// </summary>
        /// <param name="sid"></param>
        public Service2e() : base(0x2e) { }

        /// <summary>
        /// The method is used to build the request.
        /// </summary>
        /// <param name="writer"></param>
        /// <returns></returns>
        protected override int BuildRequest(BinaryWriter writer)
        {
            int result = base.BuildRequest(writer);

            // Valid the result
            if (0 == result)
            {
                // Serialize the id
                writer.Write(Swap16(Id));

                // Try to serialize the recorde data with BuildPayload delegate
                if (BuildPayload != null)
                {
                    result = BuildPayload.Invoke(writer);
                }
            }

            return result;
        }

        /// <summary>
        /// The method is used to parse a valid 2e response from the given stream.
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        protected override int ParseResponse(BinaryReader reader)
        {
            int result = base.ParseResponse(reader);

            // Valid the result
            if (0 == result)
            {
                // Read the id
                UInt16 id = Swap16(reader.ReadUInt16());

                // Valid the id
                if (id != Id)
                {
                    result = -1;
                }
            }

            return result;
        }
    }
}
