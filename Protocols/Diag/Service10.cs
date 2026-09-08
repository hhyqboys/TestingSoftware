// <copyright file=Service10 company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>edcer@sumarte.com</author>
// <date>7/12/2024 2:23:15 PM</date>
// <summary></summary>

using System.IO;
using System.Xml.Serialization;

namespace TestingSoftware.Protocols.Diag
{
    /// <summary>
    /// The service is used to control Session.
    /// </summary>
    public class Service10 : SubfuncService
    {
        /// <summary>
        /// The default session.
        /// </summary>
        public const byte DEFAULT_SESSION = 0x01;

        /// <summary>
        /// Programming session.
        /// </summary>
        public const byte PROGRAMMING_SESSION = 0x02;

        /// <summary>
        /// Extended diagnostic session.
        /// </summary>
        public const byte EXTENDED_DIAG_SESSION = 0x03;

        /// <summary>
        /// The time parameter P2Max for server side. 
        /// </summary>
        [XmlIgnore]
        public int P2ServerMax { get; set; }

        /// <summary>
        /// The time parameter P2*Max for server side.
        /// </summary>
        [XmlIgnore]
        public int P2StarServerMax { get; set; }

        /// <summary>
        /// Create a new service 0x10
        /// </summary>
        public Service10() : base(0x10) { }

        /// <summary>
        /// The method is used to parse the Session control parameters.
        /// </summary>
        /// <param name="stream">The response stream</param>
        /// <returns>0 if successfully</returns>
        protected override int ParseResponse(BinaryReader reader)
        {
            int result = base.ParseResponse(reader);

            // Try to parse out the time parameter for the session.
            if (result == 0)
            {
                // Parse the p2 server max
                P2ServerMax = Swap16(reader.ReadUInt16());

                // Parse the p2* server max
                P2StarServerMax = Swap16(reader.ReadUInt16()) * 10;
            }

            return result;
        }
    }
}
