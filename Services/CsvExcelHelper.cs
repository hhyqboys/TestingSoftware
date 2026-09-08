// <copyright file=CsvExcelHelper company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/26 11:03:53</date>
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
    /// WPF专用CSV操作助手类
    /// 功能：自动创建/打开文件、异步定时保存、数据追加、线程安全
    /// </summary>
    public class CsvExcelHelper
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
        #endregion

        #region 初始化方法：创建/打开CSV文件
        /// <summary>
        /// 初始化CSV文件：存在则打开，不存在则创建
        /// </summary>
        /// <param name="fileFullPath">CSV文件完整路径（例：D:\\test\\data.csv）</param>
        public void Init(string fileFullPath)
        {
            // 重复初始化直接返回
            if (_isInitialized) return;

            _filePath = fileFullPath;

            try
            {
                // 获取文件所在目录
                var directory = Path.GetDirectoryName(_filePath);
                // 目录不存在则创建
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 文件不存在：创建文件并写入CSV表头
                if (!File.Exists(_filePath))
                {
                    // 表头：时间、芯片ID、设备SN、测试名称、测试结果
                    string header = "时间,设备芯片ID,设备SN,测试名称,测试结果";
                    File.WriteAllText(_filePath, header + Environment.NewLine, Encoding.UTF8);
                }

                // 启动异步定时保存任务
                StartAutoSaveTimer();

                _isInitialized = true;
                Console.WriteLine($"CSV初始化成功：{_filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CSV初始化失败：{ex.Message}");
                throw;
            }
        }
        #endregion

        #region 数据追加方法
        /// <summary>
        /// 追加测试数据到缓存队列（时间自动生成）
        /// </summary>
        /// <param name="chipId">设备芯片ID</param>
        /// <param name="sn">设备SN</param>
        /// <param name="testName">测试名称</param>
        /// <param name="testResult">测试结果</param>
        public void Update(string chipId, string sn)
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("请先调用Init()方法初始化CSV文件");
            }

            // 自动生成当前时间：年-月-日 时:分:秒.毫秒
            string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

            // 拼接CSV行数据（处理逗号特殊字符，避免格式错乱）
            string dataLine = $"{EscapeCsvValue(time)},{EscapeCsvValue(chipId)},{EscapeCsvValue(sn)}";

            // 加入线程安全队列
            _dataQueue.Enqueue(dataLine);
        }
        #endregion

        #region 异步定时保存核心逻辑
        /// <summary>
        /// 启动自动保存计时器
        /// </summary>
        private void StartAutoSaveTimer()
        {
            _saveTimer = new System.Timers.Timer(_saveInterval);
            // 异步保存，不阻塞UI线程
            _saveTimer.Elapsed += async (sender, e) => await SaveDataToFileAsync();
            _saveTimer.AutoReset = true;
            _saveTimer.Start();
        }

        /// <summary>
        /// 异步批量写入数据到文件
        /// </summary>
        private async Task SaveDataToFileAsync()
        {
            // 队列为空，无需保存
            if (_dataQueue.IsEmpty) return;

            try
            {
                var sb = new StringBuilder();
                // 批量取出队列中所有数据
                while (_dataQueue.TryDequeue(out string data))
                {
                    sb.AppendLine(data);
                }

                // 异步追加写入文件（UTF8编码，防止中文乱码）
                using (var streamWriter = new StreamWriter(_filePath, append: true, encoding: Encoding.UTF8))
                {
                    await streamWriter.WriteAsync(sb.ToString());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"数据保存失败：{ex.Message}");
                // 保存失败，数据重新放回队列（可选）
                // _dataQueue.Enqueue(data);
            }
        }
        #endregion

        #region 工具方法：处理CSV特殊字符
        /// <summary>
        /// CSV转义：处理逗号、双引号，防止文件格式错乱
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
        #endregion

        #region 资源释放
        /// <summary>
        /// 释放计时器资源（页面关闭时调用）
        /// </summary>
        public void Dispose()
        {
            _saveTimer?.Stop();
            _saveTimer?.Dispose();
            // 程序退出前强制保存剩余数据
            SaveDataToFileAsync().Wait();
        }
        #endregion
    }
}
