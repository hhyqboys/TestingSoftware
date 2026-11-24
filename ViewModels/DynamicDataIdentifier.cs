// <copyright file=DynamicDataIdentifier company="Sumarte Technology">
// Copyright (c) 2026 All Rights Reserved
// </copyright>
// <summary>构造 2C 动态 DID 定义报文并解析 2A 周期上报帧（做法与 MotorStudio 保持一致）。</summary>
using System.Collections.Generic;
using System.IO;

namespace TestingSoftware.ViewModels
{
    /// <summary>
    /// 按源 DID 分组维护待监控信号，用于生成 2C（DefineDataByDataIdentifier）请求，
    /// 并按 2A 周期上报帧顺序解析出各信号值。
    /// </summary>
    public class DynamicDataIdentifier
    {
        /// <summary>
        /// 源 DID -> 信号组，便于快速查找已存在的组。
        /// </summary>
        private readonly Dictionary<ushort, SignalGroup> _map = new Dictionary<ushort, SignalGroup>();

        /// <summary>
        /// 所有信号组，顺序与 2C 定义报文中的书写顺序一致。
        /// </summary>
        private readonly List<SignalGroup> _groups = new List<SignalGroup>();

        /// <summary>
        /// 添加一个待监控信号；同一个 DID 下的信号会自动并入同一组。
        /// </summary>
        public void AddSignal(SignalViewModel signal)
        {
            if (_map.TryGetValue(signal.Id, out SignalGroup? group) && group != null)
            {
                group.AddSignal(signal);
            }
            else
            {
                group = new SignalGroup(signal.Id);
                group.AddSignal(signal);
                _map.Add(signal.Id, group);
                _groups.Add(group);
            }
        }

        /// <summary>
        /// 生成 2C 定义的请求负载（不含 2C 服务号与动态 DID）。
        /// </summary>
        public int BuildRequest(BinaryWriter writer)
        {
            foreach (var group in _groups)
            {
                group.BuildRequest(writer);
            }

            return 0;
        }

        /// <summary>
        /// 解析一帧周期上报数据（不含首字节的动态 DID 低字节）。
        /// </summary>
        public void ParseResponse(BinaryReader reader)
        {
            foreach (var group in _groups)
            {
                group.ParseResponse(reader);
            }
        }

        /// <summary>
        /// 清空全部定义，重新开启监控前必须调用。
        /// </summary>
        public void Clear()
        {
            foreach (var group in _groups)
            {
                group.Signals.Clear();
            }

            _groups.Clear();
            _map.Clear();
        }
    }
}
