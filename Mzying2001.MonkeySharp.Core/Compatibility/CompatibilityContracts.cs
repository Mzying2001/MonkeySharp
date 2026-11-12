using System;

namespace Mzying2001.MonkeySharp.Core.Compatibility
{
    /// <summary>Bounds metadata scanning independently of API calling style.</summary>
    public sealed class UserScriptMetadataParserOptions
    {
        /// <summary>Gets or sets the maximum number of lines scanned before a metadata header.</summary>
        public int MaxHeaderScanLines { get; set; } = 128;

        /// <summary>Gets or sets the maximum UTF-16 characters scanned before a metadata header.</summary>
        public int MaxHeaderScanCharacters { get; set; } = 64 * 1024;

        /// <summary>Validates metadata scan bounds.</summary>
        public void Validate()
        {
            if (MaxHeaderScanLines <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxHeaderScanLines));
            if (MaxHeaderScanCharacters <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxHeaderScanCharacters));
        }
    }
}
