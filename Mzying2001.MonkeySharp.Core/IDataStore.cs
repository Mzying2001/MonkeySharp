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
        void Store(string context, string key, string value);


        /// <summary>
        /// Retrieve data.
        /// </summary>
        /// <returns>Returns true if data found.</returns>
        bool Retrieve(string context, string key, out string value);


        /// <summary>
        /// Remove data.
        /// </summary>
        /// <returns>Returns true if data found and removed.</returns>
        bool Remove(string context, string key);


        /// <summary>
        /// List all keys in the context.
        /// </summary>
        /// <returns>An array of keys, if no key found, returns an empty array.</returns>
        string[] ListKeys(string context);
    }
}
