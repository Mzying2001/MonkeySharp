using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Mzying2001.MonkeySharp.Demo.Persistence
{
    internal static class StorageFiles
    {
        internal static bool IsHash(string value) => value != null && Regex.IsMatch(value, "\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant);
        internal static bool Exists(string path)
        {
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) throw new IOException("Expected a file: " + path);
                return true;
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }
        internal static void CheckPath(string path)
        {
            var current = Path.GetFullPath(path);
            while (current != null)
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Reparse points are not allowed in script storage: " + current);
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                current = Path.GetDirectoryName(current);
            }
        }
        internal static void WriteNew(string path, byte[] bytes)
        {
            CheckPath(path);
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
        }
    }
}
