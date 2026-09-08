// <copyright file=SubfuncService company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>edcer@sumarte.com</author>
// <date>7/12/2024 2:16:10 PM</date>
// <summary></summary>


using System.IO;
using System.Xml.Serialization;

namespace TestingSoftware.Protocols.Diag
{
    /// <summary>
    /// The diagnostic service which support sub-function 
    /// </summary>
    public abstract class SubfuncService : DiagService
    {
        /// <summary>
        /// The sub-function identifier.
        /// </summary>
        private byte _subfunc;

        /// <summary>
        /// The sub-function id.
        /// </summary>
        public byte Subfunc 
        {
            get => _subfunc;
            set
            {
                _subfunc = (byte)(value & 0x7F);
                SuppressPositiveResponse = (value & 0x80) != 0 ? true : false;
            }
        }

        /// <summary>
        /// The suppress positive response flag.
        /// </summary>
        public bool SuppressPositiveResponse { get; set; }

        /// <summary>
        /// The default constructor of sub-function service.
        /// </summary>
        /// <param name="sid"></param>
        protected SubfuncService(byte sid) : base(sid) { }

        /// <summary>
        /// The method is used to build the sub-function based request.
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        protected override int BuildRequest(BinaryWriter writer)
        {
            int result = base.BuildRequest(writer);

            if (result == 0)
            {
                // Write the sub-function.
                writer.Write((byte)(SuppressPositiveResponse ? Subfunc | 0x80 : Subfunc));
            }

            return result;
        }

        /// <summary>
        /// The method is used to parse the response from the given stream.
        /// </summary>
        /// <param name="stream">The response stream</param>
        /// <returns>0 if successfully</returns>
        protected override int ParseResponse(BinaryReader reader)
        {
            int result = base.ParseResponse(reader);

            if (result == 0)
            {
                byte subfunc = (byte)reader.ReadByte();

                if (subfunc != Subfunc)
                {
                    return -1;// bad format.
                }
            }

            return result;
        }
    }
}
