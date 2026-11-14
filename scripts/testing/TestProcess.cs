using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MonkeySharp.Testing
{
    public sealed class ProcessResult
    {
        public int ProcessId { get; set; }
        public int ExitCode { get; set; }
        public bool TimedOut { get; set; }
        public long ElapsedMs { get; set; }
        public string Error { get; set; }
    }

    public static class TestProcess
    {
        // Windows CommandLineToArgvW quoting, including embedded quotes and trailing slashes.
        public static string Quote(string value)
        {
            var result = new StringBuilder("\"");
            var slashes = 0;
            foreach (var character in value)
            {
                if (character == '\\') { slashes++; continue; }
                result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
                result.Append(character);
                slashes = 0;
            }
            result.Append('\\', slashes * 2);
            return result.Append('"').ToString();
        }

        public static ProcessResult Run(string executable, string[] arguments, string directory,
            string stdout, string stderr, int timeoutSeconds)
        {
            var timer = Stopwatch.StartNew();
            var result = new ProcessResult { ExitCode = -1 };
            var quoted = Array.ConvertAll(arguments, Quote);
            using (var output = new FileStream(stdout, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            using (var error = new FileStream(stderr, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(executable, string.Join(" ", quoted))
                {
                    WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                if (string.Equals(Path.GetFileNameWithoutExtension(executable), "powershell", StringComparison.OrdinalIgnoreCase))
                {
                    // Process.Start bypasses PowerShell 7's native-command cleanup for WinPS.
                    // Let Windows PowerShell rebuild its own module paths instead of loading Core modules.
                    process.StartInfo.EnvironmentVariables.Remove("PSModulePath");
                }
                try
                {
                    process.Start();
                    result.ProcessId = process.Id;
                    var copies = new[] { process.StandardOutput.BaseStream.CopyToAsync(output), process.StandardError.BaseStream.CopyToAsync(error) };
                    while (!process.WaitForExit(1000) && timer.Elapsed.TotalSeconds < timeoutSeconds) { }
                    if (!process.HasExited)
                    {
                        result.TimedOut = true;
                        KillTree(process);
                    }
                    if (process.WaitForExit(10000)) result.ExitCode = process.ExitCode;
                    else result.Error = "The test process did not exit after termination.";
                    if (!Task.WaitAll(copies, 5000))
                        result.Error = "Redirected output did not close after process exit.";
                }
                catch (Exception failure)
                {
                    result.Error = failure.ToString();
                    if (result.ProcessId != 0) KillTree(process);
                }
                finally { result.ElapsedMs = timer.ElapsedMilliseconds; }
            }
            return result;
        }

        private static void KillTree(Process process)
        {
            if (process.HasExited) return;
            using (var killer = Process.Start(new ProcessStartInfo("taskkill.exe", "/PID " + process.Id + " /T /F")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            }))
            {
                var output = killer.StandardOutput.ReadToEndAsync();
                var error = killer.StandardError.ReadToEndAsync();
                if (!killer.WaitForExit(10000)) killer.Kill();
                Task.WaitAll(new Task[] { output, error }, 1000);
            }
        }
    }
}
