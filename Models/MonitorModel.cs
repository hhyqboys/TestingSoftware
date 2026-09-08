// <copyright file=MonitorModel company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/26 21:46:19</date>
// <summary></summary>
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using TestingSoftware.Protocols.Diag;
using TestingSoftware.Protocols.Transforms;
using TestingSoftware.Protocols;

namespace TestingSoftware.Models
{
    /// <summary>
    /// The model for sumarte motor control ultility.
    /// </summary>
    public class MonitorModel : FileModel
    {
       



        /// <summary>
        /// The all available signals
        /// </summary>
        public List<SignalModel> Signals { get; set; } = new List<SignalModel>();

        
        /// <summary>
        /// The method is class factory for protocol stack. The UART protocol stack will be creted
        /// as default.
        /// </summary>
        /// <param name="datalink">The lowest protocol layer</param>
        /// <returns>The application layer.</returns>
        public Protocol? BuildProtocolStack(Datalink datalink)
        {
            //// Create the default protocol stack for the UART datalink.
            //if (datalink is UartDataLink)
            //{
            //    // The application is UDS protocol.
            //    Uds uds = new Uds();

            //    // Register the UDS as the high level protocol of the UART datalink.
            //    datalink.AddHighLevelProtocol(uds);

            //    return uds;
            //}

            return null;
        }

        /// <summary>
        /// The default contructor for the motor control ultility model.
        /// </summary>
        public MonitorModel() { }

    }
}
