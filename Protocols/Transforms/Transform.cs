// <copyright file=Transform company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/25 14:54:04</date>
// <summary></summary>
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Protocols.Transforms
{
    /// <summary>
    /// The class defines the transform between the physical data and raw data.
    /// </summary>
    public abstract class Transform<T>
    {
        /// <summary>
        /// The name of the transform.
        /// </summary>
        public string? Unit { get; set; }

        /// <summary>
        /// The method is used to sync the members after loader from xml.
        /// </summary>
        public virtual void Update() { }

        /// <summary>
        /// Transform the raw data to pysical data.
        /// </summary>
        /// <param name="raw"></param>
        /// <returns></returns>
        public abstract T Foward(int raw);

        /// <summary>
        /// Transform the physical data to raw data.
        /// </summary>
        /// <param name="physical"></param>
        /// <returns></returns>
        public abstract int Inverse(T physical);
    }
}
