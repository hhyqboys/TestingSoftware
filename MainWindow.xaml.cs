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
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                var textBox = sender as TextBox;
                if (textBox == null) return;

                // 强制把当前输入写回 MacAddressInput（触发ViewModel里的MAC校验与提示）
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                e.Handled = true;   // 防止系统发出"叮"的声音

                // 扫码结束：MAC 合法则光标自动跳到 SN 输入框，便于连扫；不合法留在MAC框重新扫
                if (IsValidMacInput(textBox.Text))
                {
                    MoveFocusToSnTextBox();
                }
                else
                {
                    textBox.SelectAll();
                }
            }
        }

        /// <summary>
        /// 扫码后的MAC合法性判断：去掉冒号/横线/空格后必须是12位十六进制（空视为未扫，不通过）。
        /// </summary>
        private static bool IsValidMacInput(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string macHex = raw.Replace(":", "").Replace("-", "").Replace(" ", "").Trim();
            if (macHex.Length != 12) return false;
            foreach (char c in macHex)
            {
                bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex) return false;
            }
            return true;
        }

        /// <summary>
        /// 把输入焦点切到 SN 框并全选。延迟到当前按键/绑定更新处理完之后再切，
        /// 避免扫码枪连发时尾部字符被吞掉。
        /// </summary>
        private void MoveFocusToSnTextBox()
        {
            if (ScanSnTextBox == null) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // SN框 GotFocus 里会清空旧内容，扫码枪下一次扫码直接覆盖
                Keyboard.Focus(ScanSnTextBox);
                ScanSnTextBox.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }
}