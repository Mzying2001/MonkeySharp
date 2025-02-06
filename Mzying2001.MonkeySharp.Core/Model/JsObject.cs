using System.Text.Json.Serialization;

namespace Mzying2001.MonkeySharp.Core.Model
{
    /// <summary>
    /// Represents a JavaScript object.
    /// </summary>
    public class JsObject
    {
        /// <summary>
        /// The type of the object.
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }


        /// <summary>
        /// The json string of the object, or null if type is "undefined".
        /// </summary>
        [JsonPropertyName("json")]
        public string Json { get; set; }
    }
}
