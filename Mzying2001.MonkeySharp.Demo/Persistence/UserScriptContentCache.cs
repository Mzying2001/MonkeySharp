using Mzying2001.MonkeySharp.Demo.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mzying2001.MonkeySharp.Demo.Persistence
{
    internal sealed class UserScriptContentCache
    {
        private readonly string _objects, _temporary;
        private readonly Action<string> _report;
        internal UserScriptContentCache(string root, Action<string> report)
        { _objects = Path.Combine(root, "objects"); _temporary = Path.Combine(root, "tmp"); _report = report; }
        internal byte[] Read(string hash)
        {
            var path = ObjectPath(hash);
            StorageFiles.CheckPath(path);
            var bytes = UserScriptSourceFile.ReadBytes(path);
            if (ContentHash.Sha256(bytes) != hash) throw new IOException("Cached object hash mismatch: " + hash);
            return bytes;
        }
        internal string Write(byte[] bytes)
        {
            if (bytes.Length > UserScriptSourceFile.MaximumBytes) throw new IOException("Asset exceeds the 10 MiB limit.");
            var hash = ContentHash.Sha256(bytes); var path = ObjectPath(hash);
            StorageFiles.CheckPath(path); StorageFiles.CheckPath(_temporary);
            Directory.CreateDirectory(_objects); Directory.CreateDirectory(_temporary);
            if (StorageFiles.Exists(path))
            {
                try { Read(hash); return hash; }
                catch (IOException) { } // A freshly verified copy may replace a corrupt object of the same hash.
            }
            var temporary = Path.Combine(_temporary, Guid.NewGuid().ToString("N") + ".pending");
            try
            {
                StorageFiles.WriteNew(temporary, bytes);
                if (ContentHash.Sha256(UserScriptSourceFile.ReadBytes(temporary)) != hash) throw new IOException("Object staging verification failed.");
                if (StorageFiles.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { Cleanup(temporary); }
            return hash;
        }
        internal void Collect(ISet<string> referenced)
        {
            StorageFiles.CheckPath(_objects);
            if (Directory.Exists(_objects))
                foreach (var path in Directory.EnumerateFiles(_objects))
                    if (StorageFiles.IsHash(Path.GetFileName(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 && !referenced.Contains(Path.GetFileName(path))) Cleanup(path);
            StorageFiles.CheckPath(_temporary);
            if (Directory.Exists(_temporary))
                foreach (var path in Directory.EnumerateFiles(_temporary, "*.pending"))
                    if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) Cleanup(path);
        }
        private string ObjectPath(string hash)
        {
            if (!StorageFiles.IsHash(hash)) throw new ArgumentException("Invalid content hash.", nameof(hash));
            return Path.Combine(_objects, hash);
        }
        private void Cleanup(string path)
        {
            try { if (StorageFiles.Exists(path)) File.Delete(path); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { _report("Object cleanup pending: " + error.Message); }
        }
    }
}
