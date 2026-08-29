using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Apis
{
    public sealed class ResourceScriptSourceResolver : IUserScriptSourceResolver
    {
        private readonly IUserScriptDependencyProvider _provider;
        private readonly int _maxDependencyBytes;

        public ResourceScriptSourceResolver(IUserScriptDependencyProvider provider, int maxDependencyBytes)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            if (maxDependencyBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxDependencyBytes));
            _maxDependencyBytes = maxDependencyBytes;
        }

        public async Task<string> ResolveSourceAsync(
            UserScriptInstallation installation,
            CancellationToken cancellationToken)
        {
            var source = new StringBuilder();
            var totalBytes = 0;
            foreach (var requirement in installation.Definition.Metadata.Requires)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dependency = await _provider.GetScriptAsync(
                    installation,
                    requirement,
                    cancellationToken).ConfigureAwait(false);
                if (dependency == null)
                    throw new InvalidOperationException("The dependency provider returned null for '" + requirement + "'.");
                totalBytes += Encoding.UTF8.GetByteCount(dependency);
                if (totalBytes > _maxDependencyBytes)
                    throw new InvalidOperationException("The combined @require payload exceeds the configured limit.");
                source.AppendLine(dependency);
            }
            source.Append(installation.Definition.Source);
            return source.ToString();
        }
    }
}
