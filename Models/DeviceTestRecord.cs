// <copyright file=DeviceTestRecord company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/28 11:37:01</date>
// <summary></summary>
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Models
{
    /// <summary>
    /// 
    /// </summary>
    public class DeviceTestRecord
    {
        /// <summary>
        /// 编号
        /// </summary>
        public long Id { get; set; }
        /// <summary>
        /// 设备SN号
        /// </summary>
        public string? SN { get; set; } = string.Empty;
        /// <summary>
        /// 蓝牙MAC地址
        /// </summary>
        public string? MAC { get; set; } = string.Empty;
        /// <summary>
        /// 0=成功，1=失败
        /// </summary>
        public int Status { get; set; }
        /// <summary>
        /// 错误理由
        /// </summary>
        public string? Error { get; set; } = string.Empty;
        /// <summary>
        /// 记录时间 yyyy‑MM‑dd HH:mm:ss
        /// </summary>
        public string? CreateTime { get; set; } = DateTime.Now.ToString("yyyy‑MM‑dd HH:mm:ss");
    }
}
