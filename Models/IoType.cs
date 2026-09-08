using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace TestingSoftware.Models
{
    public enum IoType
    {
        [Description("未定义")]
        [XmlEnum("未定义")]
        None = 0,

        [Description("DI")]
        [XmlEnum("DI")]
        Di = 1,

        [Description("DO")]
        [XmlEnum("DO")]
        Do = 2,

        [Description("AI")]
        [XmlEnum("AI")]
        Ai = 3,

        [Description("AO")]
        [XmlEnum("AO")]
        Ao = 4
    }
}
