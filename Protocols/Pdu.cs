// <copyright file=Pdu company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/25 13:45:04</date>
// <summary></summary>
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Protocols
{
    /// <summary>
    /// The protocol data unit.
    /// </summary>
    public class Pdu
    {
        /// <summary>
        /// The timestamp when the data unit generated.
        /// </summary>
        public Int64 Timestamp { get; set; }

        /// <summary>
        /// True: Tx, False: Rx
        /// </summary>
        public bool Tx { get; set; }

        /// <summary>
        /// The raw data of the protocol data unit.
        /// </summary>
        public byte[]? Data { get; set; }
    }
}
