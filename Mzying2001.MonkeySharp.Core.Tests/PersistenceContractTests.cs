using Mzying2001.MonkeySharp.Core.Apis;
using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Repository;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.Core.Tests
{
    public sealed class PersistenceContractTests
    {
        private static readonly CancellationToken None = CancellationToken.None;
        private static UserScriptPersistenceRecord Record(ScriptKey key, string source, IEnumerable<UserScriptAsset> assets = null) =>
            new UserScriptPersistenceRecord(key, source, null, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, assets ?? Array.Empty<UserScriptAsset>());

        [Fact]
        public async Task VersionsAndPreservePreventStaleOrAccidentalReplacement()
        {
            var persistence = new InMemoryUserScriptPersistence(); var key = ScriptKey.Parse(Guid.NewGuid().ToString());
            var original = Record(key, "source");
            var version = await persistence.SaveAsync(new UserScriptSaveRequest(original, null, SourceWriteMode.Replace), None);
            await Assert.ThrowsAsync<UserScriptSourceConflictException>(() => persistence.SaveAsync(new UserScriptSaveRequest(original, null, SourceWriteMode.Replace), None));
            await Assert.ThrowsAsync<UserScriptSourceConflictException>(() => persistence.SaveAsync(new UserScriptSaveRequest(Record(key, "other"), version, SourceWriteMode.Preserve), None));
            var kept = await persistence.SaveAsync(new UserScriptSaveRequest(original, version, SourceWriteMode.Preserve), None);
            Assert.Equal(version, kept);
            var changed = await persistence.SaveAsync(new UserScriptSaveRequest(Record(key, "other"), version, SourceWriteMode.Replace), None);
            Assert.NotEqual(version, changed);
            await Assert.ThrowsAsync<UserScriptSourceConflictException>(() => persistence.SaveAsync(new UserScriptSaveRequest(original, version, SourceWriteMode.Replace), None));
            await persistence.SetEnabledAsync(key, false, original.UpdatedAt, None);
            var loaded = await persistence.LoadAsync(key, None);
            Assert.False(loaded.Record.IsEnabled);
            Assert.Equal(original.InstalledAt, loaded.Record.InstalledAt);
            Assert.Equal(changed, loaded.SourceVersion);
            await persistence.DeleteAsync(key, None); await persistence.DeleteAsync(key, None);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => persistence.LoadAsync(key, None));
        }

        [Fact]
        public async Task AssetsRoundTripOriginalBytesAndDefensivelyCopyCollections()
        {
            var key = ScriptKey.Parse(Guid.NewGuid().ToString());
            var assets = new List<UserScriptAsset> { new UserScriptAsset(UserScriptAssetKind.Resource, 0, "name", "https://example.com/a", new ResourceContent(new byte[] { 0, 255 }, "image/png", "windows-1252")) };
            var record = Record(key, "source", assets); assets.Clear();
            var diagnostics = new List<UserScriptDiagnostic>();
            var result = new UserScriptLoadResult(record, "version", false, diagnostics);
            diagnostics.Add(new UserScriptDiagnostic("error", DiagnosticSeverity.Error, "error", scriptKey: key));
            Assert.Empty(result.Diagnostics);
            var storage = new InMemoryUserScriptPersistence();
            await storage.SaveAsync(new UserScriptSaveRequest(record, null, SourceWriteMode.Replace), None);
            var asset = Assert.Single((await storage.LoadAsync(key, None)).Record.Assets);
            Assert.Equal(new byte[] { 0, 255 }, asset.Content.Bytes);
            Assert.Equal("image/png", asset.Content.MediaType);
            Assert.Equal("windows-1252", asset.Content.Charset);
        }

        [Fact]
        public void MissingContentIsLoadableButCannotBeSavedAndInvalidOrdinalsAreRejected()
        {
            var key = ScriptKey.Parse(Guid.NewGuid().ToString());
            var asset = new UserScriptAsset(UserScriptAssetKind.Require, 0, null, "https://example.com/a", null);
            var record = Record(key, "source", new[] { asset });
            Assert.Throws<ArgumentException>(() => new UserScriptSaveRequest(record, "version", SourceWriteMode.Replace));
            Assert.Throws<ArgumentException>(() => Record(key, "source", new[] { asset, asset }));
            Assert.Throws<ArgumentException>(() => new UserScriptAsset(UserScriptAssetKind.Require, 0, "invalid", "https://example.com/a", null));
        }
    }
}
