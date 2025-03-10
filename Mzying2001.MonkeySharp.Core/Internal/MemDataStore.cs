﻿using System.Collections.Generic;
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


        /// <inheritdoc/>
        public string[] ListKeys(string context)
        {
            lock (_syncLock)
            {
                var dic = GetContext(context);
                return dic.Keys.ToArray();
            }
        }


        /// <inheritdoc/>
        public bool Remove(string context, string key)
        {
            lock (_syncLock)
            {
                var dic = GetContext(context);
                return dic.Remove(key);
            }
        }


        /// <inheritdoc/>
        public bool Remove(string context, IEnumerable<string> keys)
        {
            bool result = true;
            lock (_syncLock)
            {
                var dic = GetContext(context);

                foreach (var item in keys)
                {
                    if (!dic.Remove(item))
                    {
                        result = false;
                    }
                }
            }
            return result;
        }


        /// <inheritdoc/>
        public bool Retrieve(string context, string key, out string value)
        {
            lock (_syncLock)
            {
                var dic = GetContext(context);
                return dic.TryGetValue(key, out value);
            }
        }


        /// <inheritdoc/>
        public bool Retrieve(string context, IEnumerable<string> keys, out Dictionary<string, string> values)
        {
            bool result = true;
            lock (_syncLock)
            {
                var dic = GetContext(context);
                values = new Dictionary<string, string>();

                foreach (var key in keys)
                {
                    if (dic.TryGetValue(key, out var value))
                    {
                        values[key] = value;
                    }
                    else
                    {
                        result = false;
                    }
                }
            }
            return result;
        }


        /// <inheritdoc/>
        public bool Store(string context, string key, string value)
        {
            lock (_syncLock)
            {
                var dic = GetContext(context);

                if (dic.ContainsKey(key))
                {
                    dic[key] = value;
                }
                else
                {
                    dic.Add(key, value);
                }
            }
            return true;
        }


        /// <inheritdoc/>
        public bool Store(string context, IEnumerable<KeyValuePair<string, string>> values)
        {
            lock (_syncLock)
            {
                var dic = GetContext(context);

                foreach (var item in values)
                {
                    if (dic.ContainsKey(item.Key))
                    {
                        dic[item.Key] = item.Value;
                    }
                    else
                    {
                        dic.Add(item.Key, item.Value);
                    }
                }
            }
            return true;
        }


        /// <summary>
        /// Get the context.
        /// </summary>
        private Dictionary<string, string> GetContext(string context)
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


        /// <summary>
        /// The global instance.
        /// </summary>
        public static MemDataStore Instance { get; } = new MemDataStore();
    }
}
