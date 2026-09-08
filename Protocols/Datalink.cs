// <copyright file=Protocol company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/25 13:44:34</date>
// <summary></summary>
using System.Xml.Serialization;
using TestingSoftware.Protocols.Bluetoolth;

namespace TestingSoftware.Protocols
{
    /// <summary>
    /// The class define the basical requirement of the OSI datalink layer. 
    /// </summary>
    [XmlInclude(typeof(BluetoothDataLink))]   // 指定 ZLG 数据链路派生类型
    public abstract class Datalink : Protocol
    {
        /// <summary>
        /// The parameter name for baudrate.
        /// </summary>
        public const string BAUDRATE = "baudrate";

        /// <summary>
        /// The default value of the baudrate.
        /// </summary>
        public const int BAUDRAT_DEFAULT = 500000;

        /// <summary>
        /// The constructor is used to create a new OSI datalink instance.
        /// </summary>
        /// <param name="layer"></param>
        /// <param name="name"></param>
        internal Datalink(Layer layer, string name) : base(layer, name) => SetParameter<int>(BAUDRATE, BAUDRAT_DEFAULT, true);

        /// <summary>
        /// The method is used to test the specific datalink channel is valid or not.
        /// </summary>
        /// <returns>true if the datalink is available.</returns>
        public abstract bool Available();
    }
}
