using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace GraphEditor.Tests
{
    /// <summary>
    /// 独立截图工具：启动 GraphEditor.exe，等待窗口出现后截图，然后关闭进程。
    ///   ScreenshotTool.exe &lt;exe路径&gt; &lt;输出png&gt; [等待毫秒]
    /// </summary>
    internal static class ScreenshotTool
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [STAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            if (args.Length < 2)
            {
                Console.WriteLine("用法: ScreenshotTool.exe <exe路径> <输出png> [等待毫秒]");
                return 2;
            }

            string exePath = Path.GetFullPath(args[0]);
            string outPath = Path.GetFullPath(args[1]);
            int waitMs = args.Length > 2 ? int.Parse(args[2]) : 2500;
            string extraArgument = args.Length > 3 ? args[3] : null;

            ProcessStartInfo startInfo = new ProcessStartInfo(exePath);
            startInfo.WorkingDirectory = Path.GetDirectoryName(exePath);
            if (!string.IsNullOrEmpty(extraArgument)) startInfo.Arguments = extraArgument;
            Process process = Process.Start(startInfo);
            try
            {
                IntPtr hwnd = IntPtr.Zero;
                StopwatchLike watch = new StopwatchLike();
                while (watch.ElapsedMs < waitMs + 15000)
                {
                    process.Refresh();
                    if (process.HasExited)
                    {
                        Console.WriteLine("进程已退出，退出码 " + process.ExitCode);
                        return 3;
                    }
                    IntPtr candidate = process.MainWindowHandle;
                    if (candidate != IntPtr.Zero && IsWindowVisible(candidate) && IsUsableWindow(candidate))
                    {
                        hwnd = candidate;
                        break;
                    }
                    Thread.Sleep(120);
                }

                if (hwnd == IntPtr.Zero)
                {
                    Console.WriteLine("未能获取主窗口句柄（可能是窗口尚未创建）");
                    return 4;
                }

                Thread.Sleep(waitMs);
                SetForegroundWindow(hwnd);
                Thread.Sleep(400);
                // 触发一次重绘
                SetForegroundWindow(hwnd);
                Thread.Sleep(300);

                RECT rect;
                if (!GetWindowRect(hwnd, out rect))
                {
                    Console.WriteLine("GetWindowRect 失败");
                    return 5;
                }
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                Console.WriteLine("窗口区域 " + width + "x" + height + " @ (" + rect.Left + "," + rect.Top + ")");

                using (Bitmap bmp = new Bitmap(width, height))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                    bmp.Save(outPath, ImageFormat.Png);
                    Console.WriteLine("截图已保存: " + outPath);
                }

                if (args.Length > 3 && args[3] == "measure")
                {
                    Console.WriteLine("将窗口置前 1 秒，请在此期间按 Ctrl+Shift+M 导出控件宽度……");
                    Thread.Sleep(1200);
                }
                return 0;
            }
            finally
            {
                try
                {
                    if (!process.HasExited) process.Kill();
                    process.WaitForExit(5000);
                }
                catch
                {
                    // 忽略关闭错误
                }
                process.Dispose();
            }
        }

        private static void MeasureWindow(IntPtr hwnd)
        {
            // 通过 Win32 无法拿到 ToolStripItem 宽度，改由子进程自己输出；
            // 这里仅作为后备，用窗口宽度作为参考。
            RECT rect;
            GetWindowRect(hwnd, out rect);
            Console.WriteLine("MEASURE 窗口宽度=" + (rect.Right - rect.Left));
        }

        private sealed class StopwatchLike
        {
            private readonly DateTime _start = DateTime.UtcNow;
            public int ElapsedMs { get { return (int)(DateTime.UtcNow - _start).TotalMilliseconds; } }
        }

        /// <summary>过滤掉 WinForms 创建过程中尺寸/位置异常的临时窗口。</summary>
        private static bool IsUsableWindow(IntPtr hwnd)
        {
            RECT rect;
            if (!GetWindowRect(hwnd, out rect)) return false;
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 400 || height < 300) return false;
            if (rect.Left <= -10000 || rect.Top <= -10000) return false;
            return true;
        }
    }
}
