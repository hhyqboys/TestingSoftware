using System.ComponentModel;
using System.Xml.Serialization;
using TestingSoftware.Protocols.Diag;
using TestingSoftware.Protocols.Transforms;
using TestingSoftware.ViewModels;

namespace TestingSoftware.Models
{
    public class Job : FileModel
    {
        private int _status = 0;
        private bool _TestRecorded;

        [Description("执行状态 0:未执行 1:执行中 2:执行完成 -1:执行失败")]
        [XmlIgnore]
        public int Status
        {
            get => _status; set
            {
                _status = value;
                OnPropertyChanged(); // 修改Status会UI刷新圆球
            }
        } //0:未执行 1:执行中 2:执行完成 -1:执行失败

        [XmlIgnore]
        public bool TestRecorded
        {
            get => _TestRecorded; set
            {
                _TestRecorded = value;
                OnPropertyChanged(); 
            }
        } 
        [Description("任务名称")]
        public string JobName { get; set; } = string.Empty;
        [Description("任务序号")]
        public string SerialNumber { get; set; } = string.Empty;
        [Description("任务说明")]
        public string JobDescription { get; set; } = string.Empty;

        [Description("任务是否循环")]
        public bool Cycle { get; set; } = false;
        [Description("任务循环间隔时间ms")]
        public int CycleDelayTimeMs { get; set; } = 100;
        public int StartDelayTimeMs { get; set; } = 100;

        [Description("任务循环次数 1-65535")]
        public int CycleNumber { get; set; } = 1;


        [Description("是否维持解锁会话状态")]
        public bool Keep3E80 { get; set; } = false;

        [Description("是否是Can任务")]
        public bool IsCan { get; set; } = false;

        [Description("任务执行信号")]
        public List<DiagService> Services { get; set; } = new List<DiagService>();

        /// <summary>
        /// 复测前置任务：复测从本任务（或其后的任务）开始时，会先补跑这些任务，
        /// 用来恢复被复测起点跳过、但设备又必需的状态（如设置速度、启动电机）。
        /// 按任务名称（JobName）或任务序号（SerialNumber）匹配，按书写顺序补跑。
        /// </summary>
        [Description("复测前置任务（名称或序号），复测时先补跑")]
        [XmlArray(ElementName = "Prerequisites")]
        [XmlArrayItem(ElementName = "Prerequisite")]
        public List<string> Prerequisites { get; set; } = new List<string>();

        /// <summary>
        /// 没有前置任务时不写该节点，避免老工程保存后多出空元素。
        /// </summary>
        public bool ShouldSerializePrerequisites()
        {
            return Prerequisites != null && Prerequisites.Count > 0;
        }

        [XmlIgnore]
        public List<SignalViewModel> SignalViewModels { get; set; } = new List<SignalViewModel>();
    }

    /// <summary>
    /// 
    /// </summary>
    public class JobItem
    {
        /// <summary>
        /// 
        /// </summary>
        public int Status { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public string ShowText { get; set; }
    }
}
