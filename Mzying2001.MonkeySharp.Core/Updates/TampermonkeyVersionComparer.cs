using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Mzying2001.MonkeySharp.Core.Updates
{
    /// <summary>Compares userscript versions using Tampermonkey-style numeric dotted segments.</summary>
    public sealed class TampermonkeyVersionComparer : IComparer<string>
    {
        private static readonly Regex NumericPrefix = new Regex(
            "^(?<version>[0-9]+(?:\\.[0-9]+)*)(?<suffix>.*)$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(100));

        /// <summary>Compares two version strings.</summary>
        public int Compare(string left, string right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;
            left = left.Trim();
            right = right.Trim();

            if (TryNumericVersion(left, out var leftParts) && TryNumericVersion(right, out var rightParts))
                return CompareParts(leftParts, rightParts);

            var leftMatch = NumericPrefix.Match(left);
            var rightMatch = NumericPrefix.Match(right);
            if (leftMatch.Success && rightMatch.Success &&
                CompareParts(ParseParts(leftMatch.Groups["version"].Value),
                    ParseParts(rightMatch.Groups["version"].Value)) == 0)
            {
                var leftSuffix = leftMatch.Groups["suffix"].Value;
                var rightSuffix = rightMatch.Groups["suffix"].Value;
                if (IsPrerelease(leftSuffix) && string.IsNullOrEmpty(rightSuffix)) return -1;
                if (IsPrerelease(rightSuffix) && string.IsNullOrEmpty(leftSuffix)) return 1;
            }

            var result = StringComparer.OrdinalIgnoreCase.Compare(left, right);
            return result != 0 ? result : StringComparer.Ordinal.Compare(left, right);
        }

        /// <summary>Compares two versions without constructing a comparer instance.</summary>
        public static int CompareVersions(string left, string right)
        {
            return new TampermonkeyVersionComparer().Compare(left, right);
        }

        private static bool IsPrerelease(string suffix)
        {
            return !string.IsNullOrEmpty(suffix) && suffix[0] == '-';
        }

        private static bool TryNumericVersion(string value, out IReadOnlyList<BigInteger> parts)
        {
            if (value.Length == 0 || value.Any(character => !(character >= '0' && character <= '9') && character != '.'))
            {
                parts = null;
                return false;
            }
            var rawParts = value.Split('.');
            if (rawParts.Any(part => part.Length == 0))
            {
                parts = null;
                return false;
            }
            parts = ParseParts(rawParts);
            return true;
        }

        private static IReadOnlyList<BigInteger> ParseParts(string version)
        {
            return ParseParts(version.Split('.'));
        }

        private static IReadOnlyList<BigInteger> ParseParts(IEnumerable<string> values)
        {
            return values.Select(value => BigInteger.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture)).ToArray();
        }

        private static int CompareParts(IReadOnlyList<BigInteger> left, IReadOnlyList<BigInteger> right)
        {
            var count = Math.Max(left.Count, right.Count);
            for (var index = 0; index < count; index++)
            {
                var leftPart = index < left.Count ? left[index] : BigInteger.Zero;
                var rightPart = index < right.Count ? right[index] : BigInteger.Zero;
                var result = leftPart.CompareTo(rightPart);
                if (result != 0) return result;
            }
            return 0;
        }
    }
}
