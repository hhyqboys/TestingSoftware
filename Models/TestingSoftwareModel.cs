using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Xml.Serialization;
using TestingSoftware.Commands;

namespace TestingSoftware.Models
{
    public class TestingSoftwareModel
    {
        /// <summary>
        /// 是否显示配置页面
        /// </summary>
        /// <summary>
        /// 界面标题 xml加载
        /// </summary>
        public string Title { get; set; } = "请选择配置文件!";
        public List<Job> Jobs { get; set; } = new List<Job>();

        public string SnWildcard { get; set; } = "^MSM\\d{4}\\d{6}\\d\\d{5}$";
        public string DefaultSN { get; set; } = string.Empty;


        /// <summary>
        /// 是否显示控制台页面
        /// </summary>
        public bool IsConsole { get; set; } = false;
        /// <summary>
        /// RSHUNT (R value of shunt)
        /// </summary>
        public float RShunt { get; set; } = 0.0084f;

        /// <summary>
        /// AMPLIFICATION_GAIN (Amplification gain of sampling circuit)
        /// </summary>
        public float Aop { get; set; } = 0.662f;

        /// <summary>
        /// 
        /// </summary>
        public float VbusAop { get; set; } = 41.0f;

        /// <summary>
        /// ADC_REFERENCE_VOLTAGE (ADC ref voltage value)
        /// </summary>
        public float AdcRefVolt { get; set; } = 3.3f;

        /// <summary>
        /// The nominal bus voltage.
        /// </summary>
        public float NominalBusVoltage { get; set; } = 72.0f;

        /// <summary>
        /// the CAN bus baud rate for communication with the device.
        /// </summary>
        public int CanBaudRate { get; set; } = 500000;

        /// <summary>
        /// 跳过所有 IO 模块操作（调试/无 IO 模块联调开关）。
        /// true：开始测试时不再打开 IO 模块串口，任务里的 DI/DO/AI/AO 读写全部跳过（视为通过），
        /// 也不做 AO 阶梯输出与任务结束后的 DO/AO 复位，测试只执行 UDS 诊断任务。
        /// false（默认）：行为与原来完全一致。
        /// XML 配置中写成属性：SkipIoModules="true"。
        /// </summary>
        [XmlAttribute]
        public bool SkipIoModules { get; set; } = false;
        /// <summary>
        /// 跳过所有 IO 模块操作（调试/无 IO 模块联调开关）。
        /// true：开始测试时不再打开 IO 模块串口，任务里的 DI/DO/AI/AO 读写全部跳过（视为通过），
        /// 也不做 AO 阶梯输出与任务结束后的 DO/AO 复位，测试只执行 UDS 诊断任务。
        /// false（默认）：行为与原来完全一致。
        /// XML 配置中写成属性：SkipIoModules="true"。
        /// </summary>
        [XmlAttribute]
        public bool SkipCanModules { get; set; } = false;
    }
}
