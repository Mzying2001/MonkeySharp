using Mzying2001.MonkeySharp.Demo.Runtime;
using System;
using System.IO;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Demo.Tests
{
    public sealed class SmokeReportTests
    {
        [Fact]
        public async Task FailureRetainsTheNamedStepAndOrderedConcurrentEvidence()
        {
            using (var fixture = await PersistenceTests.Fixture.Create())
            using (var run = new DemoSmokeRun(Path.Combine(fixture.Paths.BaseDirectory, "failure.json"), "navigation-menu"))
            {
                run.StartTrace();
                SmokeTrace.Step("cross-site callback exactly once");
                await Task.WhenAll(Enumerable.Range(0, 20).Select(index => Task.Run(() =>
                    SmokeTrace.Write("lifecycle", new { documentId = "document-" + index }))));
                SmokeTrace.Observe("clicks === 1", false);
                run.ReportFailure(new TimeoutException("callback timed out"), null);
                using (var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.Paths.BaseDirectory, "failure.json"))))
                {
                    Assert.Contains("cross-site callback exactly once", report.RootElement.GetProperty("context").GetString());
                    Assert.Contains("clicks === 1", report.RootElement.GetProperty("context").GetString());
                    Assert.Equal("navigation-menu", report.RootElement.GetProperty("smokeCase").GetString());
                    Assert.Equal(run.TracePath, report.RootElement.GetProperty("trace").GetString());
                }
                run.Dispose();
                var lines = File.ReadAllLines(run.TracePath);
                Assert.Equal(22, lines.Length);
                for (var index = 0; index < lines.Length; index++)
                    using (var record = JsonDocument.Parse(lines[index]))
                        Assert.Equal(index + 1, record.RootElement.GetProperty("sequence").GetInt32());
            }
        }

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
