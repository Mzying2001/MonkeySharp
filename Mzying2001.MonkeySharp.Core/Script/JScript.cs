using Mzying2001.MonkeySharp.Core.Internal;
using System;
using System.IO;

namespace Mzying2001.MonkeySharp.Core.Script
{
    /// <summary>
    /// Javascript script.
    /// </summary>
    public class JScript
    {
        /// <summary>
        /// The ID of the script instance.
        /// </summary>
        public string ScriptId { get; }


        /// <summary>
        /// The text of the script.
        /// </summary>
        public string ScriptText { get; }


        /// <summary>
        /// The metadata of the script.
        /// </summary>
        public JScriptMeta Metadata { get; }


        /// <summary>
        /// Constructor.
        /// </summary>
        private JScript(string script)
        {
            ScriptText = script;
            Metadata = JScriptMeta.Parse(script);
            ScriptId = Guid.NewGuid().ToString();
        }


        /// <summary>
        /// Check if the script should be run on the url.
        /// </summary>
        public bool MatchUrl(string url)
        {
            ThrowHelper.ThrowIfArgumentNull(url, nameof(url));

            foreach (var item in Metadata.Exclude)
            {
                if (Matcher.Match(item, url))
                    return false;
            }
            foreach (var item in Metadata.Include)
            {
                if (Matcher.Match(item, url))
                    return true;
            }
            foreach (var item in Metadata.Match)
            {
                if (Matcher.UrlMatch(item, url))
                    return true;
            }
            return false;
        }


        /// <summary>
        /// Load script from file.
        /// </summary>
        /// <param name="fileName">The path of the script file.</param>
        /// <param name="verifier">The verifier to verify the script, or null to use the default verifier.</param>
        public static JScript LoadFromFile(string fileName, IScriptVerifier verifier = null)
        {
            ThrowHelper.ThrowIfFileNotFound(fileName);
            return LoadFromString(File.ReadAllText(fileName), verifier);
        }


        /// <summary>
        /// Load script from string.
        /// </summary>
        /// <param name="script">The script text.</param>
        /// <param name="verifier">The verifier to verify the script, or null to use the default verifier.</param>
        public static JScript LoadFromString(string script, IScriptVerifier verifier = null)
        {
            ThrowHelper.ThrowIfArgumentNull(script, nameof(script));

            string errmsg = null;
            verifier = verifier ?? DefScriptVerifier.Instance;

            if (!verifier.Verify(script, ref errmsg))
                throw new ArgumentException(errmsg, nameof(script));

            return new JScript(script);
        }
    }
}
