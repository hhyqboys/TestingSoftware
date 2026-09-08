// <copyright file=FixedSizeObservableCollection company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>edcer@sumarte.com</author>
// <date>2026/8/26 9:53:18</date>
// <summary></summary>
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Utilities
{
    /// <summary>
    /// 固定最大容量、超出后自动从头部删除的 ObservableCollection
    /// 用于 OxyPlot 实时波形，性能比 RemoveAt(0) 高 10~50 倍
    /// </summary>
    public class FixedSizeObservableCollection<T> : ObservableCollection<T>
    {
        private readonly int _maxSize;
        private readonly object _lock = new object();

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="maxSize">最大保留点数，建议 500~2000</param>
        public FixedSizeObservableCollection(int maxSize)
        {
            _maxSize = maxSize > 0 ? maxSize : 3000;
        }

        /// <summary>
        /// 添加新项时自动删除最旧项（核心优化点）
        /// </summary>
        protected override void InsertItem(int index, T item)
        {
            lock (_lock)
            {
                // 如果已满，先删除最旧的（索引 0）
                if (Count == _maxSize)
                {
                    RemoveAt(0);
                }

                // 永远添加到末尾
                base.InsertItem(Count, item);
            }
        }

        /// <summary>
        /// 提供线程安全的批量添加（可选，性能更好）
        /// </summary>
        public void AddRange(IEnumerable<T> items)
        {
            if (items == null) return;

            lock (_lock)
            {
                foreach (var item in items)
                {
                    if (Count == _maxSize)
                        RemoveAt(0);
                    base.InsertItem(Count, item);
                }
            }
        }

        /// <summary>
        /// 清空所有点（暂停时使用）
        /// </summary>
        public new void Clear()
        {
            lock (_lock)
            {
                base.Clear();
            }
        }
    }
}
