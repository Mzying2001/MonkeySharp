using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;

namespace Mzying2001.MonkeySharp.Demo.ViewModels
{
    public sealed class DiagnosticsViewModel : ObservableObject
    {
        private readonly Dispatcher _dispatcher;
        private readonly string _log;
        private readonly object _sync = new object();
        public DiagnosticsViewModel(Dispatcher dispatcher, string directory)
        {
            _dispatcher = dispatcher;
            _log = Path.Combine(directory, "demo.log");
        }
        public ObservableCollection<string> Entries { get; } = new ObservableCollection<string>();
        public void Report(string message)
        {
            var line = DateTimeOffset.Now.ToString("HH:mm:ss") + "  " + message;
            lock (_sync)
            {
                try
                {
                    if (File.Exists(_log) && new FileInfo(_log).Length > 5 * 1024 * 1024)
                    {
                        var previous = _log + ".previous";
                        if (File.Exists(previous)) File.Delete(previous);
                        File.Move(_log, previous);
                    }
                    File.AppendAllText(_log, line + Environment.NewLine);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(new Action(() =>
            {
                Entries.Add(line);
                while (Entries.Count > 500) Entries.RemoveAt(0);
            }));
        }
    }
}
