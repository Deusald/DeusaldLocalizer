using JetBrains.Annotations;

namespace DeusaldLocalizerBackend;

/// <summary>
/// Bound from the <c>Cors</c> section of configuration. Controls which browser origins may call the bot.
/// Only the web client needs this — the desktop app uses a native <c>HttpClient</c> and is not subject to
/// the browser's same-origin policy.
/// </summary>
[PublicAPI]
public sealed class CorsOptions
{
    public const string SECTION_NAME = "Cors";

    /// <summary>
    /// Browser origins allowed to call the API (scheme + host + port, no trailing slash), e.g.
    /// <c>http://localhost:5047</c> or <c>https://deusald.github.io</c>. Empty disables CORS entirely.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>
    /// When true, the bot answers Chrome's Private Network Access preflight so a project served from a
    /// public HTTPS origin (e.g. GitHub Pages) may reach a backend running on the local machine. Leave
    /// off unless you actually hit a localhost backend from a deployed web build.
    /// </summary>
    public bool AllowPrivateNetwork { get; set; }
}
