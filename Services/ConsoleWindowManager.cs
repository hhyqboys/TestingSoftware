using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Services
{
    public static class ConsoleWindow
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

        private delegate bool ConsoleCtrlDelegate(uint ctrlType);

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        private const uint CTRL_CLOSE_EVENT = 2;

        public static bool Exists => GetConsoleWindow() != IntPtr.Zero;
        public static bool IsVisible => Exists && IsWindowVisible(GetConsoleWindow());

        /// <summary>
        /// 创建并显示控制台（首次调用时创建，之后仅显示）
        /// </summary>
        public static void Show()
        {
            if (!Exists)
            {
                AllocConsole();
                DisableCloseButton(); // 拦截关闭按钮，防止误关退出进程
            }
            ShowWindow(GetConsoleWindow(), SW_SHOW);
        }

        public static void Hide()
        {
            if (Exists)
                ShowWindow(GetConsoleWindow(), SW_HIDE);
        }

        public static void Toggle()
        {
            if (IsVisible) Hide(); else Show();
        }

        /// <summary>
        /// 彻底销毁控制台（之后 Serilog.Console 输出失效，需重新 Show 才能恢复）
        /// </summary>
        public static void Destroy()
        {
            if (Exists) FreeConsole();
        }

        private static void DisableCloseButton()
        {
            SetConsoleCtrlHandler(type =>
            {
                if (type == CTRL_CLOSE_EVENT)
                {
                    Hide();
                    return true;
                }
                return false;
            }, add: true);
        }
    }
}
