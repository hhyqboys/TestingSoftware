using System;
using System.Globalization;
using System.Windows.Data;
using TestingSoftware.Models;

namespace TestingSoftware.Converters
{
    public class MultiJobStateConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values[0] is not string jobName || values[1] is not int status)
            {
                return "未执行测试项目";
            }

            var stateText = status switch
            {
                0 => "(未开始)",
                1 => "(正在执行)",
                2 => "(执行完成)",
                -1 => "(执行失败)",
                _ => "(未知状态)"
            };
            return $"{jobName}  {stateText}";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
