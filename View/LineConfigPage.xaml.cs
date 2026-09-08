using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace TestingSoftware.View
{
    /// <summary>
    /// LineConfigPage.xaml 的交互逻辑
    /// </summary>
    public partial class LineConfigPage : UserControl
    {
        public LineConfigPage()
        {
            InitializeComponent();
            //页面加载，输入框自动聚焦等待扫码枪
            this.Loaded += (s, e) => TxtSn.Focus();
        }

        private void LineLoaded(object sender, RoutedEventArgs e)
        {
            TxtSn.Focus();
            /* Trigger binding update. */
            var dataContext = DataContext;
            DataContext = null;
            DataContext = dataContext;
        }
    }
}
