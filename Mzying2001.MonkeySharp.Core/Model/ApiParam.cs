using System.Text.Json.Serialization;

namespace Mzying2001.MonkeySharp.Core.Model
{
    /// <summary>
    /// The parameters from the API call.
    /// </summary>
    public class ApiParam
    {
        /// <summary>
        /// The id of the script to be executed.
        /// </summary>
        [JsonPropertyName("scriptId")]
        public string ScriptId { get; set; }


        /// <summary>
        /// Parameters passed from the API call.
        /// </summary>
        [JsonPropertyName("jsonData")]
        public string JsonData { get; set; }
    }
}
