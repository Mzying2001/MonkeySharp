using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Permissions;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Bridge
{
    public sealed partial class UserScriptBridgeGateway
    {
        private async Task<string> HandleHelloAsync(JsonElement root, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryReadExecution(root, out var execution))
            {
                return Limit(ProtocolJson.Error(
                    null,
                    BridgeErrorCodes.SessionExpired,
                    "The script execution session is not active.",
                    true));
            }

            if (!await _engine.IsInstallationActiveAsync(execution.Installation, cancellationToken).ConfigureAwait(false))
            {
                await _engine.InvalidateExecutionAsync(execution.Invocation.ExecutionId, CancellationToken.None).ConfigureAwait(false);
                return Limit(ProtocolJson.Error(null, BridgeErrorCodes.SessionExpired, "The installation changed or was disabled.", true));
            }
            var grants = execution.Installation.Definition.Metadata.Capabilities;
            var supported = SupportedApis().Where(api => (api == "GM.info" || grants.Contains(api)) &&
                !(execution.Frame.ExecutionWorld == "isolated" && api == "GM.webRequest"));
            var compatibility = new Dictionary<string, object>();
            using (var authorizationLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, execution.Cancellation.Token))
            {
                var bootstrapMethods = new List<string>();
                if (HasStorageCapability(grants)) bootstrapMethods.Add("GM.getValues");
                if (HasResourceCapability(grants)) bootstrapMethods.Add("GM.getResourceText");
                foreach (var method in bootstrapMethods)
                    if (await AuthorizeAsync(new ApiAuthorizationRequest(execution.Installation, execution.Frame, method, null,
                        "execution bootstrap", SupportedApis()), authorizationLifetime.Token).ConfigureAwait(false) != PermissionDecision.Allow)
                        return Limit(ProtocolJson.Error(null, BridgeErrorCodes.PermissionDenied, "The host denied execution data preparation.", true));
            }
            if (HasStorageCapability(grants))
            {
                try
                {
                    using (var timeout = new CancellationTokenSource(_options.CompatibilityBootstrapTimeout))
                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken, execution.Cancellation.Token, timeout.Token))
                    {
                        compatibility["storage"] = await BuildStorageBootstrapAsync(execution, linked.Token)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
                    !execution.Cancellation.IsCancellationRequested)
                {
                    EmitDiagnostic(
                        "MSC410_COMPATIBILITY_BOOTSTRAP_FAILED",
                        "The compatibility storage snapshot timed out.",
                        null,
                        execution);
                }
                catch (Exception exception)
                {
                    EmitDiagnostic(
                        "MSC410_COMPATIBILITY_BOOTSTRAP_FAILED",
                        "The compatibility storage snapshot could not be prepared.",
                        exception,
                        execution);
                }
            }
            if (HasResourceCapability(grants))
            {
                try
                {
                    using (var timeout = new CancellationTokenSource(_options.CompatibilityBootstrapTimeout))
                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken, execution.Cancellation.Token, timeout.Token))
                    {
                        compatibility["resources"] = await BuildResourceBootstrapAsync(execution, linked.Token)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
                    !execution.Cancellation.IsCancellationRequested)
                {
                    EmitDiagnostic(
                        "MSC410_COMPATIBILITY_BOOTSTRAP_FAILED",
                        "The compatibility resource snapshot timed out.",
                        null,
                        execution);
                }
                catch (Exception exception)
                {
                    EmitDiagnostic(
                        "MSC410_COMPATIBILITY_BOOTSTRAP_FAILED",
                        "The compatibility resource snapshot could not be prepared.",
                        exception,
                        execution);
                }
            }
            return Limit(ProtocolJson.Hello(_options, supported, compatibility));
        }

        private static bool HasResourceCapability(IReadOnlyList<string> grants)
        {
            return grants.Contains("GM.getResourceText") || grants.Contains("GM.getResourceUrl");
        }

        private Task<object> BuildResourceBootstrapAsync(
            UserScriptEngine.ExecutionRecord execution,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = ResourcePackage(execution);
            if (Encoding.UTF8.GetByteCount(json) <= _options.MaxResponseBytes / 4)
            {
                using (var document = JsonDocument.Parse(json)) return Task.FromResult<object>(document.RootElement.Clone());
            }
            var chunkSize = Math.Max(1, (_options.MaxResponseBytes - 512) / 6);
            return Task.FromResult<object>(new Dictionary<string, object>
            {
                ["complete"] = false, ["chunkCount"] = (json.Length + chunkSize - 1) / chunkSize,
                ["jsonLength"] = json.Length
            });
        }

        private string ResourcePackage(UserScriptEngine.ExecutionRecord execution)
        {
            foreach (var content in execution.Installation.PreparedRevision.Resources.Values)
                if (content.ByteLength > _options.MaxResourceBytes ||
                    content.Text != null && Encoding.UTF8.GetByteCount(content.Text) > _options.MaxResourceBytes)
                    throw new BridgeProtocolException(BridgeErrorCodes.PayloadTooLarge, "A prepared resource exceeds MaxResourceBytes.");
            var json = execution.Installation.PreparedRevision.ResourcePackageJson;
            if (Encoding.UTF8.GetByteCount(json) > _options.MaxCompatibilityBootstrapBytes)
                throw new BridgeProtocolException(BridgeErrorCodes.PayloadTooLarge,
                    "MSR411_PREPARED_REVISION_LIMIT: increase MaxCompatibilityBootstrapBytes for this complete resource package.");
            return json;
        }

    }
}
