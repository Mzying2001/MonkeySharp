using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.CefSharp.SmokeHost
{
    internal sealed class PreparedContent : IResourceProvider, IUserScriptDependencyProvider
    {
        public int Calls;
        public bool Offline;
        public Task<ResourceContent> GetAsync(UserScriptInstallation installation, ResourceDeclaration resource, CancellationToken token)
        {
            Calls++;
            if (Offline) throw new InvalidOperationException("Execution attempted to fetch installed content.");
            return Task.FromResult(new ResourceContent(new byte[] { 0, 255, 1 }.Concat(Enumerable.Repeat((byte)97, 600000)).ToArray(), "application/octet-stream"));
        }
        public Task<ResourceContent> GetScriptAsync(UserScriptInstallation installation, UserScriptDependencyDeclaration dependency, CancellationToken token)
        {
            Calls++;
            if (Offline) throw new InvalidOperationException("Execution attempted to fetch a dependency.");
            var source = dependency.Url.EndsWith("one.js") ? "'use strict'; var shared = 1 // trailing comment" : "var accented = 'café'; shared++";
            return Task.FromResult(new ResourceContent(Encoding.GetEncoding(1252).GetBytes(source), "text/javascript", charset: "windows-1252"));
        }
    }
}
