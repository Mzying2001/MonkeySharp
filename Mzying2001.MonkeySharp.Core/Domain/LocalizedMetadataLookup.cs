using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Mzying2001.MonkeySharp.Core.Domain
{
    internal static class LocalizedMetadataLookup
    {
        private static readonly StringComparer CultureNameComparer = StringComparer.OrdinalIgnoreCase;

        internal static string Find(string cultureName, IReadOnlyDictionary<string, string> translations)
        {
            if (translations.Count == 0 || string.IsNullOrWhiteSpace(cultureName))
                return null;

            CultureInfo culture;
            try
            {
                culture = CultureInfo.GetCultureInfo(cultureName.Trim());
            }
            catch (CultureNotFoundException)
            {
                return null;
            }

            var language = GetPrimaryLanguage(culture.Name);
            var requestedChain = GetCultureChain(culture, language).ToList();
            foreach (var name in requestedChain)
            {
                if (translations.TryGetValue(name, out var text) && !string.IsNullOrWhiteSpace(text))
                    return text;
            }

            var candidates = translations
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Value) &&
                    CultureNameComparer.Equals(GetPrimaryLanguage(entry.Key), language))
                .Select(entry => new
                {
                    entry.Key,
                    entry.Value,
                    Chain = GetCultureChain(CultureInfo.GetCultureInfo(entry.Key), language).ToList(),
                })
                .ToList();

            foreach (var ancestor in requestedChain)
            {
                var match = candidates
                    .Select(candidate => new
                    {
                        candidate.Key,
                        candidate.Value,
                        Distance = candidate.Chain.FindIndex(name => CultureNameComparer.Equals(name, ancestor)),
                    })
                    .Where(candidate => candidate.Distance >= 0)
                    .OrderBy(candidate => candidate.Distance)
                    .ThenBy(candidate => candidate.Key, CultureNameComparer)
                    .FirstOrDefault();
                if (match != null)
                    return match.Value;
            }

            return null;
        }

        private static IEnumerable<string> GetCultureChain(CultureInfo culture, string language)
        {
            for (; culture.Name.Length != 0; culture = culture.Parent)
            {
                var name = culture.Name;
                if (!CultureNameComparer.Equals(GetPrimaryLanguage(name), language))
                    continue;

                yield return name;
                if (CultureNameComparer.Equals(name, language))
                    yield break;
            }

            yield return language;
        }

        private static string GetPrimaryLanguage(string cultureName)
        {
            var separator = cultureName.IndexOf('-');
            return separator >= 0 ? cultureName.Substring(0, separator) : cultureName;
        }
    }
}
