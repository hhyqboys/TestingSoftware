// <copyright file=HighResolutionTimer company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2026/8/25 13:55:09</date>
// <summary></summary>
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Utilities
{

    /// <summary>
    /// The high resolution timer.
    /// </summary>
    public class HighResolutionTimer
    {
        [DllImport("Kernel32.dll")]
        private static extern int QueryPerformanceCounter(ref Int64 count);

        [DllImport("Kernel32.dll")]
        private static extern int QueryPerformanceFrequency(ref Int64 frequency);

        /// <summary>
        /// The current timestamp with miliseconds.
        /// </summary>
        public static Int64 Milliseconds
        {
            get
            {
                Int64 count = 0, frequency = 0;

                // Get counter from the last startup
                QueryPerformanceCounter(ref count);

                // Get the frequency per seconds
                QueryPerformanceFrequency(ref frequency);

                return count * 1000 / frequency;
            }
        }
    }
}
