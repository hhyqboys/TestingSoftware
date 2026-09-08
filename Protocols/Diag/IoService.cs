using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using TestingSoftware.Models;

namespace TestingSoftware.Protocols.Diag
{
    public class IoService : DiagService
    {
        [Description("从机ID")]
        [XmlIgnore]
        public byte SlaveId { get; set; }


        /// <summary>
        /// Serialize the request identifier to hex format.
        /// </summary>
        [XmlElement(ElementName = "SlaveId")]
        public string IdHex
        {
            get => SlaveId.ToString("x");
            set => SlaveId = Convert.ToByte(value,16);
        }

        /// <summary>
        /// The address of the io service.
        /// </summary>
        [Description("读取长度")]
        [XmlIgnore]
        public int Length { get; set; }

        /// <summary>
        /// The address of the io service.
        /// </summary>
        [Description("读取长度")]
        [XmlElement(ElementName = "Length")]
        public string LengthHex
        {
            get => Length.ToString("x");
            set => Length = Convert.ToByte(value, 16);
        }


        [Description("IO类型")]
        public IoType IoType { get; set; }

        [XmlIgnore]
        public ushort StartAddress { get; set; }
        /// <summary>
        /// Serialize the request identifier to hex format.
        /// </summary>
        [XmlElement(ElementName = "StartAddress")]
        public string StartAddressHex
        {
            get => StartAddress.ToString("x");
            set => StartAddress = Convert.ToUInt16(value, 16);
        }

        [XmlIgnore]
        public int Value { get; set; }
        /// <summary>
        /// Serialize the request identifier to hex format.
        /// </summary>
        [XmlElement(ElementName = "Value")]
        public string ValueHex
        {
            get => Value.ToString("x");
            set => Value = Convert.ToUInt16(value, 16);
        }
        [XmlIgnore]
        public byte[] Values { get; set; }

        [XmlElement(ElementName = "Values")]
        public string ValuesHex
        {
            get
            {
                if (Values == null || Values.Length == 0)
                    return string.Empty;
                // 每个字节2位小写hex，空格分隔，示例：01 31 00 05 69 7f
                return BitConverter.ToString(Values).Replace("-", " ").ToLower();
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    Values = Array.Empty<byte>();
                    return;
                }
                // 按空格分割，过滤空字符串（防止多个连续空格）
                var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                Values = new byte[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    Values[i] = Convert.ToByte(parts[i], 16);
                }
            }
        }


        public IoService() : base(0xff)
        {

        }
        /// <summary>
        /// Build the request with the current setting.
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        protected override int BuildRequest(BinaryWriter writer)
        {

            return 0;
        }
        /// <summary>
        /// Try to parse out a valid 22 response.
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        protected override int ParseResponse(BinaryReader reader)
        {
            int result = 0;


            // Verifty the id
            if (ParsePayload != null)
            {
                result = ParsePayload.Invoke(reader);
            }
            else
            {
                result = -1;
            }


            return result;
        }
    }
}
