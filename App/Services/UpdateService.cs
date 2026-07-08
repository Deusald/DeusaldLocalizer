using JetBrains.Annotations;
#if !MACCATALYST
using Velopack;
using Velopack.Sources;
#else
using System.Text.Json;
#endif

namespace App
{
    /// <summary>
    /// Details of a newer release found on GitHub. When <see cref="DownloadPageUrl"/> is set, the app
    /// cannot update itself (macOS) and the UI should open that page for a manual download; otherwise
    /// the update is applied in place (Windows, via Velopack).
    /// </summary>
    public sealed record UpdateInfo(string LatestVersion, string? ReleaseNotesHtml, string? DownloadPageUrl = null);

    /// <summary>
    /// Checks GitHub Releases for a newer build. On Windows it wraps Velopack's <see cref="UpdateManager"/>
    /// to download and relaunch in place. On macOS in-place update is not supported, so it only detects a
    /// newer release via the GitHub API and hands the UI the releases page to download manually. Never
    /// throws to the caller — offline, rate-limited, or not-installed runs simply yield <c>null</c>.
    /// </summary>
    [PublicAPI]
    public sealed class UpdateService
    {
        private const string _REPO_URL = "https://github.com/Deusald/DeusaldLocalizer";

#if MACCATALYST
        // ── macOS ────────────────────────────────────────────────────────────────
        // Velopack's in-place update doesn't work under Mac Catalyst, so we only detect a newer
        // release through the GitHub API and send the user to the releases page to download it by hand.
        private const string _RELEASES_API  = "https://api.github.com/repos/Deusald/DeusaldLocalizer/releases/latest";
        private const string _RELEASES_PAGE = _REPO_URL + "/releases/latest";

        // Dedicated client so the shared app HttpClient's base address / headers can't affect the call.
        private static readonly HttpClient _Http = new();

        public async Task<UpdateInfo?> CheckForUpdateAsync()
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _RELEASES_API);
                request.Headers.UserAgent.ParseAdd("DeusaldLocalizer");
                request.Headers.Accept.ParseAdd("application/vnd.github+json");

                using HttpResponseMessage response = await _Http.SendAsync(request);
                if (!response.IsSuccessStatusCode) return null;

                await using Stream stream = await response.Content.ReadAsStreamAsync();
                using JsonDocument doc = await JsonDocument.ParseAsync(stream);

                if (!doc.RootElement.TryGetProperty("tag_name", out JsonElement tagElement)) return null;
                string? tag = tagElement.GetString();
                if (string.IsNullOrWhiteSpace(tag)) return null;

                if (!IsNewer(tag, BuildInfo.Version)) return null;

                string page = doc.RootElement.TryGetProperty("html_url", out JsonElement urlElement)
                           && urlElement.GetString() is { Length: > 0 } url
                                  ? url
                                  : _RELEASES_PAGE;

                return new UpdateInfo(tag.TrimStart('v', 'V'), ReleaseNotesHtml: null, DownloadPageUrl: page);
            }
            catch
            {
                // Offline, DNS failure, rate-limited, or unparseable response — treat as "no update".
                return null;
            }
        }

        // Compares dotted numeric versions ("1.2.4" vs "1.2.3"), tolerating a leading 'v'.
        private static bool IsNewer(string candidate, string current)
        {
            return Version.TryParse(candidate.TrimStart('v', 'V'), out Version? c)
                && Version.TryParse(current.TrimStart('v', 'V'),   out Version? cur)
                && c > cur;
        }

        // Never used on macOS — the UI opens DownloadPageUrl in the browser instead. Present so the
        // service exposes the same surface on both platforms.
        public Task<bool> DownloadAndApplyAsync(Action<int>? progress = null) => Task.FromResult(false);
#else
        // ── Windows ──────────────────────────────────────────────────────────────
        // Local-test hook: set this env var to a folder (or URL) containing a Velopack release
        // (releases.win.json + .nupkg) to update from there instead of GitHub. Unset in production.
        private const string _SOURCE_OVERRIDE_ENV = "DEUSALD_UPDATE_SOURCE";

        // Null only when the UpdateManager cannot be constructed (e.g. not a Velopack install). The
        // try/catch degrades to "updates unavailable" rather than crashing startup.
        private readonly UpdateManager? _Manager = CreateManager();

        private static UpdateManager? CreateManager()
        {
            try
            {
                string? overrideSource = Environment.GetEnvironmentVariable(_SOURCE_OVERRIDE_ENV);
                return string.IsNullOrWhiteSpace(overrideSource)
                    ? new UpdateManager(new GithubSource(_REPO_URL, accessToken: null, prerelease: false))
                    : new UpdateManager(overrideSource);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateManager unavailable: {ex.Message}");
                return null;
            }
        }

        // The Velopack update descriptor from the last successful check, needed to download / apply.
        private Velopack.UpdateInfo? _Pending;

        /// <summary>
        /// Returns info about a newer release, or <c>null</c> when up to date, offline, or the app is
        /// not a Velopack install (e.g. running from the IDE) — in which case in-app update is disabled.
        /// </summary>
        public async Task<UpdateInfo?> CheckForUpdateAsync()
        {
            if (_Manager is null) return null;

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
            if (_Manager is null || _Pending is null) return false;
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
#endif
    }
}
