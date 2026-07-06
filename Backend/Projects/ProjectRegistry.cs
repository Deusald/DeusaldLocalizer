using Microsoft.Extensions.Options;

namespace DeusaldLocalizerBackend;

/// <summary>
/// Resolves configured projects and guarantees each one has a local working-tree clone.
/// </summary>
public sealed class ProjectRegistry
{
    private readonly BotOptions               _Options;
    private readonly GitService               _Git;
    private readonly ILogger<ProjectRegistry> _Logger;

    public ProjectRegistry(IOptions<BotOptions> options, GitService git, ILogger<ProjectRegistry> logger)
    {
        _Options = options.Value;
        _Git     = git;
        _Logger  = logger;
    }

    public BotOptions Options => _Options;

    public ProjectConfig? Find(Guid projectId) =>
        _Options.Projects.FirstOrDefault(p => p.ProjectId == projectId);

    public string LocalPath(ProjectConfig config) =>
        Path.Combine(_Options.ReposRoot, config.Slug);

    /// <summary>
    /// Ensures the project is cloned locally and returns its working-tree path. Must be called
    /// under the project lock (it may create/replace directories).
    /// </summary>
    public async Task<string> EnsureClonedAsync(ProjectConfig config, CancellationToken ct)
    {
        string path   = LocalPath(config);
        string gitDir = Path.Combine(path, ".git");

        if (Directory.Exists(gitDir)) return path;

        Directory.CreateDirectory(_Options.ReposRoot);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true); // stale non-git dir

        _Logger.LogInformation("Cloning project '{Slug}' into {Path}", config.Slug, path);
        await _Git.CloneAsync(config.RemoteUrl, path, ct);
        return path;
    }
}
