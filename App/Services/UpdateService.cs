using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace App
{
    /// <summary>
    /// Details of a newer release found on GitHub.
    /// </summary>
    public sealed record UpdateInfo(string LatestVersion, string ReleaseUrl);

    /// <summary>
    /// Checks GitHub Releases for a version newer than the running build and, if found,
    /// returns where to download it. Never throws to the caller — offline, rate-limited,
    /// or malformed responses simply yield <c>null</c> (no update).
    /// </summary>
    [PublicAPI]
    public sealed class UpdateService(HttpClient http)
    {
        private const string _OWNER = "Deusald";
        private const string _REPO  = "DeusaldLocalizer";

        private static readonly JsonSerializerOptions _Json = new(JsonSerializerDefaults.Web);

        public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
        {
            try
            {
                using HttpRequestMessage request = new(HttpMethod.Get,
                    $"https://api.github.com/repos/{_OWNER}/{_REPO}/releases/latest");
                // GitHub rejects requests without a User-Agent; the other headers pin the API version.
                request.Headers.TryAddWithoutValidation("User-Agent", _REPO);
                request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
                request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

                using HttpResponseMessage response = await http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode) return null;

                GitHubRelease? release = await response.Content.ReadFromJsonAsync<GitHubRelease>(_Json, ct);
                if (release?.TagName is null || release.HtmlUrl is null) return null;
                if (!IsNewer(release.TagName, BuildInfo.Version)) return null;

                return new UpdateInfo(release.TagName.TrimStart('v', 'V'), release.HtmlUrl);
            }
            catch
            {
                // Offline, DNS failure, rate-limited, or unparseable body — treat as "no update".
                return null;
            }
        }

        private static bool IsNewer(string latestTag, string current)
        {
            if (!Version.TryParse(latestTag.TrimStart('v', 'V'), out Version? latest)) return false;
            if (!Version.TryParse(current,                       out Version? cur))    return false;
            return latest > cur;
        }

        private sealed class GitHubRelease
        {
            [JsonPropertyName("tag_name")] public string? TagName { get; set; }
            [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        }
    }
}
