// <copyright file=Affine company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/25 14:53:36</date>
// <summary></summary>
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Protocols.Transforms
{
    /// <summary>
    /// The class define a transform can be implemented use following formula:
    /// y = kx + b
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class Affine : Transform<double>
    {
        /// <summary>
        /// The slop
        /// </summary>
        public double K { get; set; } = 1;

        /// <summary>
        /// The intercept of y axis.
        /// </summary>
        public double B { get; set; } = 0;

        /// <summary>
        /// Transform the raw data to physical data.
        /// </summary>
        /// <param name="raw"></param>
        /// <returns></returns>
        public override double Foward(int raw)
        {
            return raw * K + B;
        }

        /// <summary>
        /// Transform the physical data to raw data.
        /// </summary>
        /// <param name="physical"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public override int Inverse(double physical)
        {
            return (int)((physical - B) / K);
        }
    }
}
