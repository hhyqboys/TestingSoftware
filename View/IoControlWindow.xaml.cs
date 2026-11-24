using System.Windows;

namespace TestingSoftware.View
{
    /// <summary>
    /// 下位机 IO 控制窗口（DataContext 由调用方注入 IoControlViewModel）。
    /// </summary>
    public partial class IoControlWindow : Window
    {
        public IoControlWindow()
        {
            InitializeComponent();
        }
    }
}
