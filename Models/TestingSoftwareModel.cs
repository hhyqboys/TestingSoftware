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
        public bool IsShowPidView { get; set; } = false;
        /// <summary>
        /// 界面标题 xml加载
        /// </summary>
        public string Title { get; set; } = "请选择配置文件!";
        public List<Job> Jobs { get; set; } = new List<Job>();

        public string SnWildcard { get; set; } = "^MSM\\d{4}\\d{6}\\d\\d{5}$";
        public string DefaultSN { get; set; } = string.Empty;
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
    }
}
