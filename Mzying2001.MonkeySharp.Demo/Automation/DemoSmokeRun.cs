using System;
using System.Collections.Generic;
using System.IO;
using Mzying2001.MonkeySharp.Demo.Localization;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Demo.Runtime
{
    /// <summary>One isolated smoke profile and the report contract consumed by Run-E2E.ps1.</summary>
    internal sealed class DemoSmokeRun
    {
        private readonly string _reportPath;
        private readonly Dictionary<string, bool> _checks = new Dictionary<string, bool>();

        internal DemoSmokeRun(string reportPath)
        {
            _reportPath = reportPath;
            ProfileDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SmokeRuns", Guid.NewGuid().ToString("N"));
        }

        internal string ProfileDirectory { get; }

        internal async Task RunAsync(DemoRuntime runtime)
        {
            await DemoSmokeTest.RunAsync(runtime, _checks);
            var shutdown = runtime.ShutdownAsync();
            if (!ReferenceEquals(shutdown, runtime.ShutdownAsync()))
                throw new InvalidOperationException("Repeated shutdown did not await the same disposal.");
            await shutdown;
            _checks["finalDisposal"] = true;
            WriteReport(new
            {
                success = true,
                checks = _checks,
                profile = ProfileDirectory,
                environment = new
                {
                    cefSharpVersion = typeof(global::CefSharp.Cef).Assembly.GetName().Version.ToString(),
                    platform = Environment.Is64BitProcess ? "x64" : "x86",
                    uiLanguage = UiLanguage.CurrentCulture.Name
                }
            });
        }

        internal void ReportFailure(Exception exception, DemoRuntime runtime)
        {
            WriteReport(new
            {
                success = false,
                error = exception.ToString(),
                checks = _checks,
                profile = runtime?.Paths.BaseDirectory
            });
        }

        private void WriteReport(object report)
        {
            if (_reportPath != null) File.WriteAllText(_reportPath, JsonSerializer.Serialize(report));
        }
    }
}
