using Mzying2001.MonkeySharp.Core.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace Mzying2001.MonkeySharp.Core.Security
{
    /// <summary>Verifies Tampermonkey-compatible integrity declarations.</summary>
    public static class ResourceIntegrityVerifier
    {
        /// <summary>Verifies the supplied bytes against the last supported declaration.</summary>
        /// <param name="bytes">The bytes returned by the resource provider.</param>
        /// <param name="integrity">The declarations in metadata order.</param>
        /// <param name="url">The URL used in an error message.</param>
        public static void Verify(
            byte[] bytes,
            IReadOnlyList<ResourceIntegrityDeclaration> integrity,
            string url = null)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            if (integrity == null || integrity.Count == 0)
                return;

            ResourceIntegrityDeclaration selected = null;
            byte[] expected = null;
            foreach (var declaration in integrity)
            {
                if (!declaration.IsSupported)
                    continue;
                if (!TryDecode(declaration.Algorithm, declaration.Digest, out var decoded))
                    throw new ResourceIntegrityException(
                        "MSR410_RESOURCE_INTEGRITY_FAILED",
                        "The integrity digest for '" + (url ?? "the resource") + "' is invalid.");
                selected = declaration;
                expected = decoded;
            }

            if (selected == null || expected == null)
                throw new ResourceIntegrityException(
                    "MSR410_RESOURCE_INTEGRITY_FAILED",
                    "The resource declares no supported integrity algorithm.");

            byte[] actual;
            using (var algorithm = CreateAlgorithm(selected.Algorithm))
                actual = algorithm.ComputeHash(bytes);
            if (!FixedTimeEquals(actual, expected))
                throw new ResourceIntegrityException(
                    "MSR410_RESOURCE_INTEGRITY_FAILED",
                    "The integrity digest for '" + (url ?? "the resource") + "' does not match.");
        }

        /// <summary>Attempts to decode a supported digest in hexadecimal or Base64 form.</summary>
        public static bool TryDecode(string algorithm, string encoded, out byte[] digest)
        {
            digest = null;
            if (string.IsNullOrWhiteSpace(algorithm) || string.IsNullOrWhiteSpace(encoded))
                return false;
            var normalized = algorithm.Trim().ToLowerInvariant();
            var expectedLength = normalized == "md5" ? 16 : normalized == "sha256" ? 32 : 0;
            if (expectedLength == 0)
                return false;

            if (encoded.Length == expectedLength * 2 && TryDecodeHex(encoded, out digest))
                return true;
            if (encoded.Any(char.IsWhiteSpace) || encoded.Length % 4 == 1)
                return false;
            try
            {
                var padded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
                var decoded = Convert.FromBase64String(padded);
                if (decoded.Length != expectedLength)
                    return false;
                digest = decoded;
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static HashAlgorithm CreateAlgorithm(string algorithm)
        {
            return string.Equals(algorithm, "md5", StringComparison.OrdinalIgnoreCase)
                ? (HashAlgorithm)MD5.Create()
                : SHA256.Create();
        }

        private static bool TryDecodeHex(string text, out byte[] bytes)
        {
            bytes = new byte[text.Length / 2];
            for (var index = 0; index < text.Length; index += 2)
            {
                var high = Hex(text[index]);
                var low = Hex(text[index + 1]);
                if (high < 0 || low < 0)
                {
                    bytes = null;
                    return false;
                }
                bytes[index / 2] = (byte)((high << 4) | low);
            }
            return true;
        }

        private static int Hex(char value)
        {
            if (value >= '0' && value <= '9') return value - '0';
            if (value >= 'a' && value <= 'f') return value - 'a' + 10;
            if (value >= 'A' && value <= 'F') return value - 'A' + 10;
            return -1;
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            var difference = 0;
            for (var index = 0; index < left.Length; index++)
                difference |= left[index] ^ right[index];
            return difference == 0;
        }
    }

    /// <summary>Indicates that an external userscript resource failed integrity validation.</summary>
    public sealed class ResourceIntegrityException : UserScriptException
    {
        /// <summary>Initializes an integrity failure.</summary>
        public ResourceIntegrityException(string code, string message) : base(message)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
        }

        /// <summary>Gets the stable diagnostic code.</summary>
        public string Code { get; }
    }
}
