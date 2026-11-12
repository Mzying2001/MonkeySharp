using Mzying2001.MonkeySharp.Demo.Runtime;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Demo.Tests
{
    public sealed class SmokeReportTests
    {
        [Fact]
        public async Task StartupFailureProducesTheExistingMachineReadableReport()
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            {
                var report = Path.Combine(fixture.Paths.BaseDirectory, "failure.json");
                var run = new DemoSmokeRun(report);
                run.ReportFailure(new InvalidOperationException("startup failed"), null);

                using (var document = JsonDocument.Parse(File.ReadAllText(report)))
                {
                    var root = document.RootElement;
                    Assert.False(root.GetProperty("success").GetBoolean());
                    Assert.Contains("startup failed", root.GetProperty("error").GetString());
                    Assert.Equal(JsonValueKind.Object, root.GetProperty("checks").ValueKind);
                    Assert.Equal(JsonValueKind.Null, root.GetProperty("profile").ValueKind);
                }

                Assert.Equal(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SmokeRuns"),
                    Path.GetDirectoryName(run.ProfileDirectory));
                Assert.NotEqual(run.ProfileDirectory, new DemoSmokeRun(null).ProfileDirectory);
            }
        }
    }
}
