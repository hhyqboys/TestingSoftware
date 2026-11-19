// <copyright file=IoStepConfig company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <summary>IO阶梯递增输出配置</summary>

using System;
using System.Linq;
using System.Xml.Serialization;

namespace TestingSoftware.Models
{
    /// <summary>
    /// AO 阶梯递增输出配置，序列化为 IoService 下的 &lt;Step&gt; 节点。
    /// 只有显式写了 &lt;Step&gt; 的 IoService 才会进入阶梯模式；
    /// 老 XML 没有该节点，反序列化后 Step 为 null，执行逻辑与原来完全一致。
    /// </summary>
    public class IoStepConfig
    {
        /// <summary>
        /// 把 "03E8" / "0x03E8" / "03 e8" 之类的字符串解析为 ushort。
        /// 空字符串或非法内容返回 0。
        /// </summary>
        public static ushort ParseHex(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            string clean = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(2);

            if (clean.Length == 0)
                return 0;

            try
            {
                return ushort.Parse(clean, System.Globalization.NumberStyles.HexNumber);
            }
            catch
            {
                return 0;
            }
        }

        private ushort _startValue = 0;
        /// <summary>
        /// 起始值（十六进制），上电后第一次输出即使用该值。
        /// </summary>
        [XmlIgnore]
        public ushort StartValue
        {
            get => _startValue;
            set => _startValue = value;
        }

        [XmlElement(ElementName = "StartValue")]
        public string StartValueHex
        {
            get => _startValue.ToString("X4");
            set => _startValue = ParseHex(value);
        }

        private ushort _stepValue = 0x0064;
        /// <summary>
        /// 每次判定成功后自增的步进值（十六进制），默认 0x0064 即 100。
        /// </summary>
        [XmlIgnore]
        public ushort StepValue
        {
            get => _stepValue;
            set => _stepValue = value;
        }

        [XmlElement(ElementName = "StepValue")]
        public string StepValueHex
        {
            get => _stepValue.ToString("X4");
            set => _stepValue = ParseHex(value);
        }

        private ushort _maxValue = 0;
        /// <summary>
        /// 上限值（十六进制）。为 0 时取 IoService 第一个 Signal 的 Expectation。
        /// </summary>
        [XmlIgnore]
        public ushort MaxValue
        {
            get => _maxValue;
            set => _maxValue = value;
        }

        [XmlElement(ElementName = "MaxValue")]
        public string MaxValueHex
        {
            get => _maxValue == 0 ? string.Empty : _maxValue.ToString("X4");
            set => _maxValue = ParseHex(value);
        }

        /// <summary>
        /// 每级抬升后、重新开始判定前的等待时间ms。
        /// 为 0 时使用 Job.CycleDelayTimeMs。
        /// </summary>
        public int DelayAfterStepMs { get; set; } = 0;
    }
}
