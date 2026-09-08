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
    public class Service37 : DiagService
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public Service37() : base(0x37) { }

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

            }

            return result;
        }
    }
}
