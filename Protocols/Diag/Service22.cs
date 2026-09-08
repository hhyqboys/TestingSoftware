using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Protocols.Diag
{
    /// <summary>
    /// The diagnostic service is used to read data by identifier.
    /// </summary>
    public class Service22 : DiagService
    {
        /// <summary>
        /// The identifier of the data.
        /// </summary>
        public UInt16 Id { get; set; }

        /// <summary>
        /// The default construct.
        /// </summary>
        /// <param name="sid"></param>
        public Service22() : base(0x22) { }

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
            writer.Write(Swap16(Id));

            return result;
        }

        /// <summary>
        /// Try to parse out a valid 22 response.
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        protected override int ParseResponse(BinaryReader reader)
        {
            int result = base.ParseResponse(reader);

            if (0 == result)
            {
                // Try to parse out the identifier.
                UInt16 id = Swap16(reader.ReadUInt16());

                // Verifty the id
                if (id == Id)
                {
                    result = ParsePayload?.Invoke(reader)??0;
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
