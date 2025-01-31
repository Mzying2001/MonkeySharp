using System;
using System.Text.RegularExpressions;

namespace Mzying2001.MonkeySharp.Core.Internal
{
    internal static class Matcher
    {
        /// <summary>
        /// Match the pattern with the text.
        /// </summary>
        public static bool Match(string pattern, string text)
        {
            if (pattern == null || text == null)
            {
                return false;
            }

            int patternLength = pattern.Length;
            int textLength = text.Length;

            bool[,] dp = new bool[patternLength + 1, textLength + 1];
            dp[0, 0] = true;

            for (int i = 1; i <= patternLength; i++)
            {
                if (pattern[i - 1] == '*')
                {
                    dp[i, 0] = dp[i - 1, 0];
                }
            }

            for (int i = 1; i <= patternLength; i++)
            {
                for (int j = 1; j <= textLength; j++)
                {
                    if (pattern[i - 1] == '*')
                    {
                        dp[i, j] = dp[i - 1, j] || dp[i, j - 1];
                    }
                    else if (pattern[i - 1] == '?' || pattern[i - 1] == text[j - 1])
                    {
                        dp[i, j] = dp[i - 1, j - 1];
                    }
                }
            }

            return dp[patternLength, textLength];
        }


        /// <summary>
        /// Match the URL pattern with the URL text. Currently same as <see cref="Match"/>.
        /// </summary>
        public static bool UrlMatch(string pattern, string text)
        {
            return Match(pattern, text);
        }


        ///// <summary>
        ///// Match the URL pattern with the URL text.<br/>
        ///// https://developer.chrome.com/docs/extensions/develop/concepts/match-patterns
        ///// </summary>
        //public static bool UrlMatch(string pattern, string text)
        //{
        //    // Handle the special case for "<all_urls>"
        //    if (pattern == "<all_urls>")
        //    {
        //        Uri uri;
        //        if (!Uri.TryCreate(text, UriKind.Absolute, out uri))
        //            return false;
        //        string scheme = uri.Scheme.ToLower();
        //        return scheme == "http" || scheme == "https" || scheme == "file";
        //    }

        //    // Split pattern into scheme and the rest
        //    string[] schemeSplit = pattern.Split(new[] { "://" }, 2, StringSplitOptions.None);
        //    if (schemeSplit.Length != 2)
        //        return false;

        //    string patternScheme = schemeSplit[0].ToLower();
        //    string afterScheme = schemeSplit[1];

        //    // Validate pattern scheme
        //    if (patternScheme != "http" && patternScheme != "https" && patternScheme != "*" && patternScheme != "file")
        //        return false;

        //    // Split host and path from the remaining part
        //    int firstSlashIndex = afterScheme.IndexOf('/');
        //    if (firstSlashIndex == -1)
        //        return false;

        //    string patternHost = afterScheme.Substring(0, firstSlashIndex);
        //    string patternPath = afterScheme.Substring(firstSlashIndex);

        //    // Validate host pattern
        //    if (!IsValidHostPattern(patternHost))
        //        return false;

        //    // Parse the text URL
        //    Uri textUri;
        //    if (!Uri.TryCreate(text, UriKind.Absolute, out textUri))
        //        return false;

        //    string textScheme = textUri.Scheme.ToLower();
        //    string textHost = textUri.Host;
        //    string textPath = textUri.AbsolutePath;

        //    // Handle file scheme host (empty string)
        //    if (textUri.Scheme == "file")
        //        textHost = "";

        //    // Scheme matching
        //    if (patternScheme == "*")
        //    {
        //        if (textScheme != "http" && textScheme != "https")
        //            return false;
        //    }
        //    else
        //    {
        //        if (patternScheme != textScheme)
        //            return false;
        //    }

        //    // Host matching
        //    if (!MatchHost(patternHost, textHost))
        //        return false;

        //    // Path matching
        //    if (!MatchPath(patternPath, textPath))
        //        return false;

        //    return true;
        //}


        ///// <summary>
        ///// Validates whether the host pattern conforms to the rules (correct position of the wildcard).
        ///// </summary>
        //private static bool IsValidHostPattern(string host)
        //{
        //    if (host == "*")
        //        return true;

        //    if (host.StartsWith("*."))
        //    {
        //        string remaining = host.Substring(2);
        //        return !remaining.Contains("*");
        //    }

        //    return !host.Contains("*");
        //}


        ///// <summary>
        ///// Matches the hostname based on the pattern, handling wildcards and exact matches.
        ///// </summary>
        //private static bool MatchHost(string patternHost, string textHost)
        //{
        //    if (patternHost == "*")
        //        return true;

        //    if (patternHost.StartsWith("*."))
        //    {
        //        string remainingHost = patternHost.Substring(2);
        //        string escaped = Regex.Escape(remainingHost).Replace("\\.", "\\.");
        //        string regexPattern = "^([a-z0-9-]+\\.)+" + escaped + "$";
        //        return Regex.IsMatch(textHost, regexPattern, RegexOptions.IgnoreCase);
        //    }

        //    return string.Equals(patternHost, textHost, StringComparison.OrdinalIgnoreCase);
        //}


        ///// <summary>
        ///// Converts the path pattern into a regular expression for matching.
        ///// </summary>
        //private static bool MatchPath(string patternPath, string textPath)
        //{
        //    string regexPattern = Regex.Escape(patternPath).Replace("\\*", ".*");
        //    regexPattern = "^" + regexPattern + "$";
        //    return Regex.IsMatch(textPath, regexPattern);
        //}
    }
}
