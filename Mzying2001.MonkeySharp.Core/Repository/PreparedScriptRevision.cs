using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Security;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Repository
{
    /// <summary>An immutable, verified installation payload shared by all executions of a revision.</summary>
    public sealed class PreparedScriptRevision
    {
        internal PreparedScriptRevision(UserScriptDefinition definition, IEnumerable<ResourceContent> dependencies,
            IDictionary<string, ResourceContent> resources, Matching.UserScriptUrlRules urlRules, IReadOnlyList<WebRequestRule> webRequestRules)
        {
            WebRequestRules = webRequestRules;
            UrlRules = urlRules;
            Dependencies = new ReadOnlyCollection<ResourceContent>(dependencies.ToArray());
            Resources = new ReadOnlyDictionary<string, ResourceContent>(new Dictionary<string, ResourceContent>(resources, StringComparer.Ordinal));
            var source = new StringBuilder();
            for (var index = 0; index < Dependencies.Count; index++)
                source.Append("// @require ").Append(definition.Metadata.Requires[index].Url).Append('\n')
                    .Append(Dependencies[index].DecodeText(strict: true)).Append("\n;\n");
            source.Append(definition.Source);
            Source = source.Append("\n//# sourceURL=monkeysharp://scripts/")
                .Append(Uri.EscapeDataString(definition.Metadata.Name ?? "script"))
                .Append(".user.js").ToString();
            ResourcePackageJson = JsonSerializer.Serialize(new { complete = true,
                values = Resources.ToDictionary(item => item.Key, item => new
                { text = item.Value.DecodeText(), url = "data:" + item.Value.MediaType + ";base64," + Convert.ToBase64String(item.Value.Bytes) }, StringComparer.Ordinal) });
        }

        /// <summary>Gets the URL and context conditions compiled with this revision.</summary>
        public Matching.UserScriptUrlRules UrlRules { get; }
        /// <summary>Gets bounded static rules compiled before activation.</summary>
        public IReadOnlyList<Apis.WebRequestRule> WebRequestRules { get; }

        /// <summary>Gets the executable source with dependency order, scope and statement boundaries preserved.</summary>
        public string Source { get; }
        /// <summary>Gets the original dependency bytes and media information in metadata order.</summary>
        public IReadOnlyList<ResourceContent> Dependencies { get; }
        /// <summary>Gets the original named resource bytes and media information.</summary>
        public IReadOnlyDictionary<string, ResourceContent> Resources { get; }

        internal string ResourcePackageJson { get; }

    }

    /// <summary>Fetches and validates all external content before a repository publishes a revision.</summary>
    public sealed class UserScriptPreparer
    {
        private readonly IUserScriptDependencyProvider _dependencies;
        private readonly IResourceProvider _resources;
        private readonly int _maxContentBytes;
        /// <summary>Creates a preparation service. External declarations require the corresponding provider.</summary>
        public UserScriptPreparer(IUserScriptDependencyProvider dependencies = null, IResourceProvider resources = null,
            int maxContentBytes = 32 * 1024 * 1024)
        {
            if (maxContentBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxContentBytes));
            _dependencies = dependencies;
            _resources = resources;
            _maxContentBytes = maxContentBytes;
        }

        internal async Task<PreparationResult> PrepareAsync(UserScriptInstallation installation,
            IReadOnlyList<UserScriptAsset> previous, PreparationMode mode, CancellationToken token)
        {
            var definition = installation.Definition;
            if (!definition.ParseResult.CanEnable) throw new MetadataValidationException(definition.ParseResult);
            var urlRules = Matching.UserScriptUrlRules.Compile(definition.Metadata);
            IReadOnlyList<WebRequestRule> webRequestRules;
            try
            {
                webRequestRules = Array.AsReadOnly(definition.Metadata.WebRequest.Select(rule => WebRequestApiProvider.CompileRule(
                    JsonSerializer.SerializeToElement(new { selector = rule.Selector, action = rule.Action }), definition.Metadata)).ToArray());
            }
            catch (BridgeProtocolException error) { throw new PreparationException("MSR421_ASSET_UNAVAILABLE", error.Message, error); }
            var assets = new List<UserScriptAsset>();
            var downloads = new Dictionary<string, ResourceContent>(StringComparer.Ordinal);
            long total = Encoding.UTF8.GetByteCount(definition.Source);
            CheckBudget(total);
            var downloaded = false;

            async Task Add(UserScriptAssetKind kind, int ordinal, string name, string url,
                IReadOnlyList<ResourceIntegrityDeclaration> integrity, Func<Task<ResourceContent>> fetch)
            {
                token.ThrowIfCancellationRequested();
                ResourceContent selected = null;
                if (mode != PreparationMode.Refresh)
                {
                    var candidates = previous.Where(asset => asset.Kind == kind && asset.Url == url && asset.Content != null)
                        .OrderBy(asset => kind == UserScriptAssetKind.Require ? (asset.Ordinal == ordinal ? 0 : 1) : (asset.Name == name ? 0 : 1))
                        .ThenBy(asset => asset.Ordinal);
                    foreach (var candidate in candidates)
                    {
                        try { Validate(candidate.Content, integrity, url, kind); selected = candidate.Content; break; }
                        catch (ResourceIntegrityException) { }
                        catch (PreparationException) { }
                    }
                }
                if (selected == null)
                {
                    if (mode == PreparationMode.CachedOnly)
                        throw new PreparationException("MSR421_ASSET_UNAVAILABLE", "Cached asset unavailable: " + url);
                    var key = ((int)kind).ToString() + ":" + url;
                    if (!downloads.TryGetValue(key, out selected))
                    {
                        try { selected = await fetch().ConfigureAwait(false); }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                        catch (Exception error) { throw new PreparationException("MSR425_ASSET_FETCH_FAILED", "Asset fetch failed: " + url, error); }
                        if (selected == null) throw new PreparationException("MSR425_ASSET_FETCH_FAILED", "Asset provider returned no content: " + url);
                        downloads.Add(key, selected);
                        downloaded = true;
                    }
                    Validate(selected, integrity, url, kind);
                }
                total += selected.ByteLength + Encoding.UTF8.GetByteCount(selected.DecodeText(kind == UserScriptAssetKind.Require));
                CheckBudget(total);
                assets.Add(new UserScriptAsset(kind, ordinal, name, url, selected));
            }

            for (var index = 0; index < definition.Metadata.Requires.Count; index++)
            {
                var declaration = definition.Metadata.Requires[index];
                await Add(UserScriptAssetKind.Require, index, null, declaration.Url, declaration.Integrity,
                    () => _dependencies != null ? _dependencies.GetScriptAsync(installation, declaration, token) :
                    throw new PreparationException("MSR425_ASSET_FETCH_FAILED", "Configure a dependency provider for " + declaration.Url)).ConfigureAwait(false);
            }
            for (var index = 0; index < definition.Metadata.Resources.Count; index++)
            {
                var declaration = definition.Metadata.Resources[index];
                await Add(UserScriptAssetKind.Resource, index, declaration.Name, declaration.Url, declaration.Integrity,
                    () => _resources != null ? _resources.GetAsync(installation, declaration, token) :
                    throw new PreparationException("MSR425_ASSET_FETCH_FAILED", "Configure a resource provider for " + declaration.Url)).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            var revision = new PreparedScriptRevision(definition,
                assets.Where(asset => asset.Kind == UserScriptAssetKind.Require).Select(asset => asset.Content),
                assets.Where(asset => asset.Kind == UserScriptAssetKind.Resource).ToDictionary(asset => asset.Name, asset => asset.Content, StringComparer.Ordinal), urlRules, webRequestRules);
            return new PreparationResult(revision, Array.AsReadOnly(assets.ToArray()), downloaded);
        }

        private void CheckBudget(long total)
        {
            if (total > _maxContentBytes) throw new PreparationException("MSR411_PREPARED_REVISION_LIMIT", "Prepared content exceeds " + _maxContentBytes + " bytes.");
        }

        private static void Validate(ResourceContent content, IReadOnlyList<ResourceIntegrityDeclaration> integrity, string url, UserScriptAssetKind kind)
        {
            ResourceIntegrityVerifier.Verify(content.Bytes, integrity, url);
            try { content.DecodeText(kind == UserScriptAssetKind.Require); }
            catch (ArgumentException error) { throw new PreparationException("MSR421_ASSET_UNAVAILABLE", "Asset decoding failed: " + url, error); }
        }
    }

    internal enum PreparationMode { Reuse, Refresh, CachedOnly }

    internal sealed class PreparationResult
    {
        internal PreparationResult(PreparedScriptRevision revision, IReadOnlyList<UserScriptAsset> assets, bool downloaded)
        { Revision = revision; Assets = assets; Downloaded = downloaded; }
        internal PreparedScriptRevision Revision { get; }
        internal IReadOnlyList<UserScriptAsset> Assets { get; }
        internal bool Downloaded { get; }
    }

    internal sealed class PreparationException : InvalidOperationException
    {
        internal PreparationException(string code, string message, Exception innerException = null) : base(code + ": " + message, innerException) { Code = code; }
        internal string Code { get; }
    }
}
