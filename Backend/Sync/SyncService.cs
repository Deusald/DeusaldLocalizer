using DeusaldLocalizerCommon;

namespace DeusaldLocalizerBackend;

/// <summary>
/// Implements the "pull" half of the protocol: given the client's <c>SyncId</c>, return the files
/// that changed since the commit carrying that id, or that the client is already up to date.
/// </summary>
public sealed class SyncService(
    ProjectRegistry registry,
    ProjectSerializer serializer,
    RepoPreparer preparer,
    GitService git,
    AuthService auth)
{
    public async Task<ServiceResult<SyncResponse>> SyncAsync(
        Guid projectId, Guid userId, string token, Guid clientSyncId, CancellationToken ct)
    {
        ProjectConfig? config = registry.Find(projectId);
        if (config == null) return ServiceResult<SyncResponse>.NotFound();

        using IDisposable _ = await serializer.AcquireAsync(projectId, ct);

        string     repoPath = await preparer.ToLatestAsync(config, ct);
        LocProject project  = await preparer.LoadAsync(repoPath);

        LocProjectMember? member = auth.Authenticate(project, userId, token);
        if (member == null) return ServiceResult<SyncResponse>.Unauthorized();

        Guid   currentSyncId = project.Metadata.SyncId;
        string headSha       = await git.RevParseAsync(repoPath, "HEAD", ct);

        // Locate the commit the client last saw.
        string? baseSha = await git.FindCommitByMessageAsync(repoPath, SyncTag.For(clientSyncId), ct);

        if (baseSha == null && clientSyncId != currentSyncId)
            return ServiceResult<SyncResponse>.Ok(await BuildFullResyncAsync(repoPath, currentSyncId, ct));

        if (clientSyncId == currentSyncId ||
            string.Equals(baseSha, headSha, StringComparison.OrdinalIgnoreCase))
            return ServiceResult<SyncResponse>.Ok(new SyncResponse
            {
                Status    = SyncStatus.UpToDate,
                NewSyncId = currentSyncId,
            });

        SyncResponse response = new()
        {
            Status    = SyncStatus.Updated,
            NewSyncId = currentSyncId,
        };

        IReadOnlyList<GitFileChange> diff = await git.DiffNameStatusAsync(repoPath, baseSha!, headSha, ct);
        foreach (GitFileChange change in diff)
        {
            if (change.Status == 'D')
            {
                response.DeletedFiles.Add(NormalizePath(change.Path));
                continue;
            }

            string full = Path.Combine(repoPath, change.Path);
            if (File.Exists(full))
                response.ChangedFiles.Add(new SyncFile
                {
                    Path    = NormalizePath(change.Path),
                    Content = await File.ReadAllTextAsync(full, ct),
                });
        }
        return response.ChangedFiles.Count == 0 && response.DeletedFiles.Count == 0
            ? ServiceResult<SyncResponse>.Ok(new SyncResponse { Status = SyncStatus.UpToDate, NewSyncId = currentSyncId })
            : ServiceResult<SyncResponse>.Ok(response);
    }

    /// <summary>
    /// First-time full download: authenticate the caller by username (a fresh member only holds a
    /// username + one-time token, not their <c>UserId</c>) and return every project file as a
    /// <see cref="SyncStatus.FullResync"/>. This is the entry point for a client "connect to server".
    /// </summary>
    public async Task<ServiceResult<SyncResponse>> BootstrapAsync(
        Guid projectId, string username, string token, CancellationToken ct)
    {
        ProjectConfig? config = registry.Find(projectId);
        if (config == null) return ServiceResult<SyncResponse>.NotFound();

        using IDisposable _ = await serializer.AcquireAsync(projectId, ct);

        string     repoPath = await preparer.ToLatestAsync(config, ct);
        LocProject project  = await preparer.LoadAsync(repoPath);

        LocProjectMember? member = auth.AuthenticateByUsername(project, username, token);
        if (member == null) return ServiceResult<SyncResponse>.Unauthorized();

        return ServiceResult<SyncResponse>.Ok(await BuildFullResyncAsync(repoPath, project.Metadata.SyncId, ct));
    }

    private static async Task<SyncResponse> BuildFullResyncAsync(string repoPath, Guid currentSyncId, CancellationToken ct)
    {
        SyncResponse response = new()
        {
            Status    = SyncStatus.FullResync,
            NewSyncId = currentSyncId,
        };

        foreach (string file in Directory.EnumerateFiles(repoPath, "*.json", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(repoPath, file).Replace('\\', '/');

            // Never ship .git internals or the client-only uncommitted queue.
            if (relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)) continue;
            if (relative.StartsWith(ProjectFileService.UNCOMMITTED_CHANGES_FOLDER + "/", StringComparison.OrdinalIgnoreCase)) continue;

            response.ChangedFiles.Add(new SyncFile
            {
                Path    = relative,
                Content = await File.ReadAllTextAsync(file, ct),
            });
        }
        return response;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
