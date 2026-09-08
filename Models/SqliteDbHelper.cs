// <copyright file=SqliteDbHelper company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/28 11:27:47</date>
// <summary></summary>
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Models
{
    /// <summary>
    /// 
    /// </summary>
    public class SqliteDbHelper
    {
        private readonly string _dbFilePath;

        public SqliteDbHelper(string dbFileName = "business.db")
        {
            var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TestingSoftware");
            if (!Directory.Exists(appDataDir))
                Directory.CreateDirectory(appDataDir);

            _dbFilePath = Path.Combine(appDataDir, dbFileName);
        }
        /// <summary>
        /// 
        /// </summary>
        private string ConnectionString => $"Data Source={_dbFilePath}";

        /// <summary>
        /// 执行增删改
        /// </summary>
        public async Task<int> ExecuteNonQueryAsync(string sql, params SqliteParameter[] parameters)
        {
            await using var conn = new SqliteConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new SqliteCommand(sql, conn);
            if (parameters != null && parameters.Length > 0)
                cmd.Parameters.AddRange(parameters);
            return await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// 初始化：设备测试记录表 DeviceTestRecord
        /// </summary>
        public async Task InitDatabaseAsync()
        {
            string createSql = @"
                CREATE TABLE IF NOT EXISTS DeviceTestRecord (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SN TEXT NOT NULL,
                    MAC TEXT,
                    Status INTEGER NOT NULL DEFAULT 0,
                    Error TEXT,
                    CreateTime TEXT
                );
                CREATE INDEX IF NOT EXISTS IX_DeviceTestRecord_SN ON DeviceTestRecord(SN);
                ";
            await ExecuteNonQueryAsync(createSql);
        }
    }
}
