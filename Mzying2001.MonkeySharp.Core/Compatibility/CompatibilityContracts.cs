using System;

namespace Mzying2001.MonkeySharp.Core.Compatibility
{
    /// <summary>Describes the JavaScript compatibility profile used for script execution.</summary>
    public enum UserScriptCompatibilityProfile
    {
        /// <summary>Provides legacy globals and non-strict wrapper semantics.</summary>
        LegacyCompatible,

        /// <summary>Provides only the modern Promise facade and strict wrapper semantics.</summary>
        ModernStrict
    }

    /// <summary>Configures compatibility behavior for an execution host.</summary>
    public sealed class UserScriptCompatibilityOptions
    {
        /// <summary>Gets or sets the compatibility profile.</summary>
        public UserScriptCompatibilityProfile Profile { get; set; } =
            UserScriptCompatibilityProfile.LegacyCompatible;

        /// <summary>Gets or sets whether legacy grant names are accepted by metadata parsing.</summary>
        public bool AcceptLegacyGrantAliases { get; set; } = true;

        /// <summary>Gets or sets whether legacy globals may be exposed in the legacy profile.</summary>
        public bool ExposeLegacyGlobals { get; set; } = true;

        /// <summary>Gets or sets whether the execution bootstrap may provide a synchronous storage mirror.</summary>
        public bool EnableSynchronousStorageMirror { get; set; } = true;

        /// <summary>Gets or sets whether the execution bootstrap may provide synchronous resource snapshots.</summary>
        public bool EnableSynchronousResourceSnapshot { get; set; } = true;

        /// <summary>Gets whether the selected profile requires a strict JavaScript wrapper.</summary>
        public bool Strict => Profile == UserScriptCompatibilityProfile.ModernStrict;

        /// <summary>Gets whether legacy globals are active for the selected profile.</summary>
        public bool LegacyGlobalsEnabled =>
            Profile == UserScriptCompatibilityProfile.LegacyCompatible && ExposeLegacyGlobals;
    }

    /// <summary>Configures metadata parser compatibility behavior.</summary>
    public sealed class UserScriptMetadataParserOptions
    {
        /// <summary>Gets or sets the metadata parsing profile.</summary>
        public UserScriptCompatibilityProfile Profile { get; set; } =
            UserScriptCompatibilityProfile.LegacyCompatible;

        /// <summary>Gets or sets whether legacy grant aliases are normalized.</summary>
        public bool AcceptLegacyGrantAliases { get; set; } = true;

        /// <summary>Gets or sets whether bounded preamble scanning is enabled.</summary>
        public bool AllowHeaderPreamble { get; set; } = true;

        /// <summary>Gets or sets the maximum number of lines scanned before a metadata header.</summary>
        public int MaxHeaderScanLines { get; set; } = 128;

        /// <summary>Gets or sets the maximum UTF-16 characters scanned before a metadata header.</summary>
        public int MaxHeaderScanCharacters { get; set; } = 64 * 1024;

        /// <summary>Gets whether strict header parsing is active.</summary>
        public bool Strict => Profile == UserScriptCompatibilityProfile.ModernStrict;

        /// <summary>Validates parser limits and profile-derived behavior.</summary>
        public void Validate()
        {
            if (MaxHeaderScanLines <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxHeaderScanLines));
            if (MaxHeaderScanCharacters <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxHeaderScanCharacters));
            if (Strict)
                AllowHeaderPreamble = false;
        }
    }
}
