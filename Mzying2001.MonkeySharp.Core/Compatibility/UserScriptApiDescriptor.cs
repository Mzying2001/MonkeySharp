namespace Mzying2001.MonkeySharp.Core.Compatibility
{
    /// <summary>Describes official names, return categories and the modern authorization family.</summary>
    public sealed class UserScriptApiDescriptor
    {
        internal UserScriptApiDescriptor(string capability, string legacyName, string modernReturn, string legacyReturn, string family)
        { Capability = capability; LegacyName = legacyName; ModernReturn = modernReturn; LegacyReturn = legacyReturn; Family = family; }
        /// <summary>Gets the internal operation and modern grant name.</summary>
        public string Capability { get; }
        /// <summary>Gets the official legacy name, when one exists.</summary>
        public string LegacyName { get; }
        /// <summary>Gets the modern immediate return category.</summary>
        public string ModernReturn { get; }
        /// <summary>Gets the legacy immediate return category.</summary>
        public string LegacyReturn { get; }
        /// <summary>Gets the modern capability family established by reference evidence.</summary>
        public string Family { get; }
    }
}
