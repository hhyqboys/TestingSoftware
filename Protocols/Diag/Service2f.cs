// <copyright file=Service27 company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>edcer@sumarte.com</author>
// <date>7/12/2024 2:23:28 PM</date>
// <summary></summary>


using System.IO;
using System.Xml.Serialization;

namespace TestingSoftware.Protocols.Diag
{
    /// <summary>
    /// The service is used to security access
    /// </summary>
    public class Service2f : DiagService
    {
        /// <summary>
        /// This parameter specifies that the server shall short adjust ecu
        /// </summary>
        public const byte SHORT_TERM_ADJUSTMENT = 0x03;

        /// <summary>
        /// This parameter specifies that the server shall return control to ecu
        /// </summary>
        public const byte RETURN_CONTROL_TO_ECU = 0x00;

        /// <summary>
        /// The identifier of the routine.
        /// </summary>
        public UInt16 Id { get; set; }

        /// <summary>
        /// The transfer block nubmer.
        /// </summary>
        public Byte Ctrl_type { get; set; }

        /// <summary>
        /// Create a new service 0x2f
        /// </summary>
        public Service2f() : base(0x2f) { }

        /// <summary>
        /// The method is used to build a new security access request.
        /// </summary>
        /// <param name="stream">The stream which write to</param>
        /// <returns>0 if successfully</returns>
        protected override int BuildRequest(BinaryWriter writer)
        {
            int result = base.BuildRequest(writer);

            // Valid result
            if (0 == result)
            {
                // Serialize the routine id
                writer.Write(Swap16(Id));

                // Set the control type.
                writer.Write(Ctrl_type);

                // Try to serialize the recorde data with BuildPayload delegate
                if (BuildPayload != null)
                {
                    result = BuildPayload.Invoke(writer);
                }
            }

            return result;
        }

        /// <summary>
        /// The method is used parse a new response from the given stream.
        /// </summary>
        /// <param name="stream"></param>
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
