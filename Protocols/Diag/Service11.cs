using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Protocols.Diag
{
    /// <summary>
    /// The diagnostic service is used to reset the ECU server
    /// </summary>
    public class Service11 : SubfuncService
    {
        /// <summary>
        /// The default sub function.
        /// </summary>
        public const int HARD_RESET = 0x01;

        /// <summary>
        /// The default constructor.
        /// </summary>
        public Service11() : base(0x11) { }
    }
}
