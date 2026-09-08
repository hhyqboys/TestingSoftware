// <copyright file=BluetoothInfo company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/25 13:39:16</date>
// <summary></summary>
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Adapters
{
    /// <summary>
    /// 
    /// </summary>
    public class BluetoothInfo
    {
        public static string macAddress = string.Empty;
        public string Name { get; set; } = "未知设备";
        public string Address { get; set; } = string.Empty; // BluetoothAddress 类型为 ulong
        public ulong BluetoothAddress { get; set; } // 原始 ulong 地址，用于去重
                                                    // public short Rssi { get; set; }
        public override string ToString() => $"{Name} ({Address})";
    }
}
