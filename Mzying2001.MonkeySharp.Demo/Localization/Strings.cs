using System;
using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace Mzying2001.MonkeySharp.Demo.Localization
{
    public static class Strings
    {
        internal static readonly ResourceManager Resources = new ResourceManager(
            "Mzying2001.MonkeySharp.Demo.Localization.Strings", typeof(Strings).Assembly);

        // CEF callbacks can carry an older ExecutionContext/UICulture onto the dispatcher.
        // Resource selection must follow the application language, not the calling thread.
        public static string Get(string key) => Get(key, UiLanguage.CurrentCulture);

        internal static string Get(string key, CultureInfo culture) => Resources.GetString(key, culture)
            ?? throw new ArgumentException("Unknown UI resource: " + key, nameof(key));

        public static string Format(string key, params object[] arguments)
            => string.Format(UiLanguage.CurrentCulture, Get(key), arguments);
    }

    // UI language is selected before any windows are created and changes on the next launch.
    [MarkupExtensionReturnType(typeof(string))]
    public sealed class TextExtension : MarkupExtension
    {
        public TextExtension(string key) { Key = key; }
        public string Key { get; }
        public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
    }
}
