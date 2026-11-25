using CefSharp;
using Mzying2001.MonkeySharp.Core.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.CefSharp
{
    internal sealed partial class CefSharpIsolatedExecutor
    {
        private readonly IWebBrowser _menuBrowser;
        private readonly Dictionary<string, MenuContext> _menuContexts = new Dictionary<string, MenuContext>(StringComparer.Ordinal);
        private readonly Dictionary<string, NativeMenuFrame> _menuNativeFrames = new Dictionary<string, NativeMenuFrame>(StringComparer.Ordinal);
        private bool _menuMainLoading;
        private bool _menuRefreshPending;
        private bool _menuRefreshRunning;
        private bool _menuProbeUnsupported;

        private void RememberMenuContext(string frameId, int id, JsonElement context)
        {
            var uniqueId = context.TryGetProperty("uniqueId", out var unique) ? unique.GetString() : null;
            if (uniqueId != null && _menuContexts.TryGetValue(frameId, out var previous) && previous.Id == id && previous.UniqueId == uniqueId) return;
            InvalidateMenuDocument(frameId);
            _menuContexts[frameId] = new MenuContext(id, uniqueId);
            RequestMenuFrameRefresh();
        }

        private void ForgetMenuContext(int id, JsonElement context)
        {
            var uniqueId = context.TryGetProperty("executionContextUniqueId", out var unique) ? unique.GetString() : null;
            var match = _menuContexts.FirstOrDefault(pair => pair.Value.Id == id && (uniqueId == null || pair.Value.UniqueId == uniqueId));
            if (match.Key == null) return;
            _menuContexts.Remove(match.Key);
            InvalidateMenuDocument(match.Key);
        }

        internal void NativeMenuNavigation(string frameId, bool main, bool loading)
        {
            lock (_sync)
            {
                if (_stopping || _disposed) return;
                if (main)
                {
                    _menuMainLoading = loading;
                    if (loading) ClearNativeMenuFrames();
                }
                else if (loading)
                {
                    if (_menuNativeFrames.TryGetValue(frameId, out var previous)) previous.ClearBinding();
                    _menuNativeFrames[frameId] = new NativeMenuFrame(frameId) { Loading = true };
                }
                else if (_menuNativeFrames.TryGetValue(frameId, out var frame) && frame.Loading)
                {
                    // The next scan creates a fresh slot for this committed document.
                    _menuNativeFrames.Remove(frameId);
                }
            }
            if (!loading) RequestMenuFrameRefresh();
        }

        private void ClearNativeMenuFrames()
        {
            foreach (var native in _menuNativeFrames.Values) native.ClearBinding();
            _menuNativeFrames.Clear();
        }

        private void InvalidateMenuDocument(string frameId)
        {
            foreach (var native in _menuNativeFrames.Values.Where(frame => frame.Document?.Id == frameId).ToArray())
            {
                native.ClearBinding();
                _menuNativeFrames[native.Id] = new NativeMenuFrame(native.Id) { Loading = native.Loading };
            }
        }

        private bool TryGetMenuBinding(World world, string nativeFrameId, bool main, out CancellationToken binding)
        {
            binding = default;
            if (!world.Ready || !Alive(world) || main != world.Native.Main) return false;
            if (main) return true;
            if (!_menuNativeFrames.TryGetValue(nativeFrameId, out var native) || !ReferenceEquals(native.Document, world.Native)) return false;
            binding = native.Binding.Token;
            return true;
        }

        private void RequestMenuFrameRefresh()
        {
            if (_menuBrowser == null) return;
            lock (_sync)
            {
                if (_disposed || _stopping || _menuProbeUnsupported || _menuMainLoading) return;
                _menuRefreshPending = true;
                if (_menuRefreshRunning) return;
                _menuRefreshRunning = true;
                Track(Task.Run(RefreshMenuFramesAsync));
            }
        }

        private async Task RefreshMenuFramesAsync()
        {
            while (true)
            {
                lock (_sync)
                {
                    if (!_menuRefreshPending || _disposed || _stopping || _menuProbeUnsupported || _menuMainLoading)
                    {
                        _menuRefreshRunning = false;
                        return;
                    }
                    _menuRefreshPending = false;
                }
                try
                {
                    await ProbeMenuFramesAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (TimeoutException) { }
                catch (InvalidOperationException error) when (Regex.IsMatch(error.Message, @"""code""\s*:\s*-32601\b"))
                {
                    lock (_sync) _menuProbeUnsupported = true;
                    _diagnostic(new UserScriptDiagnostic("MSC237_MENU_FRAME_MAPPING_UNAVAILABLE", DiagnosticSeverity.Warning,
                        "This Chromium target does not support menu frame verification."));
                }
                catch (Exception error)
                {
                    if (!_stopping && !_disposed)
                        _diagnostic(new UserScriptDiagnostic("MSC237_MENU_FRAME_MAPPING_UNAVAILABLE", DiagnosticSeverity.Warning,
                            "Native menu frame verification failed.", error));
                }
            }
        }

        private async Task ProbeMenuFramesAsync()
        {
            MenuCandidate[] contexts;
            lock (_sync)
            {
                if (!_frames.Values.Any(NeedsMenuBinding)) return;
                contexts = _frames.Values.Where(frame => !frame.Main && _menuContexts.ContainsKey(frame.Id))
                    .Select(frame => new MenuCandidate(frame, _menuContexts[frame.Id])).ToArray();
            }
            if (contexts.Length == 0) return;

            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(2));
                var token = deadline.Token;
                var before = (await _session.ExecuteAsync("Page.getFrameTree", cancellationToken: token).ConfigureAwait(false)).GetProperty("frameTree");
                contexts = contexts.Where(candidate => FindFrame(before, candidate.Document.Id, candidate.Document.Loader)).ToArray();
                if (contexts.Length == 0) return;
                var markers = await Cef.UIThreadTaskFactory.StartNew(() => InstallMenuMarkers(token), token).ConfigureAwait(false);
                if (markers.Count == 0) return;
                while (true)
                {
                    var matches = await Task.WhenAll(contexts.Select(candidate => ReadMenuMarkersAsync(candidate, markers, token))).ConfigureAwait(false);
                    if (matches.Any(names => names.Length != 0))
                    {
                        var after = (await _session.ExecuteAsync("Page.getFrameTree", cancellationToken: token).ConfigureAwait(false)).GetProperty("frameTree");
                        PublishMenuBindings(contexts, markers, matches, after);
                    }
                    lock (_sync)
                        if (contexts.All(candidate => !IsCurrentMenuCandidate(candidate) || !NeedsMenuBinding(candidate.Document))) return;
                    await Task.Delay(75, token).ConfigureAwait(false);
                }
            }
        }

        private bool NeedsMenuBinding(NativeFrame frame) => !frame.Main &&
            frame.Worlds.Values.Any(world => world.Ready && Alive(world)) &&
            !_menuNativeFrames.Values.Any(native => ReferenceEquals(native.Document, frame));

        private Dictionary<string, NativeMenuFrame> InstallMenuMarkers(CancellationToken token)
        {
            var markers = new Dictionary<string, NativeMenuFrame>(StringComparer.Ordinal);
            if (!_menuBrowser.IsBrowserInitialized || _menuBrowser.IsDisposed) return markers;
            var browser = _menuBrowser.GetBrowser();
            if (browser == null || browser.IsDisposed) return markers;
            var identifiers = browser.GetFrameIdentifiers().Select(id => Convert.ToString(id, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            lock (_sync)
            {
                if (_menuMainLoading || _stopping || _disposed) return markers;
                var present = new HashSet<string>(identifiers, StringComparer.Ordinal);
                foreach (var removed in _menuNativeFrames.Keys.Where(id => !present.Contains(id)).ToArray())
                {
                    _menuNativeFrames[removed].ClearBinding();
                    _menuNativeFrames.Remove(removed);
                }
                foreach (var id in identifiers)
                {
                    token.ThrowIfCancellationRequested();
                    using (var frame = CefSharpUserScriptHost.GetFrameByIdentifier(browser, id))
                    {
                        if (frame == null || frame.IsDisposed || !frame.IsValid || frame.IsMain) continue;
                        if (!_menuNativeFrames.TryGetValue(id, out var native))
                            _menuNativeFrames[id] = native = new NativeMenuFrame(id);
                        if (native.Loading || native.Document != null) continue;
                        if (native.Markers.Count < 2)
                        {
                            // V8 exposes lexical names to CDP without invoking page JavaScript.
                            // The page gets no callable binding or value to forward to another frame.
                            var marker = "__MonkeySharpMenuFrame_" + Guid.NewGuid().ToString("N");
                            frame.ExecuteJavaScriptAsync("let " + marker + ";", "about:blank", 1);
                            native.Markers.Add(marker);
                        }
                        foreach (var marker in native.Markers)
                            markers.Add(marker, native);
                    }
                }
            }
            return markers;
        }

        private async Task<string[]> ReadMenuMarkersAsync(MenuCandidate candidate, Dictionary<string, NativeMenuFrame> markers, CancellationToken token)
        {
            lock (_sync) if (!IsCurrentMenuCandidate(candidate)) return Array.Empty<string>();
            try
            {
                var result = await _session.ExecuteAsync("Runtime.globalLexicalScopeNames",
                    new { executionContextId = candidate.Context.Id }, token).ConfigureAwait(false);
                return result.GetProperty("names").EnumerateArray().Select(name => name.GetString()).Where(markers.ContainsKey).ToArray();
            }
            catch (InvalidOperationException error) when (Regex.IsMatch(error.Message, @"""code""\s*:\s*-32000\b"))
            {
                // The renderer can retire its context before the notification reaches this observer.
                return Array.Empty<string>();
            }
        }

        private bool IsCurrentMenuCandidate(MenuCandidate candidate) => !_disposed && !_stopping && !_menuMainLoading &&
            _frames.TryGetValue(candidate.Document.Id, out var document) && ReferenceEquals(document, candidate.Document) &&
            _menuContexts.TryGetValue(document.Id, out var context) && ReferenceEquals(context, candidate.Context);

        private void PublishMenuBindings(MenuCandidate[] contexts, Dictionary<string, NativeMenuFrame> markers, string[][] matches, JsonElement tree)
        {
            var observed = Enumerable.Range(0, contexts.Length).SelectMany(index =>
                matches[index].Select(marker => markers[marker]).Distinct().Select(native => new { Native = native, Candidate = contexts[index] })).ToArray();
            lock (_sync)
            {
                foreach (var group in observed.GroupBy(item => item.Native))
                {
                    var native = group.Key;
                    if (!_menuNativeFrames.TryGetValue(native.Id, out var current) || !ReferenceEquals(current, native) || native.Loading) continue;
                    var candidate = group.First().Candidate;
                    if (group.Count() != 1 || observed.Count(item => ReferenceEquals(item.Candidate, candidate)) != 1 ||
                        !IsCurrentMenuCandidate(candidate) || !FindFrame(tree, candidate.Document.Id, candidate.Document.Loader))
                    {
                        native.ClearBinding();
                        continue;
                    }
                    var other = _menuNativeFrames.Values.FirstOrDefault(item => !ReferenceEquals(item, native) && ReferenceEquals(item.Document, candidate.Document));
                    if (other != null)
                    {
                        other.ClearBinding();
                        native.ClearBinding();
                        continue;
                    }
                    if (ReferenceEquals(native.Document, candidate.Document)) continue;
                    native.ClearBinding();
                    native.Document = candidate.Document;
                    native.Binding = new CancellationTokenSource();
                }
            }
        }

        private sealed class MenuContext
        {
            internal MenuContext(int id, string uniqueId) { Id = id; UniqueId = uniqueId; }
            internal int Id { get; }
            internal string UniqueId { get; }
        }

        private sealed class MenuCandidate
        {
            internal MenuCandidate(NativeFrame document, MenuContext context) { Document = document; Context = context; }
            internal NativeFrame Document { get; }
            internal MenuContext Context { get; }
        }

        private sealed class NativeMenuFrame
        {
            internal NativeMenuFrame(string id) { Id = id; }
            internal string Id { get; }
            internal readonly List<string> Markers = new List<string>(2);
            internal bool Loading;
            internal NativeFrame Document;
            internal CancellationTokenSource Binding;

            internal void ClearBinding()
            {
                Document = null;
                Binding?.Cancel();
                Binding?.Dispose();
                Binding = null;
            }
        }
    }
}
