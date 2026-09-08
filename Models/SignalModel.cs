// <copyright file=SignalModel company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>edcer@sumarte.com</author>
// <date>2026/8/25 14:51:26</date>
// <summary></summary>
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using TestingSoftware.Protocols.Diag;
using TestingSoftware.Protocols.Transforms;

namespace TestingSoftware.Models
{
    /// <summary>
    /// The class is used to define a motor control related signals detail information.
    /// </summary>
    [XmlType("Signal")]
    public class SignalModel : FileModel
    {
        /// <summary>
        /// RSHUNT (R value of shunt)
        /// </summary>
        [XmlIgnore]
        public float RShunt { get; set; } = 0.0084f;

        /// <summary>
        /// AMPLIFICATION_GAIN (Amplification gain of sampling circuit)
        /// </summary>
        [XmlIgnore]
        public float Aop { get; set; } = 0.662f;

        /// <summary>
        /// 
        /// </summary>
        [XmlIgnore]
        public float VbusAop { get; set; } = 41.0f;

        /// <summary>
        /// ADC_REFERENCE_VOLTAGE (ADC ref voltage value)
        /// </summary>
        [XmlIgnore]
        public float AdcRefVolt { get; set; } = 3.3f;

        /// <summary>
        /// The nominal bus voltage.
        /// </summary>
        [XmlIgnore]
        public float NominalBusVoltage { get; set; } = 72.0f;
        /// <summary>
        /// The string type signal
        /// </summary>
        public const int STR = 0;

        /// <summary>
        /// The unsigned byte type signal
        /// </summary>
        public const int U8 = 2;

        /// <summary>
        /// The unsigned short type signal
        /// </summary>
        public const int U16 = 3;

        /// <summary>
        /// The signed short type signal
        /// </summary>
        public const int S16 = 4;

        /// <summary>
        /// The unsigned integer type signal
        /// </summary>
        public const int U32 = 5;

        /// <summary>
        /// The signed type signal.
        /// </summary>
        public const int S32 = 6;

        /// <summary>
        /// The float type signal, which need to transform
        /// </summary>
        public const int FLOAT = 7;
        /// <summary>
        /// 
        /// </summary>
        public bool IsTwo { get; set; } = false;




        /// <summary>
        /// The float type signal, which need to transform
        /// </summary>
        public const int BCD = 8;

        /// <summary>
        /// The length of the signals
        /// </summary>
        private byte _length = 0;


        /// <summary>
        /// The index of the signal in the signal list.
        /// </summary>

        /// <summary>
        /// Serialize the request identifier to hex format.
        /// </summary>
        [XmlElement(ElementName = "Id")]
        public string IdHex
        {
            get => Id.ToString("x");
            set => Id = Convert.ToUInt16(value, 16);
        }


        public string? ErrorMsg { get; set; }


        /// <summary>
        /// 
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// 二进制原始数据
        /// </summary>
        [XmlIgnore]
        public byte[]? WriteData { get; set; }

        /// <summary>
        /// 十六进制字符串（如 "01 02 03" 或 "010203"，双向同步）
        /// </summary>
        [XmlElement(ElementName = "WriteData")]
        public string? WriteDataHex
        {
            get
            {
                if (WriteData == null || WriteData.Length == 0)
                    return null;
                // 输出带空格分隔的十六进制，例："11 22 33 AA"
                return BitConverter.ToString(WriteData).Replace("-", " ");
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    WriteData = null;
                    return;
                }
                // 清除所有空白字符，兼容 "112233AA" 或者 "11 22 33 AA"
                string cleanHex = new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray());
                // 必须是偶数长度的16进制
                if (cleanHex.Length % 2 != 0)
                {
                    // 格式非法，可以选择抛异常或者置null，这里置null
                    WriteData = null;
                    return;
                }
                try
                {
                    byte[] buf = new byte[cleanHex.Length / 2];
                    for (int i = 0; i < buf.Length; i++)
                    {
                        buf[i] = Convert.ToByte(cleanHex.Substring(i * 2, 2), 16);
                    }
                    WriteData = buf;
                }
                catch
                {
                    // 非法十六进制字符，直接置null
                    WriteData = null;
                }
            }
        }

        /// <summary>
        /// The identifier of the data
        /// </summary>
        [XmlIgnore]
        public UInt16 Id { get; set; }


        public UInt16 Tolerance { get; set; } = 0;

        /// <summary>
        /// The human readable name of the data
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// The humam readable text for which the data expressing.
        /// </summary>
        public string? Unit { get; set; } = "N/A";

        /// <summary>
        /// The data type of the data
        /// </summary>
        public int Type { get; set; }

        /// <summary>
        /// 默认值
        /// </summary>
        public string? DefaultValue { get; set; }

        /// <summary>
        /// expectation value
        /// <summary>
        [XmlIgnore]
        public int Expectation { get; set; }
        [XmlElement(ElementName = "Expectation")]
        public string? ExpectationHex
        {
            get => Expectation.ToString("x");
            set => Expectation = Convert.ToInt32(value, 16);
        }


        /// <summary>
        /// The length of the data
        /// </summary>
        public byte Length
        {
            get
            {
                byte retval = _length;

                switch (Type)
                {
                    case U8:
                        retval = 1;
                        break;

                    case U16:
                    case S16:
                        retval = 2;
                        break;

                    case U32:
                    case S32:
                    case FLOAT:
                        retval = 4;
                        break;

                    default:
                        break;
                }

                return retval;
            }
            set => _length = value;
        }

        public byte Offset { get; set; } = 1;

        /// <summary>
        /// The transform which used to transform between the physical data and raw data.
        /// </summary>
        public ValueTable? Table { get; set; }

        /// <summary>
        /// The value of the data
        /// </summary>
        public string? Value { get; set; }

        /// <summary>
        /// The transform which used to transform between the physical data and raw data.
        /// </summary>
        [XmlIgnore]
        public Affine? Affine { get; set; }
        /// <summary>
        /// The all available affines transform.
        /// </summary>
        [XmlIgnore]
        public List<Affine> Affines => new List<Affine>
        {
            new Affine
            {
                Unit = "KW",
                K = 1
            },
            new Affine
            {
                Unit = "A",
                K = AdcRefVolt / (65536 * RShunt * Aop)
            },
            new Affine
            {
                Unit = "V[Bus]",
                K = AdcRefVolt * VbusAop / 65535
            },
            new Affine
            {
                Unit = "V",
                 K = AdcRefVolt * VbusAop / 65535
            },
            new Affine
            {
                Unit = "RPM",
                K = 0.6
            },
            new Affine
            {
                Unit = "rad",
                K = 1
            },
            new Affine
            {
                Unit = "Degree",
                K = 180.0 / 32768,
            },
            new Affine
            {
                Unit = "Celsius",
                K = 1,
            },
            new Affine
            {
                Unit = "%",
                K = 1,
            }
        };
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public override bool BuildReferences()
        {
            if (Table != null)
            {
                Table.Update();
            }
            foreach (var affine in Affines)
            {
                if (Unit.Equals(affine.Unit))
                {
                    Affine = affine;
                }
            }
            
            return true;
        }
    }
}
