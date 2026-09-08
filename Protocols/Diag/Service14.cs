using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Protocols.Diag
{
    /// <summary>
    /// The diagnostic service is used to reset the ECU server
    /// </summary>
    public class Service14 : SubfuncService
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public Service14() : base(0x14) { }

        /// <summary>
        /// Build the request with the current setting.
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        protected override int BuildRequest(BinaryWriter writer)
        {
            // Call the base class to do the basic serialize.
            int result = base.BuildRequest(writer);

            // Try to serialize the recorde data with BuildPayload delegate
            if (BuildPayload != null)
            {
                result = BuildPayload.Invoke(writer);
            }

            return result;
        }
    }
}
