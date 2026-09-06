using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace Mzying2001.MonkeySharp.Demo
{
    public partial class App : Application
    {
        public static Runtime.DemoRuntime Runtime { get; private set; }

        private async void ApplicationStartup(object sender, StartupEventArgs e)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var smoke = e.Args.Contains("--smoke");
            var checks = new Dictionary<string, bool>();
            var report = e.Args.SkipWhile(argument => argument != "--smoke-report").Skip(1).FirstOrDefault();
            try
            {
                var directory = smoke ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SmokeRuns", Guid.NewGuid().ToString("N")) : null;
                Runtime = await Mzying2001.MonkeySharp.Demo.Runtime.DemoRuntime.CreateAsync(Dispatcher, directory);
                MainWindow = new MainWindow(Runtime);
                MainWindow.Show();
                if (smoke)
                {
                    await Mzying2001.MonkeySharp.Demo.Runtime.DemoSmokeTest.RunAsync(Runtime, checks);
                    await Runtime.ShutdownAsync();
                    checks["finalDisposal"] = true;
                    if (report != null) File.WriteAllText(report, JsonSerializer.Serialize(new { success = true, checks, profile = directory }));
                    await ((MainWindow)MainWindow).CloseForSmokeAsync(0);
                }
                else Runtime.MainWindow.NewTab();
            }
            catch (System.Exception exception)
            {
                if (smoke)
                {
                    if (report != null) File.WriteAllText(report, JsonSerializer.Serialize(new { success = false, error = exception.ToString(), checks, profile = Runtime?.Paths.BaseDirectory }));
                    if (MainWindow is MainWindow window) await window.CloseForSmokeAsync(1);
                    else Shutdown(1);
                    return;
                }
                if (Runtime != null) await Runtime.ShutdownAsync();
                MessageBox.Show("启动失败。请确认程序目录可写、没有另一个实例占用 Data，并安装 x64 VC++ 运行库。\n\n" + exception,
                    "MonkeySharp Demo", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
        }
    }
}
