using System.Collections.Generic;

namespace Mzying2001.MonkeySharp.Core
{
    /// <summary>
    /// Interface for data persistence.
    /// </summary>
    public interface IDataStore
    {
        /// <summary>
        /// Add or update data.
        /// </summary>
        /// <returns>Returns true if data stored successfully.</returns>
        bool Store(string context, string key, string value);


        /// <summary>
        /// Add or update multiple data.
        /// </summary>
        /// <returns>Returns true if all data stored successfully.</returns>
        bool Store(string context, IEnumerable<KeyValuePair<string, string>> values);


        /// <summary>
        /// Retrieve data.
        /// </summary>
        /// <returns>Returns true if data found.</returns>
        bool Retrieve(string context, string key, out string value);


        /// <summary>
        /// Retrieve multiple data.
        /// </summary>
        /// <returns>Returns true if all data found.</returns>
        bool Retrieve(string context, IEnumerable<string> keys, out Dictionary<string, string> values);


        /// <summary>
        /// Remove data.
        /// </summary>
        /// <returns>Returns true if data found and removed.</returns>
        bool Remove(string context, string key);


        /// <summary>
        /// Remove multiple data.
        /// </summary>
        /// <returns>Returns true if all data found and removed.</returns>
        bool Remove(string context, IEnumerable<string> keys);


        /// <summary>
        /// List all keys in the context.
        /// </summary>
        /// <returns>An array of keys, if no key found, returns an empty array.</returns>
        string[] ListKeys(string context);
    }
}
