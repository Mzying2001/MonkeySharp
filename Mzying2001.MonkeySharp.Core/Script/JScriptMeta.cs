using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Mzying2001.MonkeySharp.Core.Script
{
    /// <summary>
    /// Metadata of the script.
    /// </summary>
    public class JScriptMeta : IReadOnlyDictionary<string, string[]>
    {
        /// <summary>
        /// Internal data.
        /// </summary>
        private readonly Dictionary<string, List<string>> _data;


        /// <inheritdoc/>
        public string[] this[string key] =>
            _data.ContainsKey(key) ? _data[key].ToArray() : throw new KeyNotFoundException();


        /// <inheritdoc/>
        public IEnumerable<string> Keys => _data.Keys;


        /// <inheritdoc/>
        public IEnumerable<string[]> Values => _data.Values.Select(v => v.ToArray());


        /// <inheritdoc/>
        public int Count => _data.Count;


        /// <summary>
        /// Name of the script.
        /// </summary>
        public string Name => GetStringOrEmpty("name");


        /// <summary>
        /// Namespace of the script.
        /// </summary>
        public string Namespace => GetStringOrEmpty("namespace");


        /// <summary>
        /// Version of the script.
        /// </summary>
        public string Version => GetStringOrEmpty("version");


        /// <summary>
        /// Description of the script.
        /// </summary>
        public string Description => GetStringOrEmpty("description");


        /// <summary>
        /// Author of the script.
        /// </summary>
        public string Author => GetStringOrEmpty("author");


        /// <summary>
        /// Icon of the script.
        /// </summary>
        public string Icon => GetStringOrEmpty("icon");


        /// <summary>
        /// Specify which page the script should run.
        /// </summary>
        public string[] Match => GetStringArrayOrEmpty("match");


        /// <summary>
        /// Specify which page the script should run.
        /// </summary>
        public string[] Include => GetStringArrayOrEmpty("include");


        /// <summary>
        /// Specify which page the script should not run.
        /// </summary>
        public string[] Exclude => GetStringArrayOrEmpty("exclude");


        /// <summary>
        /// Permission required by the script.
        /// </summary>
        public string[] Grant => GetStringArrayOrEmpty("grant");


        /// <summary>
        /// License of the script.
        /// </summary>
        public string License => GetStringOrEmpty("license");


        /// <summary>
        /// Specify when the script should run.
        /// </summary>
        public JScriptRunAt RunAt => ParseRunAt(GetStringOrEmpty("run-at"));


        /// <summary>
        /// Constructor.
        /// </summary>
        private JScriptMeta(Dictionary<string, List<string>> data)
        {
            _data = data;
        }


        /// <inheritdoc/>
        public bool ContainsKey(string key)
        {
            return _data.ContainsKey(key);
        }


        /// <inheritdoc/>
        public IEnumerator<KeyValuePair<string, string[]>> GetEnumerator()
        {
            foreach (var pair in _data)
                yield return new KeyValuePair<string, string[]>(pair.Key, pair.Value.ToArray());
        }


        /// <inheritdoc/>
        public bool TryGetValue(string key, out string[] value)
        {
            List<string> values;
            _data.TryGetValue(key, out values);
            value = values?.ToArray();
            return values != null;
        }


        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }


        /// <summary>
        /// Get the metadata of the script.
        /// </summary>
        public static JScriptMeta Parse(string script)
        {
            Dictionary<string, List<string>> data
                = new Dictionary<string, List<string>>();

            bool flag = false; // true if the line is in the UserScript block
            foreach (var item in script.Split('\n'))
            {
                string line = item.TrimStart();

                if (!line.StartsWith("//"))
                    continue;

                line = line.Substring(2).Trim();

                if (line == "==UserScript==")
                {
                    flag = true;
                    continue;
                }

                if (line == "==/UserScript==")
                    break;

                if (line.Length < 2 || line[0] != '@')
                    continue;

                if (flag)
                {
                    int index = 1;
                    for (; index < line.Length; index++)
                    {
                        if (char.IsWhiteSpace(line[index]))
                            break;
                    }

                    string key = line.Substring(1, index - 1);
                    string value = line.Substring(index).TrimStart();

                    if (!data.ContainsKey(key))
                        data[key] = new List<string>();
                    data[key].Add(value);
                }
            }

            return new JScriptMeta(data);
        }


        /// <summary>
        /// Get the string value of the key or empty string.
        /// </summary>
        private string GetStringOrEmpty(string key)
        {
            _data.TryGetValue(key, out List<string> values);
            return values == null || values.Count == 0 ? string.Empty : values[0];
        }


        /// <summary>
        /// Get the string array value of the key or empty array.
        /// </summary>
        private string[] GetStringArrayOrEmpty(string key)
        {
            TryGetValue(key, out string[] values);
            return values ?? Array.Empty<string>();
        }


        /// <summary>
        /// Parse the run-at value.
        /// </summary>
        public static JScriptRunAt ParseRunAt(string runAt)
        {
            switch (runAt)
            {
                case "document-start": return JScriptRunAt.DocumentStart;
                case "document-body": return JScriptRunAt.DocumentBody;
                case "document-end": return JScriptRunAt.DocumentEnd;
                case "document-idle": return JScriptRunAt.DocumentIdle;
                case "context-menu": return JScriptRunAt.ContextMenu;
                default: return JScriptRunAt.DocumentIdle;
            }
        }
    }
}
