using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Mzying2001.MonkeySharp.Demo.Runtime
{
    /// <summary>Opt-in, ordered test evidence outside the application's profile logs.</summary>
    internal sealed class SmokeTrace : IDisposable
    {
        private static SmokeTrace _current;
        private readonly object _sync = new object();
        private readonly Stopwatch _timer = Stopwatch.StartNew();
        private readonly StreamWriter _writer;
        private readonly SmokeTrace _previous;
        private long _sequence;
        private string _step;
        private string _observation;
        private bool _disposed;

        internal SmokeTrace(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            _writer = new StreamWriter(path, false) { AutoFlush = true };
            _previous = _current;
            _current = this;
        }

        internal static void Step(string name)
        {
            var current = _current;
            if (current == null) return;
            lock (current._sync) { current._step = name; current._observation = null; }
            Write("step", new { name });
        }

        internal static void Observe(string expression, object result)
        {
            var current = _current;
            if (current == null) return;
            lock (current._sync) current._observation = expression + " => " + JsonSerializer.Serialize(result);
            Write("evaluation", new { expression, result });
        }

        internal static string Describe()
        {
            var current = _current;
            if (current == null) return "No smoke trace active.";
            lock (current._sync) return "Step=" + current._step + "; last=" + current._observation;
        }

        internal static void Write(string type, object data)
        {
            var current = _current;
            if (current == null) return;
            lock (current._sync)
            {
                if (current._disposed) return;
                current._writer.WriteLine(JsonSerializer.Serialize(new
                {
                    sequence = ++current._sequence, elapsedMs = current._timer.ElapsedMilliseconds,
                    type, step = current._step, data
                }));
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                if (ReferenceEquals(_current, this)) _current = _previous;
                _writer.Dispose();
            }
        }
    }
}
