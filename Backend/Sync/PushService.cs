using DeusaldLocalizerCommon;

namespace DeusaldLocalizerBackend;

/// <summary>
/// Implements the "push" half: apply the client's pending changes as one commit each, then push.
/// Fetches the latest remote first; validates each change against the freshly-pulled state
/// (defense-in-depth); and if the remote moves during processing, discards everything.
/// </summary>
public sealed class PushService
{
    private readonly ProjectRegistry     _Registry;
    private readonly ProjectSerializer   _Serializer;
    private readonly RepoPreparer        _Preparer;
    private readonly GitService          _Git;
    private readonly AuthService         _Auth;
    private readonly ILogger<PushService> _Logger;

    public PushService(
        ProjectRegistry registry, ProjectSerializer serializer, RepoPreparer preparer,
        GitService git, AuthService auth, ILogger<PushService> logger)
    {
        _Registry   = registry;
        _Serializer = serializer;
        _Preparer   = preparer;
        _Git        = git;
        _Auth       = auth;
        _Logger     = logger;
    }

    public async Task<ServiceResult<PushResponse>> PushAsync(
        Guid projectId, Guid userId, string token, IReadOnlyList<LocEntryChange> changes, CancellationToken ct)
    {
        ProjectConfig? config = _Registry.Find(projectId);
        if (config == null) return ServiceResult<PushResponse>.NotFound();

        using IDisposable _ = await _Serializer.AcquireAsync(projectId, ct);

        string     repoPath = await _Preparer.ToLatestAsync(config, ct);
        LocProject project  = await _Preparer.LoadAsync(repoPath);

        LocProjectMember? member = _Auth.Authenticate(project, userId, token);
        if (member == null) return ServiceResult<PushResponse>.Unauthorized();

        // Defense-in-depth: validate against the pristine, freshly-pulled project before mutating it.
        List<EntryChangeConflict> conflicts = EntryChangeConflictService.Validate(project, changes);
        if (conflicts.Count > 0)
            return ServiceResult<PushResponse>.Ok(new PushResponse
            {
                Status    = PushStatus.Conflict,
                Conflicts = conflicts,
                Message   = "Some changes conflict with newer edits on the server. Sync and resolve them first.",
            });

        Guid   newSyncId = Guid.NewGuid();
        string syncTag   = SyncTag.For(newSyncId);
        string baseSha   = await _Git.RevParseAsync(repoPath, "HEAD", ct);

        string authorEmail    = $"{member.UserId}@localizer";
        string committerName  = _Registry.Options.CommitterName;
        string committerEmail = _Registry.Options.CommitterEmail;

        try
        {
            for (int x = 0; x < changes.Count; ++x)
            {
                LocEntryChange change = changes[x];
                EntryChangeExeService.ExecuteChange(project, change, out string commitString);
                if (string.IsNullOrEmpty(commitString)) commitString = $"Apply {change.Type}";

                await ProjectFileService.WriteEntityForChangeAsync(project, repoPath, change);

                // Fold the sync-id bump into the batch's final change so it lands as one commit.
                if (x == changes.Count - 1)
                {
                    project.Metadata.SyncId    = newSyncId;
                    project.Metadata.UpdatedAt = DateTime.UtcNow;
                    await ProjectFileService.SaveMetadataOnlyAsync(project, repoPath);
                }

                await _Git.StageAllAsync(repoPath, ct);
                await _Git.CommitAsync(repoPath,
                    $"{commitString}\n\n{syncTag}\nAuthor: {member.Username}",
                    member.Username, authorEmail, committerName, committerEmail, ct);
            }

            // No changes to fold into (degenerate batch): stamp the sync id in its own commit.
            if (changes.Count == 0)
            {
                project.Metadata.SyncId    = newSyncId;
                project.Metadata.UpdatedAt = DateTime.UtcNow;
                await ProjectFileService.SaveMetadataOnlyAsync(project, repoPath);
                await _Git.StageAllAsync(repoPath, ct);
                await _Git.CommitAsync(repoPath, $"Bump sync id\n\n{syncTag}",
                    member.Username, authorEmail, committerName, committerEmail, ct);
            }

            // A plain push refuses to merge: if the remote moved while we worked, it is rejected.
            GitResult push = await _Git.PushAsync(repoPath, config.Branch, ct);
            if (!push.Success)
            {
                _Logger.LogWarning("Push rejected for '{Slug}' (remote moved). Discarding batch. {Err}",
                    config.Slug, push.StdErr);
                await RollbackAsync(repoPath, baseSha, ct);
                return ServiceResult<PushResponse>.Ok(new PushResponse
                {
                    Status  = PushStatus.Failed,
                    Message = "The repository changed during processing. Please sync and try again.",
                });
            }

            return ServiceResult<PushResponse>.Ok(new PushResponse
            {
                Status    = PushStatus.Success,
                NewSyncId = newSyncId,
            });
        }
        catch (Exception ex)
        {
            _Logger.LogError(ex, "Push failed for '{Slug}', rolling back to {BaseSha}", config.Slug, baseSha);
            await RollbackAsync(repoPath, baseSha, ct);
            throw;
        }
    }

    private async Task RollbackAsync(string repoPath, string baseSha, CancellationToken ct)
    {
        await _Git.ResetHardAsync(repoPath, baseSha, ct);
        await _Git.CleanAsync(repoPath, ct);
    }
}
