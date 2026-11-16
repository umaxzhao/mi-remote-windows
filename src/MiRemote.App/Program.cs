using System;
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
                bool ok = MiRemote.Core.SelfTest.Run();
                Environment.Exit(ok ? 0 : 1);
                return;
            }

            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var services = new AppServices();
            services.Start();
            // 显示主窗口
            services.ShowMainWindow();
            app.Run();
            services.Stop();
        }
    }
}
