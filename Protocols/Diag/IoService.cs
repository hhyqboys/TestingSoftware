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

        /// <summary>
        /// 阶梯模式下 Values 中"可变字节"的起始下标。
        /// XML 里写 xx（如 00 00 xx xx）即代表占位符，-1 表示没有占位符。
        /// </summary>
        [XmlIgnore]
        public int PlaceholderOffset { get; private set; } = -1;

        /// <summary>
        /// 占位符字节个数（xx 的个数）。
        /// </summary>
        [XmlIgnore]
        public int PlaceholderCount { get; private set; } = 0;

        /// <summary>
        /// 当前阶梯输出值（运行时状态，不参与序列化）。
        /// </summary>
        [XmlIgnore]
        public ushort CurrentStepValue { get; set; }

        /// <summary>
        /// 阶梯递增输出配置。为 null 时表示该 IoService 不启用阶梯模式（老XML即此情况）。
        /// </summary>
        public IoStepConfig? Step { get; set; }

        /// <summary>
        /// 是否启用阶梯递增输出。
        /// </summary>
        [XmlIgnore]
        public bool IsStepEnabled => Step != null;

        /// <summary>
        /// 阶梯输出的上限值：优先取 Step.MaxValue，未配置时取第一个 Signal 的 Expectation。
        /// </summary>
        [XmlIgnore]
        public ushort EffectiveMaxValue
        {
            get
            {
                if (Step != null && Step.MaxValue > 0)
                    return Step.MaxValue;

                if (Signals != null && Signals.Count > 0 && Signals[0].Expectation > 0)
                    return (ushort)Signals[0].Expectation;

                return 0;
            }
        }

        /// <summary>
        /// 把阶梯值（大端 16 位）写入 Values 的占位位置。
        /// </summary>
        public void ApplyStepValue(ushort value)
        {
            CurrentStepValue = value;

            if (Values == null || Values.Length < 2)
                return;

            // 优先用 XML 里 xx 占位的位置；没有占位符则默认写最后两个字节
            int offset = PlaceholderOffset;
            if (offset < 0 || offset + 2 > Values.Length)
                offset = Values.Length - 2;

            // 大端：高字节在前
            Values[offset] = (byte)(value >> 8);
            Values[offset + 1] = (byte)(value & 0xFF);
        }

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
                PlaceholderOffset = -1;
                PlaceholderCount = 0;

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
                    // xx / XX 视为占位符：先存 0，执行时由阶梯值填入
                    if (parts[i].Equals("xx", StringComparison.OrdinalIgnoreCase))
                    {
                        if (PlaceholderOffset < 0)
                            PlaceholderOffset = i;
                        PlaceholderCount++;
                        Values[i] = 0;
                        continue;
                    }
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
