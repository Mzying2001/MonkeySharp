using System.Text.Json.Serialization;

namespace Mzying2001.MonkeySharp.Core.Model
{
    /// <summary>
    /// Key-value pair used by GM_setValue.
    /// </summary>
    public class StorePair
    {
        /// <summary>
        /// The key of the pair.
        /// </summary>
        [JsonPropertyName("key")]
        public string Key { get; set; }


        /// <summary>
        /// The value of the pair.
        /// </summary>
        [JsonPropertyName("value")]
        public JsObject Value { get; set; }
    }
}
