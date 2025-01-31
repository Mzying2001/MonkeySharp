using Mzying2001.MonkeySharp.Core.Internal;
using System.IO;

namespace Mzying2001.MonkeySharp.Core.Script
{
    /// <summary>
    /// Javascript script.
    /// </summary>
    public class JScript
    {
        /// <summary>
        /// The text of the script.
        /// </summary>
        public string ScriptText { get; }


        /// <summary>
        /// Info of the script.
        /// </summary>
        public JScriptInfo Info { get; }


        /// <summary>
        /// Constructor.
        /// </summary>
        private JScript(string script)
        {
            ScriptText = script;
            Info = JScriptInfo.Parse(script);
        }


        /// <summary>
        /// Check if the script should be run on the url.
        /// </summary>
        public bool MatchUrl(string url)
        {
            ThrowHelper.ThrowIfArgumentNull(url, nameof(url));

            foreach (var item in Info.Exclude)
            {
                if (Matcher.Match(item, url))
                    return false;
            }
            foreach (var item in Info.Include)
            {
                if (Matcher.Match(item, url))
                    return true;
            }
            foreach (var item in Info.Match)
            {
                if (Matcher.UrlMatch(item, url))
                    return true;
            }
            return false;
        }


        /// <summary>
        /// Load script from file.
        /// </summary>
        public static JScript LoadFromFile(string fileName)
        {
            ThrowHelper.ThrowIfArgumentNull(fileName, nameof(fileName));
            ThrowHelper.ThrowIfFileNotFound(fileName);
            return new JScript(File.ReadAllText(fileName));
        }


        /// <summary>
        /// Load script from string.
        /// </summary>
        public static JScript LoadFromString(string script)
        {
            ThrowHelper.ThrowIfArgumentNull(script, nameof(script));
            return new JScript(script);
        }
    }
}
