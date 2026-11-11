using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CleanSweep.Services;

namespace CleanSweep
{
    public partial class App : Application
    {
        public App()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args != null && e.Args.Length > 0 &&
                string.Equals(e.Args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Task.Run(() =>
                {
                    int code = SelfTest.Run();
                    Dispatcher.Invoke(() => Shutdown(code));
                });
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            WriteCrash(e.Exception);
            MessageBox.Show("程序遇到一个未处理的问题：\n\n" + e.Exception.Message +
                            "\n\n错误记录已写入数据目录。", "净界 CleanSweep",
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            WriteCrash(e.ExceptionObject as Exception);
        }

        private static void WriteCrash(Exception ex)
        {
            try
            {
                AppPaths.Ensure();
                string file = Path.Combine(AppPaths.LogsDir,
                    "crash-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                File.WriteAllText(file,
                    DateTime.Now.ToString("O") + Environment.NewLine +
                    (ex == null ? "Unknown error" : ex.ToString()));
            }
            catch
            {
                // Never let crash reporting hide the original failure.
            }
        }
    }
}
