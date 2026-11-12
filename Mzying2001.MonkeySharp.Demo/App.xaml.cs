using System;
using Mzying2001.MonkeySharp.Demo.Localization;
using Mzying2001.MonkeySharp.Demo.Runtime;
using System.Linq;
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
            var report = e.Args.SkipWhile(argument => argument != "--smoke-report").Skip(1).FirstOrDefault();
            var smokeRun = smoke ? new DemoSmokeRun(report) : null;
            UiLanguage.Apply(UiLanguage.DefaultLanguage);
            try
            {
                UiLanguage.Apply(UiLanguage.Resolve(e.Args, smokeRun?.ProfileDirectory ?? AppDomain.CurrentDomain.BaseDirectory));
                Runtime = await DemoRuntime.CreateAsync(Dispatcher, smokeRun?.ProfileDirectory);
                MainWindow = new MainWindow(Runtime);
                MainWindow.Show();
                if (smoke)
                {
                    await smokeRun.RunAsync(Runtime);
                    await ((MainWindow)MainWindow).CloseForSmokeAsync(0);
                }
                else Runtime.MainWindow.NewTab();
            }
            catch (Exception exception)
            {
                if (smoke)
                {
                    smokeRun.ReportFailure(exception, Runtime);
                    if (MainWindow is MainWindow window) await window.CloseForSmokeAsync(1);
                    else Shutdown(1);
                    return;
                }
                if (Runtime != null) await Runtime.ShutdownAsync();
                MessageBox.Show(Strings.Format("StartupFailed", exception),
                    "MonkeySharp Demo", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }
    }
}
