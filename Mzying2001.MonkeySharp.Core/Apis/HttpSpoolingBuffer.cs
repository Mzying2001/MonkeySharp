using System;
using System.IO;

namespace Mzying2001.MonkeySharp.Core.Apis
{
    internal sealed class HttpSpoolingBuffer : IUserScriptHttpBody, IDisposable
    {
        internal const int MemoryThreshold = 256 * 1024;
        internal const int BridgeChunkSize = 64 * 1024;

        private readonly object _sync = new object();
        private MemoryStream _memory = new MemoryStream();
        private FileStream _file;
        private string _path;
        private bool _disposed;

        public long Length
        {
            get
            {
                lock (_sync)
                {
                    ThrowIfDisposed();
                    return ActiveStream.Length;
                }
            }
        }

        internal bool IsFileBacked
        {
            get { lock (_sync) { ThrowIfDisposed(); return _file != null; } }
        }

        internal string TemporaryPath
        {
            get { lock (_sync) { ThrowIfDisposed(); return _path; } }
        }

        public void Append(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
                throw new ArgumentOutOfRangeException(nameof(offset));
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_file == null && _memory.Length + count > MemoryThreshold)
                    SpillToFile();
                ActiveStream.Position = ActiveStream.Length;
                ActiveStream.Write(buffer, offset, count);
                ActiveStream.Flush();
            }
        }

        public byte[] Read(long offset, int count)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || count > BridgeChunkSize) throw new ArgumentOutOfRangeException(nameof(count));
            lock (_sync)
            {
                ThrowIfDisposed();
                var stream = ActiveStream;
                if (offset >= stream.Length) return new byte[0];
                stream.Position = offset;
                var result = new byte[Math.Min(count, (int)Math.Min(int.MaxValue, stream.Length - offset))];
                var read = stream.Read(result, 0, result.Length);
                if (read == result.Length) return result;
                Array.Resize(ref result, read);
                return result;
            }
        }

        public Stream OpenRead()
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_file != null)
                {
                    _file.Flush();
                    return new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                }
                return new MemoryStream(_memory.ToArray(), false);
            }
        }

        public void Dispose()
        {
            string path;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _file?.Dispose();
                _memory?.Dispose();
                _file = null;
                _memory = null;
                path = _path;
                _path = null;
            }
            if (path != null)
            {
                try { File.Delete(path); }
                catch { }
            }
        }

        private Stream ActiveStream => (Stream)_file ?? _memory;

        private void SpillToFile()
        {
            _path = Path.GetTempFileName();
            _file = new FileStream(_path, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
            _memory.Position = 0;
            _memory.CopyTo(_file);
            _memory.Dispose();
            _memory = null;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HttpSpoolingBuffer));
        }
    }
}
