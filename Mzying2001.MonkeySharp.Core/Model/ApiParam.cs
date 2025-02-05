﻿using System.Text.Json.Serialization;

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
        /// Indicates whether the data is in JSON format.
        /// </summary>
        [JsonPropertyName("isJson")]
        public bool IsJson { get; set; }


        /// <summary>
        /// The data passed from the API call.
        /// </summary>
        [JsonPropertyName("data")]
        public string Data { get; set; }
    }
}
