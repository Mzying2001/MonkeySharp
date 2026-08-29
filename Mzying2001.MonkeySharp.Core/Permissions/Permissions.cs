using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Runtime;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Permissions
{
    public enum PermissionDecision
    {
        Allow,
        Deny,
        Prompt
    }

    public sealed class ApiAuthorizationRequest
    {
        public ApiAuthorizationRequest(
            UserScriptInstallation installation,
            DocumentFrame frame,
            string method,
            string target,
            string parameterSummary,
            IEnumerable<string> hostCapabilities)
        {
            Installation = installation ?? throw new ArgumentNullException(nameof(installation));
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            Method = method ?? throw new ArgumentNullException(nameof(method));
            Target = target;
            ParameterSummary = parameterSummary;
            HostCapabilities = new ReadOnlyCollection<string>(hostCapabilities.ToList());
        }

        public UserScriptInstallation Installation { get; }
        public DocumentFrame Frame { get; }
        public string Method { get; }
        public string Target { get; }
        public string ParameterSummary { get; }
        public IReadOnlyList<string> HostCapabilities { get; }
    }

    public interface IUserScriptPermissionPolicy
    {
        Task<PermissionDecision> AuthorizeAsync(ApiAuthorizationRequest request, CancellationToken cancellationToken);
    }

    public sealed class AllowDeclaredPermissionsPolicy : IUserScriptPermissionPolicy
    {
        public Task<PermissionDecision> AuthorizeAsync(
            ApiAuthorizationRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(PermissionDecision.Allow);
        }
    }
}
