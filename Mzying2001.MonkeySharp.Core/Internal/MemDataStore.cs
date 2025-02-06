using System.Collections.Generic;
using System.Linq;

namespace Mzying2001.MonkeySharp.Core.Internal
{
    /// <summary>
    /// Store data in memory, used when the DataStore property of <see cref="InjectorBase"/> is null.
    /// Note that data will be lost when the application is closed.
    /// </summary>
    internal class MemDataStore : IDataStore
    {
        /// <summary>
        /// Data store.
        /// </summary>
        private readonly Dictionary<string, Dictionary<string, string>> _data
            = new Dictionary<string, Dictionary<string, string>>();


        /// <summary>
        /// Lock object.
        /// </summary>
        private readonly object _syncLock = new object();


        /// <inheritdoc />
        public string[] ListKeys(string context)
        {
            var dic = GetContext(context);

            lock (_syncLock)
            {
                return dic.Keys.ToArray();
            }
        }


        /// <inheritdoc />
        public bool Remove(string context, string key)
        {
            var dic = GetContext(context);

            lock (_syncLock)
            {
                return dic.Remove(key);
            }
        }


        /// <inheritdoc />
        public bool Retrieve(string context, string key, out string value)
        {
            var dic = GetContext(context);

            lock (_syncLock)
            {
                return dic.TryGetValue(key, out value);
            }
        }


        /// <inheritdoc />
        public void Store(string context, string key, string value)
        {
            var dic = GetContext(context);

            lock (_syncLock)
            {
                if (dic.ContainsKey(key))
                {
                    dic[key] = value;
                }
                else
                {
                    dic.Add(key, value);
                }
            }
        }


        /// <summary>
        /// Get the context.
        /// </summary>
        private Dictionary<string, string> GetContext(string context)
        {
            lock (_syncLock)
            {
                if (_data.TryGetValue(context, out Dictionary<string, string> result))
                {
                    return result;
                }
                else
                {
                    result = new Dictionary<string, string>();
                    _data.Add(context, result);
                    return result;
                }
            }
        }


        /// <summary>
        /// The global instance.
        /// </summary>
        public static MemDataStore Instance { get; } = new MemDataStore();
    }
}
