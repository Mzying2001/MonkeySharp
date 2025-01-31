using System;
using System.IO;

namespace Mzying2001.MonkeySharp.Core.Internal
{
    internal static class ResourceLoader
    {
        /// <summary>
        /// Gets the content of an embedded resource.
        /// </summary>
        public static string GetEmbeddedResource(string resourceName)
        {
            var assembly = typeof(ResourceLoader).Assembly;

            var resourceStream = assembly.GetManifestResourceStream(resourceName);
            if (resourceStream == null)
            {
                throw new InvalidOperationException($"Resource {resourceName} not found.");
            }

            using (var reader = new StreamReader(resourceStream))
            {
                return reader.ReadToEnd();
            }
        }


        /// <summary>
        /// Gets the content of the inject.js file.
        /// </summary>
        public static string GetInjectJs()
        {
            return GetEmbeddedResource("Mzying2001.MonkeySharp.Core.Js.Inject.js");
        }
    }
}
