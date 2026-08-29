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
    /// <summary>
    /// Specifies the result of a host authorization decision.
    /// </summary>
    public enum PermissionDecision
    {
        /// <summary>Allows the requested API operation.</summary>
        Allow,

        /// <summary>Denies the requested API operation.</summary>
        Deny,

        /// <summary>Requires the host to obtain an interactive decision.</summary>
        Prompt
    }

    /// <summary>
    /// Describes a userscript API invocation for host authorization.
    /// </summary>
    public sealed class ApiAuthorizationRequest
    {
        /// <summary>Initializes an API authorization request.</summary>
        /// <param name="installation">The requesting userscript installation.</param>
        /// <param name="frame">The frame from which the request originated.</param>
        /// <param name="method">The requested API method.</param>
        /// <param name="target">The security-sensitive target, such as a URL or resource name.</param>
        /// <param name="parameterSummary">A non-sensitive summary of the request parameters.</param>
        /// <param name="hostCapabilities">The relevant capabilities available from the host.</param>
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

        /// <summary>Gets the requesting userscript installation.</summary>
        public UserScriptInstallation Installation { get; }

        /// <summary>Gets the frame from which the request originated.</summary>
        public DocumentFrame Frame { get; }

        /// <summary>Gets the requested API method.</summary>
        public string Method { get; }

        /// <summary>Gets the security-sensitive target, if one applies.</summary>
        public string Target { get; }

        /// <summary>Gets the non-sensitive request parameter summary.</summary>
        public string ParameterSummary { get; }

        /// <summary>Gets the relevant capabilities available from the host.</summary>
        public IReadOnlyList<string> HostCapabilities { get; }
    }

    /// <summary>
    /// Authorizes userscript API invocations after metadata grant validation.
    /// </summary>
    public interface IUserScriptPermissionPolicy
    {
        /// <summary>Determines whether an API request may proceed.</summary>
        /// <param name="request">The request to authorize.</param>
        /// <param name="cancellationToken">A token that cancels authorization.</param>
        /// <returns>The authorization decision.</returns>
        Task<PermissionDecision> AuthorizeAsync(ApiAuthorizationRequest request, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Allows every API operation that has already passed userscript grant checks.
    /// </summary>
    public sealed class AllowDeclaredPermissionsPolicy : IUserScriptPermissionPolicy
    {
        /// <inheritdoc />
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
