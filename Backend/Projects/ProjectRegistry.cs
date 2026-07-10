using Microsoft.Extensions.Options;

namespace DeusaldLocalizerBackend;

/// <summary>
/// Resolves configured projects and guarantees each one has a local working-tree clone.
/// </summary>
public sealed class ProjectRegistry(IOptions<BotOptions> options, GitService git, ILogger<ProjectRegistry> logger)
{
    private readonly BotOptions _Options = options.Value;

    public BotOptions Options => _Options;

    public ProjectConfig? Find(Guid projectId) => _Options.Projects.FirstOrDefault(p => p.ProjectId == projectId);

    private string LocalPath(ProjectConfig config) => Path.Combine(_Options.ReposRoot, config.Slug);

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

        logger.LogInformation("Cloning project '{Slug}' into {Path}", config.Slug, path);
        await git.CloneAsync(config.RemoteUrl, path, ct);
        return path;
    }
}