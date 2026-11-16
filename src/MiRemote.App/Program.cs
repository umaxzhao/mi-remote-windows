using System;
using System.Threading;
using System.Windows;

namespace MiRemote.App
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test")
            {
                Environment.ExitCode = MiRemote.Core.SelfTest.Run() ? 0 : 1;
                return;
            }

            using var instance = new Mutex(true, @"Local\MiRemote.Windows", out bool created);
            if (!created)
            {
                System.Windows.MessageBox.Show("MiRemote 已在运行，请从系统托盘打开设置。", "MiRemote");
                return;
            }
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var services = new AppServices();
            try
            {
                services.Start();
                services.ShowMainWindow();
                app.Run();
            }
            finally
            {
                services.Stop();
                instance.ReleaseMutex();
            }
        }
    }
}
