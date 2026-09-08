// <copyright file=BooleanNegationConverter company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>jack.liu@sumarte.com</author>
// <date>2025/10/15 9:40:48</date>
// <summary></summary>
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace TestingSoftware.Converters
{
    /// <summary>
    /// Converts boolean value to its negation
    /// </summary>
    public class BooleanNegationConverter : IValueConverter
    {
        /// <summary>
        /// Returns negated boolean value
        /// </summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(bool)value;
        }

        /// <summary>
        /// Returns negated boolean value (reverse conversion)
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(bool)value;
        }
    }
}
