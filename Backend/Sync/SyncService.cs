using DeusaldLocalizerCommon;

namespace DeusaldLocalizerBackend;

/// <summary>
/// Implements the "pull" half of the protocol: given the client's <c>SyncId</c>, return the files
/// that changed since the commit carrying that id, or that the client is already up to date.
/// </summary>
public sealed class SyncService
{
    private readonly ProjectRegistry   _Registry;
    private readonly ProjectSerializer _Serializer;
    private readonly RepoPreparer      _Preparer;
    private readonly GitService        _Git;
    private readonly AuthService       _Auth;

    public SyncService(
        ProjectRegistry registry, ProjectSerializer serializer, RepoPreparer preparer,
        GitService git, AuthService auth)
    {
        _Registry   = registry;
        _Serializer = serializer;
        _Preparer   = preparer;
        _Git        = git;
        _Auth       = auth;
    }

    public async Task<ServiceResult<SyncResponse>> SyncAsync(
        Guid projectId, Guid userId, string token, Guid clientSyncId, CancellationToken ct)
    {
        ProjectConfig? config = _Registry.Find(projectId);
        if (config == null) return ServiceResult<SyncResponse>.NotFound();

        using IDisposable _ = await _Serializer.AcquireAsync(projectId, ct);

        string     repoPath = await _Preparer.ToLatestAsync(config, ct);
        LocProject project  = await _Preparer.LoadAsync(repoPath);

        LocProjectMember? member = _Auth.Authenticate(project, userId, token);
        if (member == null) return ServiceResult<SyncResponse>.Unauthorized();

        Guid   currentSyncId = project.Metadata.SyncId;
        string headSha       = await _Git.RevParseAsync(repoPath, "HEAD", ct);

        // Locate the commit the client last saw.
        string? baseSha = await _Git.FindCommitByMessageAsync(repoPath, SyncTag.For(clientSyncId), ct);

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

        IReadOnlyList<GitFileChange> diff = await _Git.DiffNameStatusAsync(repoPath, baseSha!, headSha, ct);
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
