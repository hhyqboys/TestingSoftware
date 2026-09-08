// <copyright file=BluetoothCsvHelper company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/26 11:04:45</date>
// <summary></summary>
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Services
{
    /// <summary>
    /// 蓝牙设备MAC与SN记录CSV助手类
    /// 功能：自动创建/打开文件、异步定时保存、数据追加、线程安全
    /// 表头：时间,蓝牙MAC地址,设备SN
    /// </summary>
    public class BluetoothCsvHelper : IDisposable
    {
        #region 私有字段
        // 文件完整路径
        private string _filePath;
        // 线程安全队列：缓存待写入的数据
        private readonly ConcurrentQueue<string> _dataQueue = new ConcurrentQueue<string>();
        // 定时保存计时器
        private System.Timers.Timer _saveTimer;
        // 保存间隔：10秒
        private readonly double _saveInterval = 10000;
        // 标记是否已初始化
        private bool _isInitialized = false;
        // 释放标记
        private bool _disposed = false;
        #endregion

        #region 初始化方法：创建/打开CSV文件
        /// <summary>
        /// 初始化CSV文件：存在则打开，不存在则创建
        /// </summary>
        /// <param name="fileFullPath">CSV文件完整路径（例：D:\\test\\bluetooth_devices.csv）</param>
        public void Init(string fileFullPath)
        {
            if (_isInitialized) return;

            _filePath = fileFullPath;

            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 文件不存在：创建并写入表头
                if (!File.Exists(_filePath))
                {
                    string header = "时间,蓝牙MAC地址,设备SN";
                    File.WriteAllText(_filePath, header + Environment.NewLine, Encoding.UTF8);
                }

                StartAutoSaveTimer();
                _isInitialized = true;
                Console.WriteLine($"蓝牙CSV初始化成功：{_filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"蓝牙CSV初始化失败：{ex.Message}");
                throw;
            }
        }
        #endregion

        #region 数据追加方法
        /// <summary>
        /// 追加一条蓝牙记录（字符串MAC地址）
        /// </summary>
        /// <param name="bluetoothAddress">蓝牙MAC地址，如 AA:BB:CC:DD:EE:FF</param>
        /// <param name="sn">设备SN</param>
        public void Update(string bluetoothAddress, string sn)
        {
            EnsureInitialized();

            string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string dataLine = $"{EscapeCsvValue(time)},{EscapeCsvValue(bluetoothAddress)},{EscapeCsvValue(sn)}";
            _dataQueue.Enqueue(dataLine);
        }

        /// <summary>
        /// 追加一条蓝牙记录（Windows BLE原生ulong地址，自动格式化为 AA:BB:CC:DD:EE:FF）
        /// </summary>
        /// <param name="bluetoothAddress">Windows BLE API 返回的 ulong 蓝牙地址</param>
        /// <param name="sn">设备SN</param>
        public void Update(ulong bluetoothAddress, string sn)
        {
            string macStr = FormatMacAddress(bluetoothAddress);
            Update(macStr, sn);
        }

        /// <summary>
        /// 追加一条蓝牙记录并立即刷盘（适合程序退出前最后一条）
        /// </summary>
        public void UpdateAndFlush(string bluetoothAddress, string sn)
        {
            Update(bluetoothAddress, sn);
            Flush();
        }
        #endregion

        #region 异步定时保存核心逻辑
        private void StartAutoSaveTimer()
        {
            _saveTimer = new System.Timers.Timer(_saveInterval);
            _saveTimer.Elapsed += async (sender, e) => await SaveDataToFileAsync();
            _saveTimer.AutoReset = true;
            _saveTimer.Start();
        }

        /// <summary>
        /// 异步批量写入数据到文件
        /// </summary>
        private async Task SaveDataToFileAsync()
        {
            if (_dataQueue.IsEmpty) return;

            try
            {
                var sb = new StringBuilder();
                while (_dataQueue.TryDequeue(out string data))
                {
                    sb.AppendLine(data);
                }

                using (var streamWriter = new StreamWriter(_filePath, append: true, encoding: Encoding.UTF8))
                {
                    await streamWriter.WriteAsync(sb.ToString());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"蓝牙CSV数据保存失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 立即把队列中剩余数据写入文件（同步等待）
        /// </summary>
        public void Flush()
        {
            SaveDataToFileAsync().Wait();
        }
        #endregion

        #region 工具方法
        /// <summary>
        /// CSV转义：处理逗号、双引号
        /// </summary>
        private string EscapeCsvValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(",") || value.Contains("\""))
            {
                value = value.Replace("\"", "\"\"");
                return $"\"{value}\"";
            }
            return value;
        }

        /// <summary>
        /// 将 Windows BLE ulong 地址格式化为 AA:BB:CC:DD:EE:FF
        /// </summary>
        private string FormatMacAddress(ulong address)
        {
            byte[] bytes = BitConverter.GetBytes(address);
            // ulong 是 8 字节，BLE 地址只用低 6 字节，大端输出
            return string.Join(":", bytes
                .Reverse()
                .Skip(2)   // 跳过高2字节（BLE地址为48bit）
                .Select(b => b.ToString("X2")));
        }

        private void EnsureInitialized()
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("请先调用 Init() 方法初始化蓝牙CSV文件");
            }
        }
        #endregion

        #region 资源释放
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _saveTimer?.Stop();
                _saveTimer?.Dispose();
                // 退出前强制保存剩余数据
                SaveDataToFileAsync().Wait();
            }

            _disposed = true;
        }

        ~BluetoothCsvHelper()
        {
            Dispose(false);
        }
        #endregion
    }
}
