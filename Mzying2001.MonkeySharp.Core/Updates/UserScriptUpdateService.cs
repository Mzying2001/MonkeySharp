using Mzying2001.MonkeySharp.Core.Domain;
using Mzying2001.MonkeySharp.Core.Parsing;
using Mzying2001.MonkeySharp.Core.Repository;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mzying2001.MonkeySharp.Core.Updates
{
    /// <summary>Describes the outcome of checking one installed userscript for an update.</summary>
    public enum UserScriptUpdateStatus
    {
        /// <summary>The script has no version or update source to check.</summary>
        NotConfigured,

        /// <summary>The script explicitly disabled updates with <c>@downloadURL none</c>.</summary>
        Disabled,

        /// <summary>The remote version is not newer than the installed version.</summary>
        UpToDate,

        /// <summary>A newer version is available for explicit installation.</summary>
        Available
    }

    /// <summary>Provides userscript source from an update URL.</summary>
    public interface IUserScriptUpdateFetcher
    {
        /// <summary>Fetches the complete userscript source from an absolute HTTP(S) URL.</summary>
        Task<string> FetchSourceAsync(
            UserScriptInstallation installation,
            string url,
            CancellationToken cancellationToken);
    }

    /// <summary>Contains an immutable update-check result tied to one repository revision.</summary>
    public sealed class UserScriptUpdateCheckResult
    {
        internal UserScriptUpdateCheckResult(
            ScriptKey scriptKey,
            UserScriptUpdateStatus status,
            string currentVersion,
            string availableVersion,
            string checkUrl,
            string downloadUrl,
            Guid expectedRevisionId,
            DateTimeOffset expectedUpdatedAt,
            DateTimeOffset checkedAt)
        {
            ScriptKey = scriptKey;
            Status = status;
            CurrentVersion = currentVersion;
            AvailableVersion = availableVersion;
            CheckUrl = checkUrl;
            DownloadUrl = downloadUrl;
            ExpectedRevisionId = expectedRevisionId;
            ExpectedUpdatedAt = expectedUpdatedAt;
            CheckedAt = checkedAt;
        }

        /// <summary>Gets the checked installation.</summary>
        public ScriptKey ScriptKey { get; }

        /// <summary>Gets the check outcome.</summary>
        public UserScriptUpdateStatus Status { get; }

        /// <summary>Gets the installed version at check time.</summary>
        public string CurrentVersion { get; }

        /// <summary>Gets the version reported by the update source.</summary>
        public string AvailableVersion { get; }

        /// <summary>Gets the URL used to inspect update metadata.</summary>
        public string CheckUrl { get; }

        /// <summary>Gets the URL from which a confirmed update is downloaded.</summary>
        public string DownloadUrl { get; }

        /// <summary>Gets the immutable repository revision captured before the network check.</summary>
        public Guid ExpectedRevisionId { get; }

        /// <summary>Gets the repository revision timestamp captured before the network check.</summary>
        public DateTimeOffset ExpectedUpdatedAt { get; }

        /// <summary>Gets the time at which the remote source was checked.</summary>
        public DateTimeOffset CheckedAt { get; }
    }

    /// <summary>Checks userscript update metadata and applies a confirmed update atomically.</summary>
    public sealed class UserScriptUpdateService
    {
        /// <summary>Update checks expire after ten minutes.</summary>
        public static readonly TimeSpan CheckResultLifetime = TimeSpan.FromMinutes(10);

        private readonly IUserScriptRepository _repository;
        private readonly IUserScriptUpdateFetcher _fetcher;
        private readonly IUserScriptMetadataParser _parser;
        private readonly TampermonkeyVersionComparer _versions;
        private readonly Func<DateTimeOffset> _utcNow;

        /// <summary>Initializes the update module with repository and HTTP fetch interfaces.</summary>
        public UserScriptUpdateService(
            IUserScriptRepository repository,
            IUserScriptUpdateFetcher fetcher,
            IUserScriptMetadataParser parser = null,
            TampermonkeyVersionComparer versionComparer = null,
            Func<DateTimeOffset> utcNow = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
            _parser = parser ?? new UserScriptMetadataParser();
            _versions = versionComparer ?? new TampermonkeyVersionComparer();
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>Checks the selected installation and returns a result tied to its current revision.</summary>
        public async Task<UserScriptUpdateCheckResult> CheckAsync(
            ScriptKey scriptKey,
            CancellationToken cancellationToken)
        {
            var installation = await _repository.GetAsync(scriptKey, cancellationToken).ConfigureAwait(false);
            var metadata = installation.Definition.Metadata;
            var now = _utcNow();
            if (string.Equals(metadata.DownloadUrl?.Trim(), "none", StringComparison.OrdinalIgnoreCase))
                return Result(installation, UserScriptUpdateStatus.Disabled, metadata.Version, null, null, null, now);

            var checkUrl = FirstNonEmpty(metadata.UpdateUrl, metadata.DownloadUrl);
            if (string.IsNullOrWhiteSpace(metadata.Version) || string.IsNullOrWhiteSpace(checkUrl))
                return Result(installation, UserScriptUpdateStatus.NotConfigured, metadata.Version, null, null, null, now);

            checkUrl = ValidateUrl(checkUrl);
            var downloadUrl = ValidateUrl(FirstNonEmpty(metadata.DownloadUrl, checkUrl));
            var source = await _fetcher.FetchSourceAsync(installation, checkUrl, cancellationToken).ConfigureAwait(false);
            var remote = ParseSource(source);
            EnsureIdentity(metadata, remote.Metadata);
            if (string.IsNullOrWhiteSpace(remote.Metadata.Version))
                throw new UserScriptUpdateException("MSU003_VERSION_MISSING", "The update source does not declare @version.");

            var status = _versions.Compare(remote.Metadata.Version, metadata.Version) > 0
                ? UserScriptUpdateStatus.Available
                : UserScriptUpdateStatus.UpToDate;
            return Result(installation, status, metadata.Version,
                status == UserScriptUpdateStatus.Available ? remote.Metadata.Version : null,
                checkUrl, downloadUrl, now);
        }

        /// <summary>Downloads and installs a confirmed update if the result is current and has not expired.</summary>
        public async Task<UserScriptInstallation> ApplyAsync(
            UserScriptUpdateCheckResult result,
            CancellationToken cancellationToken)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (result.Status != UserScriptUpdateStatus.Available)
                throw new UserScriptUpdateException("MSU004_UPDATE_NOT_AVAILABLE", "The check result does not contain an available update.");

            var now = _utcNow();
            if (result.CheckedAt > now || now - result.CheckedAt > CheckResultLifetime)
                throw new UserScriptUpdateException("MSU005_STALE_CHECK", "The update check has expired; check again before installing.");

            var installation = await _repository.GetAsync(result.ScriptKey, cancellationToken).ConfigureAwait(false);
            if (installation.RevisionId != result.ExpectedRevisionId ||
                installation.UpdatedAt != result.ExpectedUpdatedAt ||
                !string.Equals(installation.Definition.Metadata.Version, result.CurrentVersion, StringComparison.Ordinal))
                throw new UserScriptUpdateException("MSU005_STALE_CHECK", "The installed script changed after the update check.");

            var source = await _fetcher.FetchSourceAsync(installation, result.DownloadUrl, cancellationToken).ConfigureAwait(false);
            var downloaded = ParseSource(source);
            EnsureIdentity(installation.Definition.Metadata, downloaded.Metadata);
            if (string.IsNullOrWhiteSpace(downloaded.Metadata.Version) ||
                _versions.Compare(downloaded.Metadata.Version, result.CurrentVersion) <= 0)
                throw new UserScriptUpdateException("MSU006_DOWNLOAD_VERSION_INVALID", "The downloaded script is not newer than the installed version.");
            if (_versions.Compare(downloaded.Metadata.Version, result.AvailableVersion) < 0)
                throw new UserScriptUpdateException("MSU007_DOWNLOAD_VERSION_OUTDATED", "The download URL returned an older version than the update source reported.");

            try
            {
                return await _repository.UpdateIfUnchangedAsync(
                    result.ScriptKey,
                    result.ExpectedRevisionId,
                    source,
                    result.DownloadUrl,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (RepositoryRevisionMismatchException exception)
            {
                throw new UserScriptUpdateException("MSU005_STALE_CHECK", "The installed script changed before the update could be committed.", exception);
            }
        }

        private UserScriptUpdateCheckResult Result(
            UserScriptInstallation installation,
            UserScriptUpdateStatus status,
            string currentVersion,
            string availableVersion,
            string checkUrl,
            string downloadUrl,
            DateTimeOffset now)
        {
            return new UserScriptUpdateCheckResult(
                installation.ScriptKey,
                status,
                currentVersion,
                availableVersion,
                checkUrl,
                downloadUrl,
                installation.RevisionId,
                installation.UpdatedAt,
                now);
        }

        private MetadataParseResult ParseSource(string source)
        {
            if (source == null)
                throw new UserScriptUpdateException("MSU008_SOURCE_MISSING", "The update source returned no script content.");
            var parsed = _parser.Parse(source);
            if (!parsed.CanEnable)
                throw new MetadataValidationException(parsed);
            return parsed;
        }

        private static void EnsureIdentity(UserScriptMetadata current, UserScriptMetadata candidate)
        {
            if (string.IsNullOrWhiteSpace(current.Name) || string.IsNullOrWhiteSpace(current.Namespace) ||
                !string.Equals(current.Name, candidate.Name, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(current.Namespace, candidate.Namespace, StringComparison.OrdinalIgnoreCase))
                throw new UserScriptUpdateException("MSU002_IDENTITY_MISMATCH", "The update source does not identify the same script by @name and @namespace.");
        }

        private static string FirstNonEmpty(string first, string second)
        {
            return !string.IsNullOrWhiteSpace(first) ? first.Trim() : second?.Trim();
        }

        private static string ValidateUrl(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") ||
                string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
                throw new UserScriptUpdateException("MSU001_INVALID_URL", "Update URLs must be absolute HTTP(S) URLs without embedded credentials.");
            return uri.AbsoluteUri;
        }
    }

    /// <summary>Indicates that update metadata, sources, or repository revisions failed validation.</summary>
    public sealed class UserScriptUpdateException : UserScriptException
    {
        /// <summary>Initializes an update failure.</summary>
        public UserScriptUpdateException(string code, string message) : base(message)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
        }

        /// <summary>Initializes an update failure with its underlying cause.</summary>
        public UserScriptUpdateException(string code, string message, Exception innerException) : base(message, innerException)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
        }

        /// <summary>Gets the stable update error code.</summary>
        public string Code { get; }
    }
}
