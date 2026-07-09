using System.Reflection;

namespace DeusaldLocalizerWeb
{
    /// <summary>
    /// Exposes the build's version and git commit hash, parsed once from the
    /// assembly's informational version (formatted as "1.0.0+abc1234").
    /// Mirrors the desktop App's BuildInfo so both clients show the same footer.
    /// </summary>
    public static class BuildInfo
    {
        /// <summary>The semantic version, e.g. "1.0.0".</summary>
        public static string Version { get; }

        /// <summary>The short git commit hash, or empty when built without git.</summary>
        public static string CommitHash { get; }

        static BuildInfo()
        {
            string informational = typeof(BuildInfo).Assembly
                                                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                                                   ?.InformationalVersion
                                 ?? "0.0.0";

            int plus = informational.IndexOf('+');
            if (plus >= 0)
            {
                Version    = informational[..plus];
                CommitHash = informational[(plus + 1)..];
            }
            else
            {
                Version    = informational;
                CommitHash = string.Empty;
            }
        }
    }
}
