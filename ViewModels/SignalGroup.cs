// <copyright file=SignalGroup company="Sumarte Technology">
// Copyright (c) 2026 All Rights Reserved
// </copyright>
// <summary>2C 动态 DID 定义中的最小单元：同一个源 DID 下的一组信号。</summary>
using System.Collections.Generic;
using System.IO;
using TestingSoftware.Protocols.Diag;

namespace TestingSoftware.ViewModels
{
    /// <summary>
    /// 把同一个源数据标识符（DID）下的若干信号归为一组，
    /// 一组对应 2C 定义报文里的 [DID(2byte)][Offset(1byte)][Length(1byte)] 三元组。
    /// </summary>
    public class SignalGroup
    {
        /// <summary>
        /// 源数据标识符。
        /// </summary>
        private ushort Id { get; set; }

        /// <summary>
        /// 该组在源 DID 中的起始偏移。
        /// </summary>
        private byte Offset { get; set; } = 1;

        /// <summary>
        /// 该组占用的字节长度（组内信号长度之和）。
        /// </summary>
        private byte Length { get; set; }

        /// <summary>
        /// 该组包含的所有信号。
        /// </summary>
        public List<SignalViewModel> Signals { get; set; } = new List<SignalViewModel>();

        public SignalGroup(ushort id)
        {
            Id = id;
        }

        /// <summary>
        /// 向组内追加信号。
        /// </summary>
        public void AddSignal(SignalViewModel signal)
        {
            Signals.Add(signal);
        }

        /// <summary>
        /// 按偏移排序后计算组偏移与组长度，并写入 2C 定义报文。
        /// </summary>
        public void BuildRequest(BinaryWriter writer)
        {
            // 组内信号按偏移升序排列，保证解析顺序与下位机上报顺序一致。
            Signals.Sort((s1, s2) => s1.Offset - s2.Offset);

            Length = 0;
            Offset = Signals[0].Offset;
            foreach (var signal in Signals)
            {
                Length += signal.Length;
            }

            writer.Write(DiagService.Swap16(Id));
            writer.Write(Offset);
            writer.Write(Length);
        }

        /// <summary>
        /// 从周期上报表中依次解析组内信号。
        /// </summary>
        public void ParseResponse(BinaryReader reader)
        {
            foreach (var signal in Signals)
            {
                signal.Parse(reader);
            }
        }
    }
}
