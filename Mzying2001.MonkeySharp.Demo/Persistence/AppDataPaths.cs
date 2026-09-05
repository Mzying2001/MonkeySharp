using System;
using System.IO;

namespace Mzying2001.MonkeySharp.Demo.Persistence
{
    public sealed class AppDataPaths
    {
        public AppDataPaths(string baseDirectory)
        {
            BaseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
            DataDirectory = Path.Combine(BaseDirectory, "Data");
            BrowserCacheDirectory = Path.Combine(DataDirectory, "BrowserCache");
            UserScriptsDirectory = Path.Combine(DataDirectory, "UserScripts");
            DependenciesDirectory = Path.Combine(DataDirectory, "Dependencies");
            DownloadsDirectory = Path.Combine(DataDirectory, "Downloads");
            LogsDirectory = Path.Combine(DataDirectory, "Logs");
            DatabasePath = Path.Combine(DataDirectory, "monkeysharp.db");
        }

        public string BaseDirectory { get; }
        public string DataDirectory { get; }
        public string BrowserCacheDirectory { get; }
        public string UserScriptsDirectory { get; }
        public string DependenciesDirectory { get; }
        public string DownloadsDirectory { get; }
        public string LogsDirectory { get; }
        public string DatabasePath { get; }

        public void EnsureCreated()
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(BrowserCacheDirectory);
            Directory.CreateDirectory(UserScriptsDirectory);
            Directory.CreateDirectory(DependenciesDirectory);
            Directory.CreateDirectory(DownloadsDirectory);
            Directory.CreateDirectory(LogsDirectory);
        }
    }
}
