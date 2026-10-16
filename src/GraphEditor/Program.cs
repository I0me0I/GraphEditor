using System;
using System.IO;
using System.Windows.Forms;

namespace GraphEditor
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                ShowCrash(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                ShowCrash(e.ExceptionObject as Exception);
            };

            // 命令行传入的第一个非开关参数视为要打开的 .graph 文件（用于双击关联文件）。
            string fileToOpen = null;
            if (args != null)
            {
                foreach (string argument in args)
                {
                    if (string.IsNullOrEmpty(argument)) continue;
                    if (argument[0] == '/' || argument[0] == '-') continue;
                    fileToOpen = argument;
                    break;
                }
            }

            MainForm form = new MainForm(fileToOpen);
            Application.Run(form);
        }

        private static void ShowCrash(Exception ex)
        {
            string message = ex == null ? "未知错误" : ex.Message;
            try
            {
                MessageBox.Show("GraphEditor 遇到错误：" + Environment.NewLine + message
                    + Environment.NewLine + Environment.NewLine
                    + (ex == null ? "" : ex.StackTrace),
                    "GraphEditor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
                // 记录失败时不再抛出新异常
            }
        }
    }
}
