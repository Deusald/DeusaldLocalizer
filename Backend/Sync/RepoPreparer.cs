using DeusaldLocalizerCommon;

namespace DeusaldLocalizerBackend;

/// <summary>
/// Shared "get the repo to the latest remote state and load it" step used by both sync and push.
/// Must be called under the project lock.
/// </summary>
public sealed class RepoPreparer(ProjectRegistry registry, GitService git)
{
    /// <summary>Ensures a clone exists, hard-resets it to the latest remote branch, and returns its path.</summary>
    public async Task<string> ToLatestAsync(ProjectConfig config, CancellationToken ct)
    {
        string repoPath = await registry.EnsureClonedAsync(config, ct);
        await git.FetchAsync(repoPath, ct);
        await git.ResetHardAsync(repoPath, $"origin/{config.Branch}", ct);
        await git.CleanAsync(repoPath, ct);
        return repoPath;
    }

    /// <summary>Loads the project from a prepared repo path.</summary>
    public Task<LocProject> LoadAsync(string repoPath) => ProjectFileService.OpenAsync(repoPath);
}
