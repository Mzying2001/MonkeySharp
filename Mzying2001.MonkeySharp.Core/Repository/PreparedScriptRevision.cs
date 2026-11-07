using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Security;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
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
            IDictionary<string, ResourceContent> resources)
        {
            WebRequestRules = Array.AsReadOnly(definition.Metadata.WebRequest.Select(rule =>
                Apis.WebRequestApiProvider.CompileRule(JsonSerializer.SerializeToElement(new { selector = rule.Selector, action = rule.Action }), definition.Metadata)).ToArray());
            UrlRules = Matching.UserScriptUrlRules.Compile(definition.Metadata);
            Dependencies = new ReadOnlyCollection<ResourceContent>(dependencies.ToArray());
            Resources = new ReadOnlyDictionary<string, ResourceContent>(new Dictionary<string, ResourceContent>(resources, StringComparer.Ordinal));
            var source = new StringBuilder();
            for (var index = 0; index < Dependencies.Count; index++)
                source.Append("// @require ").Append(definition.Metadata.Requires[index].Url).Append('\n')
                    .Append(Dependencies[index].DecodeText()).Append("\n;\n");
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

        internal string Serialize(string originalSource) => JsonSerializer.Serialize(new
        {
            format = 1,
            source = originalSource,
            dependencies = Dependencies.Select(Encode).ToArray(),
            resources = Resources.ToDictionary(item => item.Key, item => Encode(item.Value), StringComparer.Ordinal)
        });

        private static object Encode(ResourceContent content) => new
        { bytes = Convert.ToBase64String(content.Bytes), digest = Digest(content.Bytes), mime = content.MediaType, charset = content.Charset, text = content.Text };

        private static string Digest(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(bytes));
        }

        internal static PreparedScriptRevision Restore(UserScriptDefinition definition, string json)
        {
            using (var document = JsonDocument.Parse(json))
            {
                var root = document.RootElement;
                if (root.GetProperty("format").GetInt32() != 1)
                    throw new InvalidOperationException("Unsupported prepared revision format.");
                if (root.GetProperty("source").GetString() != definition.Source)
                    throw new InvalidOperationException("The prepared payload does not belong to this source revision.");
                var dependencies = root.GetProperty("dependencies").EnumerateArray().Select(Decode).ToArray();
                var resources = root.GetProperty("resources").EnumerateObject()
                    .ToDictionary(item => item.Name, item => Decode(item.Value), StringComparer.Ordinal);
                if (dependencies.Length != definition.Metadata.Requires.Count ||
                    resources.Count != definition.Metadata.Resources.Count)
                    throw new InvalidOperationException("The prepared payload is incomplete.");
                for (var index = 0; index < dependencies.Length; index++)
                    ResourceIntegrityVerifier.Verify(dependencies[index].Bytes, definition.Metadata.Requires[index].Integrity);
                foreach (var declaration in definition.Metadata.Resources)
                    ResourceIntegrityVerifier.Verify(resources[declaration.Name].Bytes, declaration.Integrity);
                return new PreparedScriptRevision(definition, dependencies, resources);
            }
        }

        private static ResourceContent Decode(JsonElement item)
        {
            var bytes = Convert.FromBase64String(item.GetProperty("bytes").GetString());
            if (Digest(bytes) != item.GetProperty("digest").GetString())
                throw new InvalidOperationException("The persisted content digest does not match.");
            return new ResourceContent(bytes, item.GetProperty("mime").GetString(),
                item.GetProperty("text").GetString(), item.GetProperty("charset").GetString());
        }
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

        internal async Task<UserScriptInstallation> PrepareAsync(UserScriptInstallation installation, CancellationToken token)
        {
            if (installation.PreparedRevision != null) return installation;
            var definition = installation.Definition;
            var dependencies = new List<ResourceContent>();
            var resources = new Dictionary<string, ResourceContent>(StringComparer.Ordinal);
            long total = Encoding.UTF8.GetByteCount(definition.Source);
            if (total > _maxContentBytes) throw new InvalidOperationException("MSR411_PREPARED_REVISION_LIMIT: source exceeds the configured content budget.");
            Action<ResourceContent, IReadOnlyList<ResourceIntegrityDeclaration>, string> validate = (content, integrity, url) =>
            {
                if (content == null) throw new InvalidOperationException("The content provider returned no content for '" + url + "'.");
                ResourceIntegrityVerifier.Verify(content.Bytes, integrity, url);
                total += content.Bytes.Length + (content.Text == null ? 0 : Encoding.UTF8.GetByteCount(content.Text));
                if (total > _maxContentBytes)
                    throw new InvalidOperationException("MSR411_PREPARED_REVISION_LIMIT: prepared content exceeds " + _maxContentBytes + " bytes; increase the installation content budget.");
            };
            foreach (var declaration in definition.Metadata.Requires)
            {
                token.ThrowIfCancellationRequested();
                if (_dependencies == null) throw new InvalidOperationException("Configure an installation dependency provider before installing @require.");
                var content = await _dependencies.GetScriptAsync(installation, declaration, token).ConfigureAwait(false);
                validate(content, declaration.Integrity, declaration.Url);
                content.DecodeText();
                dependencies.Add(content);
            }
            foreach (var declaration in definition.Metadata.Resources)
            {
                token.ThrowIfCancellationRequested();
                if (_resources == null) throw new InvalidOperationException("Configure an installation resource provider before installing @resource.");
                var content = await _resources.GetAsync(installation, declaration, token).ConfigureAwait(false);
                validate(content, declaration.Integrity, declaration.Url);
                content.DecodeText();
                resources.Add(declaration.Name, content);
            }
            token.ThrowIfCancellationRequested();
            return installation.WithPreparedRevision(new PreparedScriptRevision(definition, dependencies, resources));
        }
    }
}
