using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace TestingSoftware
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
        private void ScanSnTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var textBox = sender as TextBox;
                // 强制把当前输入写回 ScanSn
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                textBox.SelectAll();
                e.Handled = true;   // 防止系统发出“叮”的声音或继续冒泡
            }
        }
        private void ScanSnTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            // 清空输入框
            textBox.Text = string.Empty;

            // 可选：同时把绑定源也清空（推荐）
            // 这样 ViewModel 里的 ScanSn 也会变成空
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }

        private void MacAddressTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            // 扫码枪模式：获焦即清空，方便下一次扫码直接覆盖
            textBox.Text = string.Empty;
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }

        private void MacAddressTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var textBox = sender as TextBox;
                // 强制把当前输入写回 MacAddressInput
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                textBox.SelectAll();
                e.Handled = true;   // 防止系统发出"叮"的声音
            }
        }
    }
}