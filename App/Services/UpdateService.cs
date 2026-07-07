using JetBrains.Annotations;
using Velopack;
using Velopack.Sources;

namespace App
{
    /// <summary>
    /// Details of a newer release found on GitHub.
    /// </summary>
    public sealed record UpdateInfo(string LatestVersion, string? ReleaseNotesHtml);

    /// <summary>
    /// Wraps Velopack's <see cref="UpdateManager"/> to check GitHub Releases for a newer build and,
    /// on request, download it and relaunch into it. Never throws to the caller — offline, rate-limited,
    /// or not-installed (dev / portable) runs simply yield <c>null</c> (no update).
    /// </summary>
    [PublicAPI]
    public sealed class UpdateService
    {
        private const string _REPO_URL = "https://github.com/Deusald/DeusaldLocalizer";

        // Local-test hook: set this env var to a folder (or URL) containing a Velopack release
        // (releases.win.json + .nupkg) to update from there instead of GitHub. Unset in production.
        private const string _SOURCE_OVERRIDE_ENV = "DEUSALD_UPDATE_SOURCE";

        private readonly UpdateManager _Manager = CreateManager();

        private static UpdateManager CreateManager()
        {
            string? overrideSource = Environment.GetEnvironmentVariable(_SOURCE_OVERRIDE_ENV);
            return string.IsNullOrWhiteSpace(overrideSource)
                ? new UpdateManager(new GithubSource(_REPO_URL, accessToken: null, prerelease: false))
                : new UpdateManager(overrideSource);
        }

        // The Velopack update descriptor from the last successful check, needed to download / apply.
        private Velopack.UpdateInfo? _Pending;

        /// <summary>
        /// Returns info about a newer release, or <c>null</c> when up to date, offline, or the app is
        /// not a Velopack install (e.g. running from the IDE) — in which case in-app update is disabled.
        /// </summary>
        public async Task<UpdateInfo?> CheckForUpdateAsync()
        {
            try
            {
                // Not a Velopack install (debug / portable run) — nothing to update in place.
                if (!_Manager.IsInstalled) return null;

                Velopack.UpdateInfo? updates = await _Manager.CheckForUpdatesAsync();
                if (updates is null) return null;

                _Pending = updates;
                VelopackAsset target = updates.TargetFullRelease;
                return new UpdateInfo(target.Version.ToString(), target.NotesHTML);
            }
            catch
            {
                // Offline, DNS failure, rate-limited, or unparseable feed — treat as "no update".
                return null;
            }
        }

        /// <summary>
        /// Downloads the pending update and relaunches into it. On success the current process exits
        /// and does not return; returns <c>false</c> only when there is nothing to apply or the
        /// download failed (e.g. connection lost between the check and the download).
        /// </summary>
        public async Task<bool> DownloadAndApplyAsync(Action<int>? progress = null)
        {
            if (_Pending is null) return false;
            try
            {
                await _Manager.DownloadUpdatesAsync(_Pending, progress);
                _Manager.ApplyUpdatesAndRestart(_Pending); // relaunches into the new version; does not return
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
