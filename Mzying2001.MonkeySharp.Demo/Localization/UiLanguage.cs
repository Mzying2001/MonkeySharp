using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Mzying2001.MonkeySharp.Demo.Localization
{
    internal static class UiLanguage
    {
        internal const string DefaultLanguage = "en";
        internal const string PreferenceFileName = "ui-language.txt";
        internal static CultureInfo CurrentCulture { get; private set; } = CultureInfo.GetCultureInfo(DefaultLanguage);

        internal static string Normalize(string language)
        {
            if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)) return "en";
            if (string.Equals(language, "zh-CN", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
            throw new ArgumentException("Supported UI languages: en, zh-CN.", nameof(language));
        }

        internal static string Resolve(IReadOnlyList<string> arguments, string baseDirectory)
        {
            string language = null;
            for (var index = 0; index < arguments.Count; index++)
            {
                if (arguments[index] != "--lang") continue;
                if (language != null || index + 1 == arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("Use --lang en or --lang zh-CN once.");
                language = Normalize(arguments[++index]);
            }
            if (language != null) return language;

            var preference = PreferencePath(baseDirectory);
            try
            {
                if (File.Exists(preference)) return Normalize(File.ReadAllText(preference).Trim());
            }
            catch (ArgumentException) { } // Ignore unsupported preferences from other versions.
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return DefaultLanguage;
        }

        internal static void Apply(string language)
        {
            var culture = CultureInfo.GetCultureInfo(Normalize(language));
            CurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        internal static void Save(string baseDirectory, string language)
        {
            language = Normalize(language);
            var preference = PreferencePath(baseDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(preference));
            File.WriteAllText(preference, language);
        }

        private static string PreferencePath(string baseDirectory)
            => Path.Combine(baseDirectory, "Data", PreferenceFileName);
    }
}
