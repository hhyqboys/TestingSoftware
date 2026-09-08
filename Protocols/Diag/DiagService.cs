// <copyright file=DiagService company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>edcer@sumarte.com</author>
// <date>7/12/2024 2:07:37 PM</date>
// <summary></summary>

using Serilog;
using System.IO;
using System.Xml.Serialization;
using TestingSoftware.Models;


namespace TestingSoftware.Protocols.Diag
{
    /// <summary>
    /// The base class for all diagnostic services.
    /// </summary>

    [XmlInclude(typeof(Service10))]
    [XmlInclude(typeof(Service11))]
    [XmlInclude(typeof(Service14))]
    [XmlInclude(typeof(Service22))]
    [XmlInclude(typeof(Service27))]
    [XmlInclude(typeof(Service2a))]
    [XmlInclude(typeof(Service2c))]
    [XmlInclude(typeof(Service2e))]
    [XmlInclude(typeof(Service2f))]
    [XmlInclude(typeof(Service31))]
    [XmlInclude(typeof(Service34))]
    [XmlInclude(typeof(Service36))]
    [XmlInclude(typeof(Service37))]
    [XmlInclude(typeof(Service3e))]
    [XmlInclude(typeof(Service87))]
    [XmlInclude(typeof(IoService))]
    public abstract class DiagService 
    {
        /// <summary>
        /// The readonly service ID.
        /// </summary>
        private byte _sid;

        /// <summary>
        /// The readonly service ID.
        /// </summary>
        public byte Sid => _sid;
        /// <summary>
        /// The response the service is parsing.
        /// </summary>
        [XmlIgnore]
        public Pdu? Response { get; set; }

        /// <summary>
        /// The delegate is used to custom request payload.
        /// </summary>
        [XmlIgnore]
        public Func<BinaryWriter, int>? BuildPayload;

        /// <summary>
        /// The delegate handler is used to parse the user's defined payload.
        /// </summary>
        [XmlIgnore]
        public Func<BinaryReader, int>? ParsePayload;

        /// <summary>
        /// Swap byte order the given value
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static UInt16 Swap16(UInt16 value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            return BitConverter.ToUInt16(bytes, 0);
        }

        /// <summary>
        /// Swap byte order the given value
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static Int16 SwapS16(Int16 value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            return BitConverter.ToInt16(bytes, 0);
        }

        /// <summary>
        /// Swap byte order the given value
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static UInt32 Swap32(UInt32 value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }

        /// <summary>
        /// Swap byte order the given value
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static Int32 SwapS32(Int32 value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }

        /// <summary>
        /// The default constructor.
        /// </summary>
        /// <param name="sid">The service id</param>
        public DiagService(byte sid) => _sid = sid;

        /// <summary>
        /// The method is used to build a request pdu.
        /// </summary>
        /// <returns></returns>
        /// 


        public List<SignalModel> Signals { get; set; } = new List<SignalModel>();

        public Pdu BuildRequest()
        {
            UdsPdu pdu = new UdsPdu();

            // User stream to finish the request serilaize to memory.
            using (BinaryWriter writer = new BinaryWriter(new MemoryStream()))
            {
                // Call the virtual to serialize the stream.
                int result = BuildRequest(writer);

                // Valid?
                if (result == 0)
                {
                    // Build the payload here.
                    pdu.Data = new byte[writer.BaseStream.Length];

                    // Deserilaize the request into the PDU payload.
                    writer.Seek(0, SeekOrigin.Begin);
                    writer.BaseStream.Read(pdu.Data, 0, pdu.Data.Length);
                }
            }

            return pdu;
        }

        /// <summary>
        /// The method is used to parse a response from the raw data.
        /// </summary>
        /// <param name="pdu">The response</param>
        /// <returns>0 if successfully</returns>
        public int ParseResponse(Pdu pdu)
        {
            int result = 0;

            // Save the response.
            Response = pdu;

            // Valid the response data.
            if (pdu.Data != null)
            {
                // Make the sub-class can used stream to de-serialize the response.
                using (BinaryReader reader = new BinaryReader(new MemoryStream(pdu.Data)))
                {
                    // Try to de-serialize the response.
                    result = ParseResponse(reader);
                }
            }
            else
            {
                result = -1;
            }

            return result;
        }

        /// <summary>
        /// The method is used to build a request Pdu.
        /// </summary>
        /// <returns>0 if successful</returns>
        protected virtual int BuildRequest(BinaryWriter writer) { writer.Write(Sid); return 0; }

        /// <summary>
        /// The method is used to parse a new response from the given stream.
        /// </summary>
        /// <param name="stream">the raw data of the response</param>
        /// <returns></returns>
        protected virtual int ParseResponse(BinaryReader reader)
        {
            int result = -1;

            // Parse service id.
            byte sid = (byte)reader.ReadByte();

            // The service id of response is sid | 0x40
            if (sid == (_sid | 0x40))
            {
                result = 0;
            }

            return result;
        }
    }
}
