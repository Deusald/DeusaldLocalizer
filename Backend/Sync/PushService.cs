using DeusaldLocalizerCommon;

namespace DeusaldLocalizerBackend;

/// <summary>
/// Implements the "push" half: apply the client's pending changes as one commit each, then push.
/// Fetches the latest remote first; validates each change against the freshly-pulled state
/// (defense-in-depth); and if the remote moves during processing, discards everything.
/// </summary>
public sealed class PushService(
    ProjectRegistry registry,
    ProjectSerializer serializer,
    RepoPreparer preparer,
    GitService git,
    AuthService auth,
    ILogger<PushService> logger)
{
    public async Task<ServiceResult<PushResponse>> PushAsync(Guid projectId, Guid userId, string token, IReadOnlyList<LocEntryChange> changes, CancellationToken ct)
    {
        ProjectConfig? config = registry.Find(projectId);
        if (config == null) return ServiceResult<PushResponse>.NotFound();

        using IDisposable _ = await serializer.AcquireAsync(projectId, ct);

        string     repoPath = await preparer.ToLatestAsync(config, ct);
        LocProject project  = await preparer.LoadAsync(repoPath);

        LocProjectMember? member = auth.Authenticate(project, userId, token);
        if (member == null) return ServiceResult<PushResponse>.Unauthorized();

        // Authorization: the client hides actions the member's role forbids, but a crafted request
        // could push them anyway — re-check every change server-side before mutating anything.
        List<EntryChangePermissionError> denied = EntryChangePermissionService.Validate(project, member, changes);
        if (denied.Count > 0)
        {
            logger.LogWarning("Push rejected for '{Slug}': member '{User}' lacks permission for {Count} change(s): {Types}",
                config.Slug, member.Username, denied.Count, string.Join(", ", denied.Select(d => d.Type)));
            return ServiceResult<PushResponse>.Ok(new PushResponse
            {
                Status  = PushStatus.Forbidden,
                Message = denied[0].Message,
            });
        }

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
        string baseSha   = await git.RevParseAsync(repoPath, "HEAD", ct);

        string authorEmail    = $"{member.UserId}@localizer";
        string committerName  = registry.Options.CommitterName;
        string committerEmail = registry.Options.CommitterEmail;

        try
        {
            for (int x = 0; x < changes.Count; ++x)
            {
                LocEntryChange change = changes[x];

                // Server owns identity/tallies: strip any forged author or votes before applying, so a
                // member can only author their own suggestion and cast their own single vote.
                EntryChangeAuthorityService.Normalize(project, member, change);

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

                await git.StageAllAsync(repoPath, ct);
                await git.CommitAsync(repoPath,
                    $"{commitString}\n\n{syncTag}\nAuthor: {member.Username}",
                    member.Username, authorEmail, committerName, committerEmail, ct);
            }

            // No changes to fold into (degenerate batch): stamp the sync id in its own commit.
            if (changes.Count == 0)
            {
                project.Metadata.SyncId    = newSyncId;
                project.Metadata.UpdatedAt = DateTime.UtcNow;
                await ProjectFileService.SaveMetadataOnlyAsync(project, repoPath);
                await git.StageAllAsync(repoPath, ct);
                await git.CommitAsync(repoPath, $"Bump sync id\n\n{syncTag}",
                    member.Username, authorEmail, committerName, committerEmail, ct);
            }

            // A plain push refuses to merge: if the remote moved while we worked, it is rejected.
            GitResult push = await git.PushAsync(repoPath, config.Branch, ct);
            if (!push.Success)
            {
                logger.LogWarning("Push rejected for '{Slug}' (remote moved). Discarding batch. {Err}",
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
            logger.LogError(ex, "Push failed for '{Slug}', rolling back to {BaseSha}", config.Slug, baseSha);
            await RollbackAsync(repoPath, baseSha, ct);
            throw;
        }
    }

    private async Task RollbackAsync(string repoPath, string baseSha, CancellationToken ct)
    {
        await git.ResetHardAsync(repoPath, baseSha, ct);
        await git.CleanAsync(repoPath, ct);
    }
}
