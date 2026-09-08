// <copyright file=IDeviceTestRecordService company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>edcer@sumarte.com</author>
// <date>2026/8/28 11:45:48</date>
// <summary></summary>
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Models
{
    public interface IDeviceTestRecordService
    {
        /// <summary>
        /// 写入一条设备测试记录 
        /// </summary>
        /// <param name="sn">设备SN</param>
        /// <param name="mac">蓝牙MAC</param>
        /// <param name="status">0成功，1失败</param>
        /// <param name="error">失败原因，成功传空</param>
        /// <returns></returns>
        Task AddTestRecordAsync(string sn, string mac, int status, string error);
    }

    /// <summary>
    /// 设备测试记录落库服务，DI单例注入，只负责新增
    /// </summary>
    public class DeviceTestRecordService : IDeviceTestRecordService
    {
        private readonly SqliteDbHelper _dbHelper;

        public DeviceTestRecordService(SqliteDbHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }

        public async Task AddTestRecordAsync(string sn, string mac, int status, string error)
        {
            string sql = @"
INSERT INTO DeviceTestRecord(SN,MAC,Status,Error,CreateTime)
VALUES(@SN,@MAC,@Status,@Error,@CreateTime);";

            var paras = new[]
            {
                new SqliteParameter("@SN", sn),
                new SqliteParameter("@MAC", mac),
                new SqliteParameter("@Status", status),
                new SqliteParameter("@Error", error),
                new SqliteParameter("@CreateTime", DateTime.Now.ToString("yyyy‑MM‑dd HH:mm:ss"))
            };

            await _dbHelper.ExecuteNonQueryAsync(sql, paras);
        }
    }
}
