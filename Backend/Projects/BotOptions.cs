using JetBrains.Annotations;

namespace DeusaldLocalizerBackend;

/// <summary>Bound from the <c>Bot</c> section of configuration.</summary>
[PublicAPI]
public sealed class BotOptions
{
    public const string SECTION_NAME = "Bot";

    /// <summary>Root directory that holds one working-tree clone per project.</summary>
    public string ReposRoot { get; set; } = "repos";

    /// <summary>Git committer identity used for every commit the bot makes (author is the member).</summary>
    public string CommitterName  { get; set; } = "Deusald Localizer Bot";
    public string CommitterEmail { get; set; } = "bot@localizer";

    public List<ProjectConfig> Projects { get; set; } = new();
}

/// <summary>One managed project: its identity plus where to clone it from and which branch to track.</summary>
[PublicAPI]
public sealed class ProjectConfig
{
    public Guid   ProjectId { get; set; }
    public string Slug      { get; set; } = string.Empty;
    public string RemoteUrl { get; set; } = string.Empty;
    public string Branch    { get; set; } = "main";
}
