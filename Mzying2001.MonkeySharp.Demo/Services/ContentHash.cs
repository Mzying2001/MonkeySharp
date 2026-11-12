using System;
using System.Security.Cryptography;

namespace Mzying2001.MonkeySharp.Demo.Services
{
    internal static class ContentHash
    {
        internal static string Sha256(byte[] bytes)
        {
            using (var algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
