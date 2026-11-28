using CefSharp;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mzying2001.MonkeySharp.CefSharp.IntegrationTests
{
    public sealed class PageContextTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task DocumentReleaseSchedulesObjectGroupCleanupOnlyWhileTheSessionIsOpen(bool closeSession)
        {
            using (var protocol = await Protocol.CreateAsync())
            {
                var document = await protocol.BindAsync("first");
                if (closeSession) protocol.Dispose();
                var scheduledBeforeRelease = protocol.ScheduledCommands;

                await document.ReleaseAsync();

                Assert.Equal(scheduledBeforeRelease + (closeSession ? 0 : 1), protocol.ScheduledCommands);
                Assert.Equal(!closeSession, protocol.ObjectGroupReleased);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ReusedObjectIdCannotRunThePreviousDocumentsSource(bool reload)
        {
            using (var protocol = await Protocol.CreateAsync())
            {
                var old = await protocol.BindAsync("first");
                Assert.Equal(protocol.Url, old.Url);

                // The renderer has allocated the same object ID in its new context,
                // before the browser process has delivered the navigation events.
                protocol.ReplaceObject("second", reload ? protocol.Url : new Uri("https://localhost.test/other"));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old.ExecuteAsync("staleSource()", "test://old", CancellationToken.None));
                Assert.Empty(protocol.Executed);
                Assert.True(old.Revoked.IsCancellationRequested);

                protocol.CommitReplacement();
                var current = await protocol.BindAsync("second");
                Assert.Equal(protocol.Url, current.Url);
                protocol.Raise("Runtime.executionContextDestroyed", "{\"executionContextId\":1}");
                await current.ExecuteAsync("currentSource()", "test://current", CancellationToken.None);
                Assert.Equal("currentSource()", Assert.Single(protocol.Executed));
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task ANewLoaderRevokesItsPredecessorAndIgnoresOldEvents(bool legacy, bool contextBeforeCommit)
        {
            using (var protocol = await Protocol.CreateAsync(legacy))
            {
                var first = await protocol.BindAsync("first");
                protocol.DefaultContext();
                Assert.False(first.Revoked.IsCancellationRequested);
                protocol.ReplaceObject("second", protocol.Url);
                protocol.CommitReplacement(contextBeforeCommit);
                Assert.True(first.Revoked.IsCancellationRequested);
                var second = await protocol.BindAsync("second");
                Assert.False(second.Revoked.IsCancellationRequested);
                protocol.Raise("Runtime.executionContextDestroyed", legacy ? "{\"executionContextId\":1}" :
                    "{\"executionContextId\":1,\"executionContextUniqueId\":\"loader-1\"}");
                Assert.False(second.Revoked.IsCancellationRequested);
            }
        }

        internal sealed class Protocol : IDisposable
        {
            private readonly CefSharpCdpSession _session;
            private readonly CefSharpPageContexts _contexts;
            private readonly bool _legacy;
            private int _messageId;
            private int _loader = 1;
            private string _owner = "first";
            private string _generation;
            internal Uri Url { get; private set; } = new Uri("https://example.test/matched");
            internal List<string> Executed { get; } = new List<string>();
            internal TaskCompletionSource<string> Rejected { get; } = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal int ScheduledCommands { get; private set; }
            internal bool ObjectGroupReleased { get; private set; }

            private Protocol(bool legacy)
            {
                _legacy = legacy;
                var host = new Mock<IBrowserHost>();
                _session = new CefSharpCdpSession(host.Object, action =>
                {
                    ScheduledCommands++;
                    action();
                    return Task.CompletedTask;
                });
                _contexts = new CefSharpPageContexts(_session);
                host.Setup(item => item.ExecuteDevToolsMethod(0, It.IsAny<string>(), It.IsAny<string>()))
                    .Returns((int _, string method, string args) =>
                    {
                        var id = ++_messageId;
                        using (var parameters = JsonDocument.Parse(args))
                        using (var response = Reply(method, parameters.RootElement))
                            _session.OnDevToolsMethodResult(null, id, true, response);
                        return id;
                    });
            }

            internal static async Task<Protocol> CreateAsync(bool legacy = false)
            {
                var protocol = new Protocol(legacy);
                await protocol._contexts.InitializeAsync(CancellationToken.None);
                protocol.DefaultContext();
                return protocol;
            }

            private Stream Reply(string method, JsonElement args)
            {
                switch (method)
                {
                    case "Page.getFrameTree":
                        return Json("{\"frameTree\":{\"frame\":{\"id\":\"main\",\"loaderId\":\"loader-" + _loader + "\"}}}");
                    case "Runtime.evaluate":
                        Assert.Equal(1, args.GetProperty("contextId").GetInt32());
                        var expression = args.GetProperty("expression").GetString();
                        Assert.Contains("document[\"marker\"]===\"" + _owner + "\"", expression);
                        const string generation = ",generation:\"";
                        var begin = expression.IndexOf(generation, StringComparison.Ordinal) + generation.Length;
                        _generation = expression.Substring(begin, expression.IndexOf('"', begin) - begin);
                        return Json("{\"result\":{\"objectId\":\"reused-id\"}}");
                    case "Runtime.callFunctionOn":
                        Assert.Equal("reused-id", args.GetProperty("objectId").GetString());
                        var declaration = args.GetProperty("functionDeclaration").GetString();
                        const string check = "function(expected){if(this.generation!==expected||this.document!==document)return false;\n";
                        Assert.StartsWith(check, declaration);
                        if (args.GetProperty("arguments")[0].GetProperty("value").GetString() != _generation)
                        {
                            Rejected.TrySetResult(declaration);
                            return Json("{\"result\":{\"value\":false}}");
                        }
                        if (declaration.Contains("return this.url;"))
                            return Json("{\"result\":{\"value\":" + JsonSerializer.Serialize(Url.AbsoluteUri) + "}}");
                        if (declaration.StartsWith(check + "return true;\n}", StringComparison.Ordinal))
                            return Json("{\"result\":{\"value\":true}}");
                        var source = declaration.Split('\n')[2];
                        Executed.Add(source);
                        return Json("{\"result\":{\"value\":true}}");
                    case "Runtime.releaseObjectGroup":
                        Assert.Equal(_generation, args.GetProperty("objectGroup").GetString());
                        ObjectGroupReleased = true;
                        return Json("{}");
                    default:
                        return Json("{}");
                }
            }

            internal async Task<CefSharpPageDocument> BindAsync(string owner)
            {
                var document = await _contexts.BindAsync("marker", owner, true, CancellationToken.None);
                Assert.NotNull(document);
                return document;
            }

            internal void ReplaceObject(string owner, Uri url)
            {
                _owner = owner;
                Url = url;
                _generation = "replacement-generation";
                _loader++;
            }

            internal void CommitReplacement(bool contextBeforeCommit = false)
            {
                if (contextBeforeCommit) DefaultContext();
                Raise("Page.frameNavigated", "{\"frame\":{\"id\":\"main\",\"loaderId\":\"loader-" + _loader + "\"}}");
                if (!contextBeforeCommit) DefaultContext();
            }

            internal void DefaultContext() => Raise("Runtime.executionContextCreated", "{\"context\":{\"id\":1," +
                (_legacy ? string.Empty : "\"uniqueId\":\"loader-" + _loader + "\",") +
                "\"auxData\":{\"isDefault\":true,\"frameId\":\"main\"}}}");

            internal void Raise(string method, string args)
            {
                using (var stream = Json(args))
                    _session.OnDevToolsEvent(null, method, stream);
            }

            private static Stream Json(string value) => new MemoryStream(Encoding.UTF8.GetBytes(value));

            public void Dispose() => _session.Dispose();
        }
    }
}
