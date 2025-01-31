using System;
using System.IO;

namespace Mzying2001.MonkeySharp.Core.Internal
{
    internal static class ThrowHelper
    {
        /// <summary>
        /// Throw if the argument is null.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static void ThrowIfArgumentNull(object obj, string name)
        {
            if (obj == null)
                throw new ArgumentNullException(name);
        }


        /// <summary>
        /// Throw if the file is not found.
        /// </summary>
        /// <exception cref="FileNotFoundException"></exception>
        public static void ThrowIfFileNotFound(string fileName)
        {
            if (!File.Exists(fileName))
                throw new FileNotFoundException(fileName);
        }


        /// <summary>
        /// Throw if the argument is not of the specified type.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public static void ThrowIfArgumentNotOfType<T>(object obj, string name)
        {
            if (!(obj is T))
                throw new ArgumentException("Invalid type.", name);
        }
    }
}
