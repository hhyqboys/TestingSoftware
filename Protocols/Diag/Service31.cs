using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Protocols.Diag
{
    /// <summary>
    /// The diagnostic service is used to start/stop the routine.
    /// </summary>
    public class Service31 : SubfuncService
    {
        /// <summary>
        /// This parameter specifies that the server shall start the routine specified by routine identifier
        /// </summary>
        public const byte START_ROUTINE = 0x01;

        /// <summary>
        /// This parameter specifies that the server shall stop the routine specified by routine identifier
        /// </summary>
        public const byte STOP_ROUTINE = 0x02;

        /// <summary>
        /// This parameter specifices that the server shall return result values of the routine specified by routine identifier
        /// </summary>
        public const byte REQUEST_ROUTINE_RESULTS = 0x03;

        /// <summary>
        /// The identifier of the routine.
        /// </summary>
        public UInt16 Id { get; set; }

        /// <summary>
        /// The default constructor.
        /// </summary>
        public Service31() : base(0x31) { }

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
                // Serialize the routine id
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
                // Serialize the routine identifier
                UInt16 id = Swap16(reader.ReadUInt16());

                // Valid id
                if (id != Id)
                {
                    result = -1;
                }
                else
                {
                    result = ParsePayload?.Invoke(reader) ?? 0;
                }
            }

            return result;
        }
    }
}
