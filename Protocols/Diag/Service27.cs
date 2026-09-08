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
    public class Service27 : SubfuncService
    {
        /// <summary>
        /// The response seed parameter.
        /// </summary>
        public byte[]? Seed { get; set; }

        /// <summary>
        /// The key value.
        /// </summary>
        public byte[]? Key { get; set; }

        /// <summary>
        /// Create a new service 0x27
        /// </summary>
        public Service27() : base(0x27) { }

        /// <summary>
        /// The method is used to build a new security access request.
        /// </summary>
        /// <param name="stream">The stream which write to</param>
        /// <returns>0 if successfully</returns>
        protected override int BuildRequest(BinaryWriter writer)
        {
            int result = base.BuildRequest(writer);
           
            // Request seed if the sub-function is event. 
            if (Subfunc % 2 == 0)
            {
                // Valid the key
                if (Key != null && Key.Length > 0)
                {
                    writer.Write(Key, 0, Key.Length);
                }
                else
                {
                    result = -1;
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

            // Valid the result.
            if (result == 0)
            {
                // A seed required.
                if (Subfunc % 2 != 0)
                {
                    // A valid seed.
                    if (reader.BaseStream.Length - reader.BaseStream.Position == 4)
                    {
                        // Create a new space for the response seed.
                        byte[] seed = new byte[reader.BaseStream.Length - reader.BaseStream.Position];

                        // Load the seed.
                        reader.Read(seed, 0, seed.Length);

                        // Save the seed.
                        Seed = seed;
                    }
                    else
                    {
                        result = -1;
                    }
                }
            }

            return result;
        }
    }
}
