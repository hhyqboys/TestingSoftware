// <copyright file=FixedSizeQueue company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/26 9:48:54</date>
// <summary></summary>
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Utilities
{
    /// <summary>
    /// The capacity of the queue is fixed.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class FixedSizeQueue<T> : Queue<T>
    {
        /// <summary>
        /// The maximum count for the queue.
        /// </summary>
        private readonly int _capacity;

        /// <summary>
        /// The default constructor.
        /// </summary>
        /// <param name="capacity"></param>
        public FixedSizeQueue(int capacity) : base(capacity) => _capacity = capacity;

        /// <summary>
        /// Hide the base method. Use the redefine one.
        /// </summary>
        /// <param name="item"></param>
        public new void Enqueue(T item)
        {
            // Remove the oldest on if the count reached the capacity.
            if (Count >= _capacity)
            {
                Dequeue();
            }

            base.Enqueue(item);
        }
    }
}
