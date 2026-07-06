using DeusaldLocalizerCommon;

namespace DeusaldLocalizerBackend;

/// <summary>
/// Shared "get the repo to the latest remote state and load it" step used by both sync and push.
/// Must be called under the project lock.
/// </summary>
public sealed class RepoPreparer
{
    private readonly ProjectRegistry _Registry;
    private readonly GitService      _Git;

    public RepoPreparer(ProjectRegistry registry, GitService git)
    {
        _Registry = registry;
        _Git      = git;
    }

    /// <summary>Ensures a clone exists, hard-resets it to the latest remote branch, and returns its path.</summary>
    public async Task<string> ToLatestAsync(ProjectConfig config, CancellationToken ct)
    {
        string repoPath = await _Registry.EnsureClonedAsync(config, ct);
        await _Git.FetchAsync(repoPath, ct);
        await _Git.ResetHardAsync(repoPath, $"origin/{config.Branch}", ct);
        await _Git.CleanAsync(repoPath, ct);
        return repoPath;
    }

    /// <summary>Loads the project from a prepared repo path.</summary>
    public Task<LocProject> LoadAsync(string repoPath) => ProjectFileService.OpenAsync(repoPath);
}
