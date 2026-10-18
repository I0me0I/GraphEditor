using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GraphEditor
{
    /// <summary>
    /// Windows 11 的窗口外观（DWM 属性）：圆角、边框颜色、深色标题栏。
    /// 全部是「有则生效、没有就静默跳过」的调用，所以在 Windows 10 及更早系统上不影响运行。
    /// </summary>
    internal static class WindowChrome
    {
        // DWMWINDOWATTRIBUTE
        private const int DwmwaUseImmersiveDarkMode = 20;      // Win10 2004+
        private const int DwmwaWindowCornerPreference = 33;    // Win11
        private const int DwmwaBorderColor = 34;               // Win11

        private const int DwmwcpRound = 2;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private static bool _failedOnce;

        /// <summary>
        /// 应用现代窗口外观：Win11 圆角 + 与界面一致的细边框。
        /// </summary>
        public static void Apply(Form form)
        {
            if (form == null || form.IsDisposed) return;
            if (_failedOnce) return;

            try
            {
                IntPtr hwnd = form.Handle;
                if (hwnd == IntPtr.Zero) return;

                int round = DwmwcpRound;
                DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref round, sizeof(int));

                // 边框颜色：比纯黑柔和的浅灰蓝，和工具栏/画布留白更协调。
                // COLORREF 是 0x00BBGGRR，需要按 BGR 组装。
                Color border = Color.FromArgb(0xC8, 0xCF, 0xD8);
                int colorRef = border.R | (border.G << 8) | (border.B << 16);
                DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref colorRef, sizeof(int));

                // 界面本身仍是浅色，所以标题栏固定用浅色：
                // 若跟随系统深色主题，会出现「深色标题栏 + 浅色内容」的割裂感。
                int dark = 0;
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
            }
            catch (DllNotFoundException)
            {
                _failedOnce = true;
            }
            catch (EntryPointNotFoundException)
            {
                _failedOnce = true;
            }
        }

        /// <summary>读取系统「应用使用浅色/深色主题」的设置。</summary>
        public static bool IsSystemDarkTheme()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key == null) return false;
                    object value = key.GetValue("AppsUseLightTheme");
                    if (value is int) return (int)value == 0;
                }
            }
            catch (Exception)
            {
                // 读不到就当作浅色
            }
            return false;
        }
    }
}
