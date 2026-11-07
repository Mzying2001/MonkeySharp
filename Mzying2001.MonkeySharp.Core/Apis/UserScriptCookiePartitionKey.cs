using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Apis
{
    /// <summary>Determines whether an omitted ancestor bit is a filter, set default, or delete default.</summary>
    public enum CookiePartitionOperation
    {
        /// <summary>Lists both ancestor variants when the bit is omitted.</summary>
        List,
        /// <summary>Derives the bit from the cookie URL and selected site.</summary>
        Set,
        /// <summary>Selects the cross-site ancestor variant by default, matching the reference remove API.</summary>
        Delete
    }
    /// <summary>A partition filter or identity; an omitted site selects all partitions when listing.</summary>
    public sealed class UserScriptCookiePartitionKey
    {
        /// <summary>Creates a partition selector. An empty site explicitly selects unpartitioned cookies.</summary>
        public UserScriptCookiePartitionKey(string topLevelSite = null, bool? hasCrossSiteAncestor = null)
        {
            if (topLevelSite == null && hasCrossSiteAncestor.HasValue || topLevelSite == string.Empty && hasCrossSiteAncestor == true)
                throw new ArgumentException("An ancestor selector requires a nonempty topLevelSite.");
            if (!string.IsNullOrEmpty(topLevelSite) && (!Uri.TryCreate(topLevelSite, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
                throw new ArgumentException("topLevelSite must be an HTTP(S) site.");
            TopLevelSite = topLevelSite;
            HasCrossSiteAncestor = hasCrossSiteAncestor;
        }
        /// <summary>Gets the site, null for all partitions, or empty for explicit unpartitioned selection.</summary>
        public string TopLevelSite { get; }
        /// <summary>Gets the optional ancestor constraint supplied or observed by the backend.</summary>
        public bool? HasCrossSiteAncestor { get; }
        /// <summary>Tests a normalized filter against an actual cookie identity.</summary>
        public bool Matches(UserScriptCookiePartitionKey actual)
        {
            if (TopLevelSite == null) return true;
            if (TopLevelSite.Length == 0) return actual == null;
            return actual != null && string.Equals(TopLevelSite, actual.TopLevelSite, StringComparison.Ordinal) &&
                (!HasCrossSiteAncestor.HasValue || HasCrossSiteAncestor == actual.HasCrossSiteAncestor);
        }
        /// <summary>Compares complete, normalized partition identities.</summary>
        public static bool SameIdentity(UserScriptCookiePartitionKey left, UserScriptCookiePartitionKey right) =>
            left == null ? right == null : right != null && left.TopLevelSite == right.TopLevelSite && left.HasCrossSiteAncestor == right.HasCrossSiteAncestor;
    }

    /// <summary>Advertises actual partition support and normalizes selectors before any Cookie effects.</summary>
    public interface IPartitionedCookieService : ICookieService
    {
        /// <summary>Validates engine support and returns canonical site/ancestor semantics for this operation.</summary>
        Task<UserScriptCookiePartitionKey> NormalizePartitionKeyAsync(UserScriptCookiePartitionKey partitionKey,
            Uri cookieUrl, CookiePartitionOperation operation, CancellationToken cancellationToken);
    }
}
