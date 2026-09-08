using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Protocols.Diag
{
    /// <summary>
    /// The class is used to define the source data identifier, which will be referenced by service 2C.
    /// </summary>
    public class SourceDataId
    {
        /// <summary>
        /// The source identifier.
        /// </summary>
        public UInt16 Id { get; set; }

        /// <summary>
        /// The position (offset) in source data identifier
        /// </summary>
        public byte Position { get; set; }

        /// <summary>
        /// The byte size in source data identifier.
        /// </summary>
        public byte Size { get; set; }
    }

    /// <summary>
    /// The service is used to defined/clear the dynamic data identifier.
    /// </summary>
    public class Service2c : SubfuncService
    {
        /// <summary>
        /// Sub-function: define by identifier.
        /// </summary>
        public const byte DEFINE_BY_IDENTIFIER = 1;

        /// <summary>
        /// Sub-function: define by memory address.
        /// </summary>
        public const byte DEFINE_BY_ADDRESS = 2;

        /// <summary>
        /// Sub-function: clear the definition.
        /// </summary>
        public const byte CLEAR_DEFINITION = 3;

        /// <summary>
        /// The dynamical identifier.
        /// </summary>
        private UInt16 _id;

        /// <summary>
        /// The identifier for the dynamic defined data.
        /// </summary>
        public UInt16 Id 
        {
            get => _id;
            set
            {
                byte high = (byte)(value >> 8);

                if (high == 0xF2 ||  high == 0xF3)
                {
                    _id = value;
                }
                else
                {
                    throw new Exception();
                }
            }
        }

        /// <summary>
        /// The source data identifier list.
        /// </summary>
        public List<SourceDataId>? SourceDataIds { get; set; }

        /// <summary>
        /// The default constructor.
        /// </summary>
        /// <param name="sid"></param>
        public Service2c() : base(0x2C) { }

        /// <summary>
        /// Build a 2C request with current setting.
        /// </summary>
        /// <param name="writer"></param>
        /// <returns></returns>
        protected override int BuildRequest(BinaryWriter writer)
        {
            int result = base.BuildRequest(writer);

            // Valid the result
            if (0 == result)
            {
                // Serialize the dynamical id
                writer.Write(Swap16(Id));

                // Add payload for the request.
                if (Subfunc == Service2c.DEFINE_BY_IDENTIFIER)
                {
                    if (BuildPayload != null)
                    {
                        result = BuildPayload.Invoke(writer);
                    }
                }

            }

            return result;
        }

        /// <summary>
        /// Parse out a new 2c response from the given stream.
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        protected override int ParseResponse(BinaryReader reader)
        {
            int result = base.ParseResponse(reader);

            // Valid the result
            if (0 == result)
            {
                // de-serialize the id.
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
