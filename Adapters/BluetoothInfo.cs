// <copyright file=BluetoothInfo company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/25 13:39:16</date>
// <summary></summary>
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TestingSoftware.Adapters
{
    /// <summary>
    /// 蓝牙从机设备信息。实现 INotifyPropertyChanged，支持扫描过程中实时刷新下拉列表项。
    /// </summary>
    public class BluetoothInfo : INotifyPropertyChanged
    {
        /// <summary>当前选中连接的 MAC（静态，外部写入）</summary>
        public static string macAddress = string.Empty;

        private string _name = "未知设备";
        /// <summary>设备名称</summary>
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        /// <summary>冒号分隔的 MAC 字符串（展示用）</summary>
        public string Address { get; set; } = string.Empty;

        /// <summary>原始 ulong 蓝牙地址，用于去重与连接</summary>
        public ulong BluetoothAddress { get; set; }

        private short _rssi;
        /// <summary>最新一次广播的信号强度（dBm）</summary>
        public short Rssi
        {
            get => _rssi;
            set { _rssi = value; OnPropertyChanged(); }
        }

        private DateTime _lastSeenTime = DateTime.Now;
        /// <summary>最近一次收到该设备广播的时间，用于 30s 离线清理</summary>
        public DateTime LastSeenTime
        {
            get => _lastSeenTime;
            set { _lastSeenTime = value; OnPropertyChanged(); }
        }

        private bool _isConnected;
        /// <summary>是否处于已连接状态（连接中的设备不参与离线清理）</summary>
        public bool IsConnected
        {
            get => _isConnected;
            set { _isConnected = value; OnPropertyChanged(); }
        }

        public override string ToString() => $"{Name} ({Address})";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
