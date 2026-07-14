using System.Net;
using DeusaldLocalizerCommon;
using JetBrains.Annotations;

namespace DeusaldLocalizerWeb;

/// <summary>
/// Holds the currently open project and active user for the lifetime of the app session.
/// Inject as a singleton so all pages share the same state.
/// </summary>
[PublicAPI]
public partial class ProjectStateService(
    LocalizerApiClient api,
    IAuthTokenStore authTokens,
    RecentProjectsStore recents,
    IProjectStoreFactory storeFactory,
    IProjectLocationService location)
{
    // ── State ────────────────────────────────────────────────────────────────

    /// <summary>The currently loaded project. Null when no project is open.</summary>
    public LocProject? CurrentProject { get; private set; }

    /// <summary>Path on disk where the current project was loaded from / last saved to.</summary>
    public string? CurrentProjectPath { get; private set; }

    /// <summary>The currently authenticated user. Always set — offline user when no real login.</summary>
    public LocProjectMember CurrentUser { get; private set; } = LocProjectMember.OfflineMember;

    public Guid AccessToken { get; set; } = Guid.Empty;

    public HashSet<Guid> ChangedLocKeys { get; } = new();

    /// <summary>
    /// Conflicts detected the last time uncommitted changes were validated against the server
    /// (after a sync). While this is non-empty, pushing is blocked until the user resolves them.
    /// </summary>
    public IReadOnlyList<EntryChangeConflict> SyncConflicts => _SyncConflicts;

    private List<EntryChangeConflict> _SyncConflicts = new();

    /// <summary>
    /// A clean copy of the last-synced server state (no local edits replayed onto it), used purely as the
    /// baseline for conflict validation. The working <see cref="CurrentProject"/> carries the user's edits
    /// on top of the same server state, so it cannot be validated against itself.
    /// </summary>
    private LocProject? _SyncBaseline;

    public bool HasSyncConflicts => _SyncConflicts.Count > 0;

    /// <summary>True when a project is open and ready to use.</summary>
    public bool HasProject => CurrentProject is not null;

    /// <summary>True when there are unsaved changes.</summary>
    public bool IsDirty { get; private set; }

    // ── Events ───────────────────────────────────────────────────────────────

    /// <summary>Fires whenever the open project changes (load, close, new).</summary>
    public event Action? ProjectChanged;

    /// <summary>Fires whenever IsDirty changes.</summary>
    public event Action? DirtyStateChanged;

    /// <summary>
    /// Fires every time the project's data is mutated via MarkDirty(), even if
    /// IsDirty was already true. Use this (instead of DirtyStateChanged) when a
    /// component needs to refresh derived data — like translation progress —
    /// after every edit, not just the first one after a save.
    /// </summary>
    public event Action? ProjectDataChanged;

    // ── Construction ───────────────────────────────────────────────────────────

    /// <summary>The file store for the currently open project's location handle.</summary>
    private IProjectFileStore _CurrentStore => storeFactory.Create(CurrentProjectPath!);

    // ── Actions ──────────────────────────────────────────────────────────────

    public void CreateNewProject(string name, string slug, string description, string mainLangCode)
    {
        LocProject newProject = new()
        {
            Metadata = new LocProjectMetadata
            {
                Name           = name,
                Slug           = slug,
                Description    = description,
                MainLanguageId = mainLangCode,
                UpdatedAt      = DateTime.UtcNow
            }
        };

        newProject.Metadata.Languages.Add(mainLangCode);

        CurrentProject     = newProject;
        CurrentProjectPath = null;
        IsDirty            = true;
        ChangedLocKeys.Clear();
        _SyncConflicts.Clear();
        _SyncBaseline = null;
        CurrentUser   = LocProjectMember.OfflineMember;
        ResetUndoHistory();
        ProjectChanged?.Invoke();
        DirtyStateChanged?.Invoke();
    }

    public void LoadProject(LocProject project, string folderPath, Guid userId, Guid accessToken)
    {
        CurrentProject     = project;
        CurrentProjectPath = folderPath;
        IsDirty            = false;
        ChangedLocKeys.Clear();
        _SyncConflicts.Clear();
        _SyncBaseline = null;
        CurrentUser   = userId == LocProjectMember.OfflineMember.UserId ? LocProjectMember.OfflineMember : project.ProjectMembers.Find(m => m.UserId == userId)!;
        AccessToken   = accessToken;

        // When changes are staged (online, or offline uncommitted mode) the key files hold the last-committed
        // state, so any unapplied edits live only in the pending queue. Replay them onto the freshly-loaded
        // project so reopening shows the user's in-flight work.
        if (project.Metadata.UsesUncommittedChanges && project.UncommitedChanges.Count > 0)
            ReapplyPendingChanges();

        ResetUndoHistory();
        ProjectChanged?.Invoke();
        DirtyStateChanged?.Invoke();
    }

    public void CloseProject()
    {
        CurrentProject     = null;
        CurrentProjectPath = null;
        IsDirty            = false;
        ChangedLocKeys.Clear();
        _SyncConflicts.Clear();
        _SyncBaseline = null;
        CurrentUser   = LocProjectMember.OfflineMember;
        ResetUndoHistory();
        ProjectChanged?.Invoke();
        DirtyStateChanged?.Invoke();
    }

    public async Task SaveAsync()
    {
        // Online, or offline uncommitted mode with a folder already on disk: only persist the pending
        // queue locally; the key files stay at their last-committed state until the changes are applied
        // (pushed to the bot online, applied locally offline). The work is now on disk, so it counts as saved.
        // A brand-new offline project without a path still falls through to the folder-pick full save below.
        if (!string.IsNullOrEmpty(CurrentProjectPath) && CurrentProject!.Metadata.UsesUncommittedChanges)
        {
            await ProjectFileService.SaveUncommittedOnlyAsync(CurrentProject, _CurrentStore);
            MarkClean();
            return;
        }

        if (string.IsNullOrEmpty(CurrentProjectPath))
        {
            string? saveLocation = await location.PickSaveLocationAsync(CurrentProject!.Metadata.Slug);

            if (!string.IsNullOrEmpty(saveLocation))
            {
                CurrentProjectPath = saveLocation;
                await ProjectFileService.SaveAsync(CurrentProject!, _CurrentStore);
                MarkClean();
            }
        }
        else
        {
            await ProjectFileService.SaveIncrementalAsync(CurrentProject!, _CurrentStore, ChangedLocKeys);
            MarkClean();
        }

        if (!string.IsNullOrEmpty(CurrentProjectPath)) recents.UpdateRecentProjects(CurrentProject!, CurrentProjectPath!, CurrentProject!.Metadata.IsOnline);
    }

    // ── Online sync / push ─────────────────────────────────────────────────────

    /// <summary>
    /// Pulls the latest repo state from the bot: applies any changed/deleted files locally,
    /// reloads the project, then re-validates the pending uncommitted changes against the new
    /// state. Detected conflicts are stored in <see cref="SyncConflicts"/> and block pushing.
    /// </summary>
    public async Task<SyncOperationResult> SyncAsync()
    {
        if (CurrentProject is null || string.IsNullOrEmpty(CurrentProjectPath))
            return new SyncOperationResult { Outcome = SyncOutcome.Failed, Error = "No project is open." };
        if (!CurrentProject.Metadata.IsOnline)
            return new SyncOperationResult { Outcome = SyncOutcome.NotOnline };

        (Guid UserId, string Token)? creds = await authTokens.GetAsync(CurrentProject.Metadata.Id, CurrentProjectPath!);
        if (creds is null)
            return new SyncOperationResult { Outcome = SyncOutcome.NoCredentials };

        SyncResponse? response;
        try
        {
            response = await api.SyncAsync(
                           CurrentProject.Metadata.ApiUrl, CurrentProject.Metadata.Id,
                           creds.Value.UserId, creds.Value.Token, CurrentProject.Metadata.SyncId);
        }
        catch (Exception ex)
        {
            return new SyncOperationResult { Outcome = SyncOutcome.Failed, Error = ex.Message };
        }

        if (response is null)
            return new SyncOperationResult { Outcome = SyncOutcome.Failed, Error = "Empty response from server." };

        if (response.Status == SyncStatus.UpToDate)
        {
            // The server is unchanged, but the editor mutated the in-memory project in place during the
            // session (online key files are never rewritten locally). That makes the in-memory state a
            // dirty working copy, not a clean server baseline — validating pending changes against it
            // compares an edit to itself and reports false conflicts, and it also hides the fact that the
            // key files still hold the server value. Rebuild from the on-disk state whenever anything is
            // pending so the baseline is clean and the user's edits are replayed back on top.
            if (CurrentProject.UncommitedChanges.Count > 0)
            {
                await RebuildWorkingCopyAsync(CurrentProject.UncommitedChanges);
                ProjectChanged?.Invoke();
                ProjectDataChanged?.Invoke();
            }
            else
            {
                RevalidateConflicts();
            }

            return new SyncOperationResult { Outcome = SyncOutcome.UpToDate, Conflicts = _SyncConflicts.Count };
        }

        // Preserve the pending queue across the reload (server files never touch UncommittedChanges/).
        List<LocEntryChange> pending = CurrentProject.UncommitedChanges;

        await ApplyServerFilesAsync(_CurrentStore, response);

        await RebuildWorkingCopyAsync(pending);

        ProjectChanged?.Invoke();
        ProjectDataChanged?.Invoke();

        return new SyncOperationResult
        {
            Outcome      = response.Status == SyncStatus.FullResync ? SyncOutcome.FullResync : SyncOutcome.Updated,
            ChangedFiles = response.ChangedFiles.Count + response.DeletedFiles.Count,
            Conflicts    = _SyncConflicts.Count,
        };
    }

    /// <summary>
    /// Sends all pending uncommitted changes to the bot. Blocked while <see cref="HasSyncConflicts"/>.
    /// On success, clears the local queue and refreshes local files by syncing to the new version.
    /// </summary>
    public async Task<PushOperationResult> PushAsync()
    {
        if (CurrentProject is null || string.IsNullOrEmpty(CurrentProjectPath))
            return new PushOperationResult { Outcome = PushOutcome.Failed, Message = "No project is open." };
        if (!CurrentProject.Metadata.IsOnline)
            return new PushOperationResult { Outcome = PushOutcome.NotOnline };
        if (HasSyncConflicts)
            return new PushOperationResult { Outcome = PushOutcome.BlockedByConflicts, Conflicts = _SyncConflicts.Count };
        if (CurrentProject.UncommitedChanges.Count == 0)
            return new PushOperationResult { Outcome = PushOutcome.Success };

        (Guid UserId, string Token)? creds = await authTokens.GetAsync(CurrentProject.Metadata.Id, CurrentProjectPath!);
        if (creds is null)
            return new PushOperationResult { Outcome = PushOutcome.NoCredentials };

        PushResponse? response;
        try
        {
            response = await api.PushAsync(
                           CurrentProject.Metadata.ApiUrl, CurrentProject.Metadata.Id,
                           creds.Value.UserId, creds.Value.Token,
                           CurrentProject.Metadata.SyncId, CurrentProject.UncommitedChanges);
        }
        catch (Exception ex)
        {
            return new PushOperationResult { Outcome = PushOutcome.Failed, Message = ex.Message };
        }

        if (response is null)
            return new PushOperationResult { Outcome = PushOutcome.Failed, Message = "Empty response from server." };

        switch (response.Status)
        {
            case PushStatus.Conflict:
                // The server rejected the push because our changes clash with newer server edits, but we
                // haven't pulled that newer state yet — so we can't show the server version or validate
                // against it. Sync now: it pulls the server state, discards any of our edits that turned
                // out identical to the server's, and flags the genuine conflicts against a fresh baseline.
                await SyncAsync();

                if (_SyncConflicts.Count == 0)
                {
                    // Every rejected change was redundant (already on the server) and got pruned. If real
                    // work is still pending, retry the now-clean push; otherwise there is nothing left.
                    if ((CurrentProject?.UncommitedChanges.Count ?? 0) > 0)
                        return await PushAsync();
                    return new PushOperationResult { Outcome = PushOutcome.Success };
                }

                return new PushOperationResult { Outcome = PushOutcome.Conflict, Conflicts = _SyncConflicts.Count, Message = response.Message };

            case PushStatus.Failed:
                return new PushOperationResult { Outcome = PushOutcome.Failed, Message = response.Message };

            case PushStatus.Forbidden:
                // The server rejected the batch because it exceeded our role. The UI normally prevents
                // this, so it means the local state drifted from the server's view of our permissions —
                // surface the server's explanation rather than a generic failure.
                return new PushOperationResult { Outcome = PushOutcome.Failed, Message = response.Message };

            case PushStatus.Success:
                // Clear the queue first so the follow-up sync does not see our own changes as conflicts,
                // then sync (still holding the old SyncId) to pull the just-pushed state into local files.
                CurrentProject.UncommitedChanges.Clear();
                await ProjectFileService.ClearUncommittedChangesAsync(_CurrentStore);
                MarkClean();

                await SyncAsync();

                return new PushOperationResult { Outcome = PushOutcome.Success };

            default:
                return new PushOperationResult { Outcome = PushOutcome.Failed, Message = "Unexpected server status." };
        }
    }

    // ── First sign-in token rotation ───────────────────────────────────────────

    /// <summary>
    /// Completes a member's first sign-in by rotating their initial (username-based) token to a
    /// freshly generated one and installing it on the server. The rotation is pushed straight away
    /// as a single <see cref="EntryChangeType.MemberUpdated"/> change, authenticated with the
    /// member's PREVIOUS token (<paramref name="previousToken"/>) — the only credential the server
    /// still recognizes, since it does not yet know the new token, so the new token cannot push
    /// itself.
    ///
    /// Nothing local is mutated until the server accepts the push, so a rejected push leaves the
    /// member on their initial token and the caller can simply ask them to retry. On success the new
    /// hash is written to the local files right away (so the member can never be locked out even if
    /// the follow-up pull fails), the new token is cached, the just-pushed state is pulled down, and
    /// the result carries the raw token to show once plus the reloaded project.
    /// </summary>
    public async Task<InitialTokenResult> RotateInitialTokenAsync(
        LocProject project, string path, LocProjectMember member, string previousToken)
    {
        string         newToken    = AccessTokenService.GenerateToken();
        LocEntryChange tokenChange = HashRotationChange(member.UserId, newToken);
        LocEntryChange flagChange  = MustResetChange(member.UserId, false);

        PushResponse? push;
        try
        {
            push = await PushBatchAsync(project, member.UserId, previousToken, [tokenChange, flagChange]);
        }
        catch (Exception ex)
        {
            return new InitialTokenResult { Error = FriendlyError(ex) };
        }

        if (push is null || push.Status != PushStatus.Success)
            return new InitialTokenResult { Error = push?.Message ?? "The server rejected the sign-in. Please try again." };

        // The server now knows the new token. Reflect the new hash + cleared reset flag in the local
        // files immediately so a failed follow-up pull can never lock the member out, then cache the token.
        member.HashedAccessToken    = tokenChange.ChangeData;
        member.MustResetAccessToken = false;
        await ProjectFileService.WriteEntityForChangeAsync(project, storeFactory.Create(path), tokenChange);
        await authTokens.SaveAsync(project.Metadata.Id, path, member.UserId, newToken);

        LocProject reloaded = await PullLatestAsync(project, path, member.UserId, newToken);

        return new InitialTokenResult { RawToken = newToken, Project = reloaded };
    }

    /// <summary>
    /// Regenerates the current user's own access token on a live online project. Exactly like the
    /// first sign-in rotation, the change must be pushed straight away and authenticated with the
    /// user's PREVIOUS token — the one this device is currently signed in with — because the server
    /// does not know the new token yet, so it cannot push itself. Any other pending changes are left
    /// untouched in the queue.
    ///
    /// Nothing is adopted until the server accepts the push, so a rejected push leaves the user on
    /// their existing token to retry. On success the new hash is written locally (guarding against a
    /// failed follow-up sync locking the user out), the new token is cached, and the just-pushed
    /// state is pulled down via <see cref="SyncAsync"/> (which preserves the pending queue). The raw
    /// token to show once is returned; a null <see cref="InitialTokenResult.RawToken"/> means failure.
    /// </summary>
    public async Task<InitialTokenResult> RegenerateOwnTokenAsync()
    {
        if (CurrentProject is null || string.IsNullOrEmpty(CurrentProjectPath) || !CurrentProject.Metadata.IsOnline)
            return new InitialTokenResult { Error = "No online project is open." };

        (Guid UserId, string Token)? creds = await authTokens.GetAsync(CurrentProject.Metadata.Id, CurrentProjectPath!);
        if (creds is null)
            return new InitialTokenResult { Error = "You are not signed in on this device." };

        string         newToken = AccessTokenService.GenerateToken();
        LocEntryChange change   = HashRotationChange(CurrentUser.UserId, newToken);

        PushResponse? push;
        try
        {
            push = await PushSingleAsync(CurrentProject, CurrentUser.UserId, creds.Value.Token, change);
        }
        catch (Exception ex)
        {
            return new InitialTokenResult { Error = FriendlyError(ex) };
        }

        if (push is null || push.Status != PushStatus.Success)
            return new InitialTokenResult { Error = push?.Message ?? "The server rejected the change. Please sync and try again." };

        // The server now knows the new token. Adopt it locally (writing the member file first so a
        // failed follow-up sync can't lock the user out), then pull the new state down — SyncAsync
        // preserves the pending queue and re-validates it against the freshly pulled project.
        CurrentUser.HashedAccessToken = change.ChangeData;
        await ProjectFileService.WriteEntityForChangeAsync(CurrentProject, _CurrentStore, change);
        await authTokens.SaveAsync(CurrentProject.Metadata.Id, CurrentProjectPath!, CurrentUser.UserId, newToken);

        await SyncAsync();

        return new InitialTokenResult { RawToken = newToken };
    }

    // ── Connect to server (first-time full download) ──────────────────────────

    /// <summary>
    /// Downloads a whole online project from the bot into a brand-new location and prepares it for
    /// sign-in, without touching the current session. Authenticates the initial full download by
    /// <paramref name="username"/> + <paramref name="token"/> (a fresh member does not yet know their
    /// <c>UserId</c>), writes every file to the store produced by <paramref name="resolveLocation"/>
    /// (given the downloaded metadata, so the caller can name the folder after the slug), stamps the
    /// local <see cref="LocProjectMetadata.ApiUrl"/> so the project reads as online, and returns the
    /// hydrated project + matched member. The caller then drives the usual first-sign-in token
    /// rotation and <c>FinalizeLoad</c>, exactly like the local open flow.
    /// </summary>
    public async Task<ConnectResult> ConnectToServerAsync(
        string apiUrl, Guid projectId, string username, string token,
        Func<LocProjectMetadata, string> resolveLocation)
    {
        string normalizedApiUrl = apiUrl.Trim().TrimEnd('/');

        SyncResponse? resp;
        try
        {
            resp = await api.BootstrapAsync(normalizedApiUrl, projectId, username, token);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new ConnectResult { Error = "Username or access token is incorrect." };
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new ConnectResult { Error = "No project with that ID exists on this server." };
        }
        catch (Exception ex)
        {
            return new ConnectResult { Error = FriendlyError(ex) };
        }

        if (resp is null || resp.ChangedFiles.Count == 0)
            return new ConnectResult { Error = "The server did not return any project files." };

        // Read the project slug from the downloaded metadata so the caller can name the folder after it.
        SyncFile? metaFile = resp.ChangedFiles.Find(f => f.Path == ProjectFileService.METADATA_FILE_NAME);
        LocProjectMetadata? metadata = metaFile is null
                                           ? null
                                           : Newtonsoft.Json.JsonConvert.DeserializeObject<LocProjectMetadata>(metaFile.Content);
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.Slug))
            return new ConnectResult { Error = "The server returned an invalid project." };

        string            saveLocation = resolveLocation(metadata);
        IProjectFileStore store        = storeFactory.Create(saveLocation);

        if (await store.FileExistsAsync(ProjectFileService.METADATA_FILE_NAME))
            return new ConnectResult { Error = "A project already exists at that location." };

        LocProject project;
        try
        {
            await ApplyServerFilesAsync(store, resp);
            project = await ProjectFileService.OpenAsync(store);
        }
        catch (ProjectFolderException)
        {
            return new ConnectResult { Error = "The server returned an invalid project." };
        }
        catch (Exception ex)
        {
            return new ConnectResult { Error = FriendlyError(ex) };
        }

        // A project is "online" purely by having an ApiUrl. The server repo may carry a different (or
        // empty) one, so stamp the URL the user connected with and persist it without minting a new SyncId.
        project.Metadata.ApiUrl = normalizedApiUrl;
        await ProjectFileService.SaveMetadataOnlyAsync(project, store);

        LocProjectMember? member = project.ProjectMembers.Find(m =>
            string.Equals(m.Username, username, StringComparison.OrdinalIgnoreCase));

        return new ConnectResult { Project = project, Location = saveLocation, Member = member };
    }

    private static LocEntryChange HashRotationChange(Guid userId, string rawToken) => new()
    {
        Type       = EntryChangeType.MemberUpdated,
        EntryId    = userId,
        EntrySubId = nameof(LocProjectMember.HashedAccessToken),
        ChangeData = AccessTokenService.HashToken(rawToken),
    };

    private static LocEntryChange MustResetChange(Guid userId, bool value) => new()
    {
        Type       = EntryChangeType.MemberUpdated,
        EntryId    = userId,
        EntrySubId = nameof(LocProjectMember.MustResetAccessToken),
        ChangeData = value.ToString(),
    };

    /// <summary>Maps a transport-layer failure to a short, user-facing reason; other errors pass through.</summary>
    private static string FriendlyError(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException
            ? "Could not reach the server. Check your connection and try again."
            : ex.Message;

    /// <summary>Pushes a single change to the bot, authenticated with an explicit token.</summary>
    private Task<PushResponse?> PushSingleAsync(LocProject project, Guid userId, string authToken, LocEntryChange change) =>
        PushBatchAsync(project, userId, authToken, [change]);

    /// <summary>Pushes a batch of changes to the bot, authenticated with an explicit token.</summary>
    private Task<PushResponse?> PushBatchAsync(LocProject project, Guid userId, string authToken, List<LocEntryChange> changes) =>
        api.PushAsync(project.Metadata.ApiUrl, project.Metadata.Id, userId, authToken,
            project.Metadata.SyncId, changes);

    /// <summary>
    /// Best-effort pull of the latest repo state into <paramref name="path"/> without touching the
    /// session state. Applies the server delta and returns a freshly reloaded project; on any failure
    /// (or when already up to date) returns <paramref name="project"/> unchanged.
    /// </summary>
    private async Task<LocProject> PullLatestAsync(LocProject project, string path, Guid userId, string token)
    {
        try
        {
            SyncResponse? response = await api.SyncAsync(
                                         project.Metadata.ApiUrl, project.Metadata.Id, userId, token, project.Metadata.SyncId);

            if (response is null || response.Status == SyncStatus.UpToDate)
                return project;

            IProjectFileStore pullStore = storeFactory.Create(path);
            await ApplyServerFilesAsync(pullStore, response);
            return await ProjectFileService.OpenAsync(pullStore);
        }
        catch
        {
            // Best-effort pull: the token rotation is already committed and cached, so any transport
            // OR disk failure here (dropped connection, half-applied delta) must not crash sign-in.
            // Fall back to the in-memory project and let the next manual sync pick up the server delta.
            return project;
        }
    }

    private void RevalidateConflicts()
    {
        // Validate against the clean server baseline, never against the working copy (which already carries
        // the very edits we're validating). Falls back to CurrentProject only when nothing is pending, where
        // the result is empty regardless.
        LocProject baseline = _SyncBaseline ?? CurrentProject!;
        _SyncConflicts = CurrentProject!.Metadata.IsOnline
                             ? EntryChangeConflictService.Validate(baseline, CurrentProject.UncommitedChanges)
                             : new List<EntryChangeConflict>();
    }

    /// <summary>
    /// Rebuilds the in-memory project from the on-disk (server-state) files after a sync: loads a clean
    /// baseline for conflict validation, then a separate working copy onto which the pending changes are
    /// replayed so the editor shows the user's unpushed edits. Conflicting translations are left at the
    /// server value (see <see cref="ReapplyPendingChanges"/>).
    /// </summary>
    private async Task RebuildWorkingCopyAsync(List<LocEntryChange> pending)
    {
        _SyncBaseline = await ProjectFileService.OpenAsync(_CurrentStore);

        LocProject working = await ProjectFileService.OpenAsync(_CurrentStore);
        working.UncommitedChanges = pending;
        CurrentProject            = working;

        // Discard pending translation edits that already match the server exactly (someone else made the
        // same edit): they are redundant no-ops, not conflicts, so they must never be flagged or pushed.
        PruneRedundantTranslationChanges(_SyncBaseline);

        RevalidateConflicts();
        ReapplyPendingChanges();

        // The working copy was replaced; re-anchor the undo baseline to it. The undo/redo stacks hold absolute
        // snapshots and remain valid, so undo keeps working after a sync/push.
        ReanchorUndoBaseline();

        await ProjectFileService.SaveUncommittedOnlyAsync(CurrentProject, _CurrentStore);
    }

    /// <summary>
    /// Drops pending translation edits whose net result is identical to the server's current translation
    /// (same text, status, source-changed flag and base hash). This is the "someone else already made the
    /// same change" case: there is nothing left to push, so the edit is silently discarded rather than
    /// surfaced as a conflict. Edits that differ in any field (e.g. a source-matched acknowledgement) stay.
    /// </summary>
    private void PruneRedundantTranslationChanges(LocProject baseline)
    {
        if (CurrentProject is null) return;

        List<(Guid KeyId, string Lang)> pairs = CurrentProject.UncommitedChanges
                                                              .Where(c => c.Type == EntryChangeType.TranslationUpdated)
                                                              .Select(c => (c.EntryId, c.EntrySubId))
                                                              .Distinct()
                                                              .ToList();

        foreach ((Guid keyId, string lang) in pairs)
        {
            LocKeyTranslation? mine = PendingTranslation(keyId, lang);
            if (mine is null) continue;

            LocLocalizationKey? baseKey  = baseline.Keys.Find(k => k.Id == keyId);
            LocKeyTranslation?  baseDest = baseKey?.Translations.Find(t => t.LanguageId == lang);
            if (baseDest is null) continue;

            bool redundant = mine.Text == baseDest.Text
                          && mine.Status == baseDest.Status
                          && mine.SourceChanged == baseDest.SourceChanged
                          && mine.BaseTextHash == baseDest.BaseTextHash;

            if (redundant)
                CurrentProject.UncommitedChanges.RemoveAll(c =>
                    c.Type == EntryChangeType.TranslationUpdated && c.EntryId == keyId && c.EntrySubId == lang);
        }
    }

    /// <summary>
    /// Replays the pending uncommitted changes onto <see cref="CurrentProject"/> so the editor reflects the
    /// user's unpushed edits (online key files always mirror the server, never the local edits). Translation
    /// edits that currently conflict are skipped, leaving the server value visible so the conflict box can
    /// compare server-vs-yours and the user can resolve them explicitly.
    /// </summary>
    private void ReapplyPendingChanges()
    {
        if (CurrentProject is null) return;

        foreach (LocEntryChange change in CurrentProject.UncommitedChanges)
        {
            if (change.Type == EntryChangeType.TranslationUpdated
             && _SyncConflicts.Any(c => c.KeyId == change.EntryId && c.LanguageId == change.EntrySubId))
                continue;

            EntryChangeExeService.ExecuteChange(CurrentProject, change, out _);
        }
    }

    // ── Uncommitted change management ────────────────────────────────────────

    /// <summary>
    /// Removes a single pending uncommitted change by its position in the queue, then rebuilds the working
    /// copy from disk and replays the remaining changes so the editor reflects the reduced queue. Online
    /// projects only — the key files on disk mirror the server, so dropping a change means re-deriving the
    /// working copy from the clean server state rather than trying to "undo" the change in place.
    /// </summary>
    public async Task RemoveUncommittedChangeAsync(int index)
    {
        if (CurrentProject is null || string.IsNullOrEmpty(CurrentProjectPath)) return;
        if (!CurrentProject.Metadata.UsesUncommittedChanges) return;
        if (index < 0 || index >= CurrentProject.UncommitedChanges.Count) return;

        List<LocEntryChange> pending = new(CurrentProject.UncommitedChanges);
        pending.RemoveAt(index);

        await RebuildWorkingCopyAsync(pending);

        // Manually dropping a queued change out of order desyncs it from the undo history, so start fresh.
        ResetUndoHistory();

        MarkClean();
        ProjectChanged?.Invoke();
        ProjectDataChanged?.Invoke();
    }

    /// <summary>
    /// Discards every pending uncommitted change, then rebuilds the working copy from disk so the editor
    /// returns to the clean server state. Online projects only (see <see cref="RemoveUncommittedChangeAsync"/>).
    /// </summary>
    public async Task ClearUncommittedChangesAsync()
    {
        if (CurrentProject is null || string.IsNullOrEmpty(CurrentProjectPath)) return;
        if (!CurrentProject.Metadata.UsesUncommittedChanges) return;
        if (CurrentProject.UncommitedChanges.Count == 0) return;

        await RebuildWorkingCopyAsync(new List<LocEntryChange>());

        ResetUndoHistory();

        MarkClean();
        ProjectChanged?.Invoke();
        ProjectDataChanged?.Invoke();
    }

    /// <summary>
    /// Offline "commit": the working copy already reflects every staged edit, so this writes the whole
    /// project straight to the key files and clears the uncommitted queue (both in memory and on disk).
    /// Offline uncommitted mode only — online projects apply their queue by pushing to the bot.
    /// </summary>
    public async Task ApplyUncommittedChangesAsync()
    {
        if (CurrentProject is null || string.IsNullOrEmpty(CurrentProjectPath)) return;
        if (CurrentProject.Metadata.IsOnline || !CurrentProject.Metadata.UncommittedMode) return;
        if (CurrentProject.UncommitedChanges.Count == 0) return;

        CurrentProject.UncommitedChanges.Clear();
        ChangedLocKeys.Clear();

        // A full save writes every key file from the in-memory state and rewrites the (now empty)
        // UncommittedChanges folder, so nothing pending is left behind on disk.
        await ProjectFileService.SaveAsync(CurrentProject, _CurrentStore);
        MarkClean();

        ProjectChanged?.Invoke();
        ProjectDataChanged?.Invoke();
    }

    /// <summary>
    /// Turns offline uncommitted mode on or off and persists the flag. Enabling first commits any edits
    /// already made this session (a full save) so the key files form a clean baseline for the staged changes
    /// that follow; disabling is only permitted once the queue is empty (edits would otherwise be orphaned).
    /// No-op for online projects — they always stage their changes.
    /// </summary>
    public async Task SetUncommittedModeAsync(bool enabled)
    {
        if (CurrentProject is null || string.IsNullOrEmpty(CurrentProjectPath)) return;
        if (CurrentProject.Metadata.IsOnline) return;
        if (CurrentProject.Metadata.UncommittedMode == enabled) return;
        if (!enabled && CurrentProject.UncommitedChanges.Count > 0) return;

        CurrentProject.Metadata.UncommittedMode = enabled;
        ChangedLocKeys.Clear();

        // A full save persists the flag along with the whole state: on enable it commits any pre-existing
        // session edits as the baseline; on disable the queue is already empty, so it just rewrites cleanly.
        await ProjectFileService.SaveAsync(CurrentProject, _CurrentStore);
        MarkClean();

        ProjectChanged?.Invoke();
        ProjectDataChanged?.Invoke();
    }

    // ── Conflict resolution ──────────────────────────────────────────────────

    /// <summary>
    /// Raised when something (e.g. the conflicts modal) asks the translate editor to select and show a
    /// specific key on a specific language. The Translate page listens and moves its selection there.
    /// </summary>
    public event Action<Guid, string>? OpenKeyRequested;

    /// <summary>Asks the translate editor to open the given key on the given language.</summary>
    public void RequestOpenKey(Guid keyId, string languageId) => OpenKeyRequested?.Invoke(keyId, languageId);

    /// <summary>The sync conflict (if any) currently affecting the given key + language.</summary>
    public EntryChangeConflict? ConflictFor(Guid keyId, string languageId) =>
        _SyncConflicts.Find(c => c.KeyId == keyId && c.LanguageId == languageId);

    /// <summary>
    /// My most recent queued translation edit for a key + language — i.e. what a KeepMine/SuggestMine
    /// resolution would use. Null when there is no pending translation edit for the pair.
    /// </summary>
    public LocKeyTranslation? PendingTranslation(Guid keyId, string languageId)
    {
        if (CurrentProject is null) return null;
        for (int x = CurrentProject.UncommitedChanges.Count - 1; x >= 0; --x)
        {
            LocEntryChange c = CurrentProject.UncommitedChanges[x];
            if (c.Type != EntryChangeType.TranslationUpdated || c.EntryId != keyId || c.EntrySubId != languageId) continue;
            return Newtonsoft.Json.JsonConvert.DeserializeObject<LocKeyTranslation>(c.ChangeData);
        }
        return null;
    }

    /// <summary>The text of <see cref="PendingTranslation"/>, or null when there is no pending edit for the pair.</summary>
    public string? PendingTranslationText(Guid keyId, string languageId) =>
        PendingTranslation(keyId, languageId)?.Text;

    /// <summary>
    /// Resolves a single translation conflict for a key + language. Server-side values are read from the
    /// clean <see cref="_SyncBaseline"/> (so resolving is only meaningful after a sync):
    ///  • <see cref="ConflictResolution.KeepMine"/>    — overwrite the server's translation with my queued edit.
    ///  • <see cref="ConflictResolution.SuggestMine"/> — keep the server's translation, add my edit as a suggestion.
    ///  • <see cref="ConflictResolution.KeepServer"/>  — discard my queued edit for this translation.
    /// Every pending translation edit for the key + language is dropped, then re-recorded (clean, based on the
    /// current server state) as the chosen resolution requires, so the conflict clears and the push can proceed.
    /// </summary>
    public async Task ResolveConflictAsync(Guid keyId, string languageId, ConflictResolution resolution)
    {
        if (CurrentProject is null || string.IsNullOrEmpty(CurrentProjectPath)) return;

        LocLocalizationKey? key = CurrentProject.Keys.Find(k => k.Id == keyId);
        if (key is null) return;

        // Recover the edit I was trying to save from the most recent queued translation edit for this pair.
        LocKeyTranslation? mine   = PendingTranslation(keyId, languageId);
        string             myText = mine?.Text ?? string.Empty;

        // Drop every queued translation edit for this pair — we re-record below as the resolution requires.
        CurrentProject.UncommitedChanges.RemoveAll(c =>
            c.Type == EntryChangeType.TranslationUpdated && c.EntryId == keyId && c.EntrySubId == languageId);

        // Server-side values come from the clean baseline, not the working copy (which carries local edits).
        LocProject          baseline         = _SyncBaseline ?? CurrentProject;
        LocLocalizationKey? baseKey          = baseline.Keys.Find(k => k.Id == keyId);
        LocKeyTranslation?  baseSource       = baseKey?.Translations.Find(t => t.LanguageId == baseline.Metadata.MainLanguageId);
        LocKeyTranslation?  baseDest         = baseKey?.Translations.Find(t => t.LanguageId == languageId);
        string              serverSourceHash = TextHashHelper.Compute(baseSource?.Text ?? string.Empty);
        string              serverDestText   = baseDest?.Text ?? string.Empty;
        string              serverDestHash   = TextHashHelper.Compute(serverDestText);

        LocKeyTranslation? translation = key.Translations.Find(t => t.LanguageId == languageId);
        if (translation is null)
        {
            translation = new LocKeyTranslation { LanguageId = languageId };
            key.Translations.Add(translation);
        }

        switch (resolution)
        {
            case ConflictResolution.KeepMine: // overwrite the server's translation with my edit on the next push
                translation.Text          = myText;
                translation.BaseTextHash  = serverSourceHash;
                translation.SourceChanged = false;
                translation.Status        = mine?.Status ?? TranslationStatus.Suggested; // keep my status, don't escalate
                translation.UpdatedAt     = DateTime.UtcNow;

                RecordTranslationUpdated(keyId, translation, serverDestHash);
                key.UpdatedAt = DateTime.UtcNow;
                break;

            case ConflictResolution.SuggestMine: // keep the server's translation, offer mine up for voting
                LocTranslationSuggestion suggestion = new LocTranslationSuggestion
                {
                    Text       = myText,
                    AuthorId   = CurrentUser.UserId,
                    SourceHash = serverSourceHash,
                };
                translation.Text          = serverDestText; // keep the server's translation as the active text
                translation.BaseTextHash  = serverSourceHash;
                translation.SourceChanged = true;
                translation.Status        = TranslationStatus.Suggested;
                translation.UpdatedAt     = DateTime.UtcNow;
                translation.Suggestions.Add(suggestion);

                RecordTranslationUpdated(keyId, translation, serverDestHash);
                RecordSuggestionAdded(keyId, languageId, suggestion);
                key.UpdatedAt = DateTime.UtcNow;
                break;

            case ConflictResolution.KeepServer: // discard my edit, restore the server's translation
                translation.Text          = serverDestText;
                translation.BaseTextHash  = baseDest?.BaseTextHash ?? serverSourceHash;
                translation.SourceChanged = baseDest?.SourceChanged ?? false;
                translation.Status        = baseDest?.Status ?? TranslationStatus.Untranslated;
                translation.UpdatedAt     = baseDest?.UpdatedAt ?? DateTime.UtcNow;
                break;
        }

        RevalidateConflicts();
        await ProjectFileService.SaveUncommittedOnlyAsync(CurrentProject, _CurrentStore);
        MarkClean();

        ProjectDataChanged?.Invoke();
    }

    private static async Task ApplyServerFilesAsync(IProjectFileStore store, SyncResponse response)
    {
        if (response.Status == SyncStatus.FullResync)
            await WipeEntityFilesAsync(store);

        // Server file paths already use '/' separators, which is exactly what the store expects.
        foreach (SyncFile file in response.ChangedFiles)
            await store.WriteTextAsync(file.Path, file.Content);

        foreach (string deleted in response.DeletedFiles)
            await store.DeleteFileAsync(deleted);
    }

    /// <summary>Removes metadata + every entity file (a full resync then rewrites them), leaving the local queue intact.</summary>
    private static async Task WipeEntityFilesAsync(IProjectFileStore store)
    {
        await store.DeleteFileAsync(ProjectFileService.METADATA_FILE_NAME);

        foreach (string folder in new[]
                 {
                     ProjectFileService.MEMBERS_FOLDER, ProjectFileService.CATEGORIES_FOLDER,
                     ProjectFileService.ENUMS_FOLDER, ProjectFileService.KEYS_FOLDER,
                 })
        {
            foreach (string file in await store.ListJsonFilesAsync(folder))
                await store.DeleteFileAsync($"{folder}/{file}");
        }
    }

    // ── Member change recording ──────────────────────────────────────────────

    /// <summary>Records a brand-new member (whole object).</summary>
    public void RecordMemberAdded(LocProjectMember member)
    {
        LocEntryChange change = new()
        {
            Type       = EntryChangeType.MemberAdded,
            EntryId    = member.UserId,
            ChangeData = Newtonsoft.Json.JsonConvert.SerializeObject(member)
        };
        if (CurrentProject!.Metadata.UsesUncommittedChanges)
            CurrentProject.UncommitedChanges.Add(change);

        CaptureUndo(change);
        MarkDirty();
    }

    public void RecordMemberUsernameChanged(LocProjectMember member) =>
        AddMemberFieldChange(member, nameof(LocProjectMember.Username), member.Username);

    public void RecordMemberReviewPermissionsChanged(LocProjectMember member) =>
        AddMemberFieldChange(member, nameof(LocProjectMember.ReviewLanguagePermissions),
            Newtonsoft.Json.JsonConvert.SerializeObject(member.ReviewLanguagePermissions));

    public void RecordMemberBanStatusChanged(LocProjectMember member) =>
        AddMemberFieldChange(member, nameof(LocProjectMember.IsBanned), member.IsBanned.ToString());

    public void RecordMemberAccessTokenChanged(LocProjectMember member) =>
        AddMemberFieldChange(member, nameof(LocProjectMember.HashedAccessToken), member.HashedAccessToken);

    public void RecordMemberMustResetChanged(LocProjectMember member) =>
        AddMemberFieldChange(member, nameof(LocProjectMember.MustResetAccessToken), member.MustResetAccessToken.ToString());

    private void AddMemberFieldChange(LocProjectMember member, string fieldName, string changeData)
    {
        LocEntryChange change = new()
        {
            Type       = EntryChangeType.MemberUpdated,
            EntryId    = member.UserId,
            EntrySubId = fieldName,
            ChangeData = changeData
        };
        if (CurrentProject!.Metadata.UsesUncommittedChanges)
            CurrentProject.UncommitedChanges.Add(change);

        CaptureUndo(change);
        MarkDirty();
    }

    // ── Language change recording ─────────────────────────────────────────────

    public void RecordLanguageAdded(string code) =>
        AddLanguageChange(EntryChangeType.LanguageAdded, code);

    public void RecordLanguageRemoved(string code) =>
        AddLanguageChange(EntryChangeType.LanguageRemoved, code);

    private void AddLanguageChange(EntryChangeType type, string code)
    {
        LocEntryChange change = new()
        {
            Type       = type,
            EntryId    = CurrentProject!.Metadata.Id,
            ChangeData = code
        };
        if (CurrentProject.Metadata.UsesUncommittedChanges)
            CurrentProject.UncommitedChanges.Add(change);

        CaptureUndo(change);
        MarkDirty();
    }

    // ── Key change recording ──────────────────────────────────────────────────

    /// <summary>Records a brand-new key (whole object).</summary>
    public void RecordKeyAdded(LocLocalizationKey key)
    {
        ChangedLocKeys.Add(key.Id);
        LocEntryChange change = new()
        {
            Type       = EntryChangeType.KeyAdded,
            EntryId    = key.Id,
            ChangeData = Newtonsoft.Json.JsonConvert.SerializeObject(key)
        };
        if (CurrentProject!.Metadata.UsesUncommittedChanges)
            CurrentProject.UncommitedChanges.Add(change);

        CaptureUndo(change);
        MarkDirty();
    }

    /// <summary>
    /// Deletes a key: removes it and journals a KeyRemoved change (queued for push online / offline
    /// uncommitted, dirty-tracked plain offline). Undoable. No-op if the key does not exist.
    /// </summary>
    public void DeleteKey(Guid keyId)
    {
        if (CurrentProject is null || !CurrentUser.IsAdmin) return;   // admin-only; mirrors the server check
        if (CurrentProject.Keys.All(k => k.Id != keyId)) return;

        LocEntryChange change = new() { Type = EntryChangeType.KeyRemoved, EntryId = keyId };
        EntryChangeExeService.ExecuteChange(CurrentProject, change, out _);

        ChangedLocKeys.Add(keyId);
        if (CurrentProject.Metadata.UsesUncommittedChanges)
            CurrentProject.UncommitedChanges.Add(change);

        CaptureUndo(change);
        MarkDirty();
        ProjectChanged?.Invoke();
        ProjectDataChanged?.Invoke();
    }

    /// <summary>
    /// Deletes a member: removes it and reassigns every reference it owned (suggestion authors, votes and flag
    /// creators) to the offline user (see <see cref="EntryChangeExeService"/>), then journals a MemberRemoved
    /// change. Never deletes the offline user. Not undoable — the reassignment is lossy. No-op if the member
    /// does not exist.
    /// </summary>
    public void DeleteMember(Guid memberId)
    {
        if (CurrentProject is null || !CurrentUser.IsAdmin) return;   // admin-only; mirrors the server check
        if (memberId == LocProjectMember.OfflineMember.UserId) return;
        if (CurrentProject.ProjectMembers.All(m => m.UserId != memberId)) return;

        LocEntryChange change = new() { Type = EntryChangeType.MemberRemoved, EntryId = memberId };
        EntryChangeExeService.ExecuteChange(CurrentProject, change, out _);

        // The reassignment can touch any key, so mark them all dirty for the offline incremental save.
        foreach (LocLocalizationKey key in CurrentProject.Keys)
            ChangedLocKeys.Add(key.Id);

        if (CurrentProject.Metadata.UsesUncommittedChanges)
            CurrentProject.UncommitedChanges.Add(change);

        CaptureUndo(change);
        MarkDirty();
        ProjectChanged?.Invoke();
        ProjectDataChanged?.Invoke();
    }

    public void RecordKeyNameChanged(LocLocalizationKey key) =>
        AddKeyFieldChange(key, nameof(LocLocalizationKey.KeyName), key.KeyName);

    public void RecordKeyCategoryChanged(LocLocalizationKey key) =>
        AddKeyFieldChange(key, nameof(LocLocalizationKey.CategoryId), key.CategoryId.ToString());

    public void RecordKeyMaxLengthChanged(LocLocalizationKey key) =>
        AddKeyFieldChange(key, nameof(LocLocalizationKey.MaxLength), key.MaxLength.ToString());

    private void AddKeyFieldChange(LocLocalizationKey key, string fieldName, string changeData)
    {
        ChangedLocKeys.Add(key.Id);
        LocEntryChange change = new()
        {
            Type       = EntryChangeType.KeyUpdated,
            EntryId    = key.Id,
            EntrySubId = fieldName,
            ChangeData = changeData
        };
        if (CurrentProject!.Metadata.UsesUncommittedChanges)
            CurrentProject.UncommitedChanges.Add(change);

        CaptureUndo(change);
        MarkDirty();
    }

    // ── Category change recording ─────────────────────────────────────────────

    /// <summary>Records a brand-new category (whole object).</summary>
    public void RecordCategoryAdded(LocCategory category)
    {
        LocEntryChange change = new()
        {
            Type       = EntryChangeType.CategoryAdded,
            EntryId    = category.Id,
            ChangeData = Newtonsoft.Json.JsonConvert.SerializeObject(category)
        };
        if (CurrentProject!.Metadata.UsesUncommittedChanges)
            CurrentProject.UncommitedChanges.Add(change);

        CaptureUndo(change);
        MarkDirty();
    }

    public void RecordCategoryNameChanged(LocCategory category) =>
        AddCategoryFieldChange(category, nameof(LocCategory.Name), category.Name);

    public void RecordCategoryDescriptionChanged(LocCategory category) =>
        AddCategoryFieldChange(category, nameof(LocCategory.Description), category.Description);

    public void RecordCategoryParentChanged(LocCategory category) =>
        AddCategoryFieldChange(category, nameof(LocCategory.ParentCategoryId), category.ParentCategoryId?.ToString() ?? string.Empty);

    public void RecordCategoryRemoved(LocCategory category)
    {
        LocEntryChange change = new()
        {
            Type    = EntryChangeType.CategoryRemoved,
            EntryId = category.Id
        };
        if (CurrentProject!.Metadata.UsesUncommittedChanges)
            CurrentProject.UncommitedChanges.Add(change);

        CaptureUndo(change);
        MarkDirty();
    }

    private void AddCategoryFieldChange(LocCategory category, string fieldName, string changeData)
    {
        LocEntryChange change = new()
        {
            Type       = EntryChangeType.CategoryUpdated,
            EntryId    = category.Id,
            EntrySubId = fieldName,
            ChangeData = changeData
        };
        if (CurrentProject!.Metadata.UsesUncommittedChanges)
            CurrentProject.UncommitedChanges.Add(change);

        CaptureUndo(change);
        MarkDirty();
    }

    // ── Key description recording ─────────────────────────────────────────────

    public void RecordKeyDescriptionChanged(LocLocalizationKey key) =>
        AddKeyFieldChange(key, nameof(LocLocalizationKey.Description), key.Description);

    // ── Translation change recording ──────────────────────────────────────────

    /// <summary>
    /// Records a translation update. <paramref name="prevDestHash"/> is the hash of the translation's
    /// text <em>before</em> this edit (so a concurrent edit on the server can be detected); when the
    /// caller did not change the text it may be left null and the translation's current text is used.
    /// The previous source hash is the translation's <see cref="LocKeyTranslation.BaseTextHash"/>.
    /// </summary>
    public void RecordTranslationUpdated(Guid keyId, LocKeyTranslation translation, string? prevDestHash = null) =>
        AddKeyChange(keyId,                                           EntryChangeType.TranslationUpdated, translation.LanguageId,
            Newtonsoft.Json.JsonConvert.SerializeObject(translation), translation.BaseTextHash,
            prevDestHash ?? TextHashHelper.Compute(translation.Text));

    // ── Suggestion change recording ───────────────────────────────────────────

    public void RecordSuggestionAdded(Guid keyId, string languageId, LocTranslationSuggestion suggestion) =>
        AddKeyChange(keyId, EntryChangeType.SuggestionAdded, languageId,
            Newtonsoft.Json.JsonConvert.SerializeObject(suggestion));

    public void RecordSuggestionVoted(Guid keyId, string languageId, LocTranslationSuggestion suggestion) =>
        AddKeyChange(keyId, EntryChangeType.SuggestionVoted, languageId,
            Newtonsoft.Json.JsonConvert.SerializeObject(suggestion));

    public void RecordSuggestionRemoved(Guid keyId, string languageId, Guid suggestionId) =>
        AddKeyChange(keyId, EntryChangeType.SuggestionRemoved, languageId, suggestionId.ToString());

    // ── Comment change recording ──────────────────────────────────────────────

    public void RecordKeyCommentAdded(Guid keyId, LocComment comment) =>
        RecordCommentAdded(keyId, new LocCommentRef { Scope = CommentScope.Key, Comment = comment });

    public void RecordTranslationCommentAdded(Guid keyId, string languageId, LocComment comment) =>
        RecordCommentAdded(keyId, new LocCommentRef { Scope = CommentScope.Translation, LanguageId = languageId, Comment = comment });

    public void RecordSuggestionCommentAdded(Guid keyId, string languageId, Guid suggestionId, LocComment comment) =>
        RecordCommentAdded(keyId, new LocCommentRef
        {
            Scope        = CommentScope.Suggestion,
            LanguageId   = languageId,
            SuggestionId = suggestionId,
            Comment      = comment,
        });

    public void RecordKeyCommentRemoved(Guid keyId, Guid commentId) =>
        RecordCommentRemoved(keyId, new LocCommentRef { Scope = CommentScope.Key, CommentId = commentId });

    public void RecordTranslationCommentRemoved(Guid keyId, string languageId, Guid commentId) =>
        RecordCommentRemoved(keyId, new LocCommentRef { Scope = CommentScope.Translation, LanguageId = languageId, CommentId = commentId });

    public void RecordSuggestionCommentRemoved(Guid keyId, string languageId, Guid suggestionId, Guid commentId) =>
        RecordCommentRemoved(keyId, new LocCommentRef
        {
            Scope        = CommentScope.Suggestion,
            LanguageId   = languageId,
            SuggestionId = suggestionId,
            CommentId    = commentId,
        });

    private void RecordCommentAdded(Guid keyId, LocCommentRef commentRef) =>
        AddKeyChange(keyId, EntryChangeType.CommentAdded, string.Empty, Newtonsoft.Json.JsonConvert.SerializeObject(commentRef));

    private void RecordCommentRemoved(Guid keyId, LocCommentRef commentRef) =>
        AddKeyChange(keyId, EntryChangeType.CommentRemoved, string.Empty, Newtonsoft.Json.JsonConvert.SerializeObject(commentRef));

    /// <summary>
    /// Admin maintenance: deletes every comment (across keys, translations and suggestions) created strictly
    /// before <paramref name="cutoffUtc"/>. Mutates the in-memory project and records one CommentRemoved per
    /// comment (so each is pushed/undone the normal way). Returns how many comments were cleared.
    /// </summary>
    public int ClearCommentsOlderThan(DateTime cutoffUtc)
    {
        if (CurrentProject == null) return 0;

        int cleared = 0;
        foreach (LocLocalizationKey key in CurrentProject.Keys)
        {
            foreach (LocComment comment in key.Comments.Where(c => c.CreatedAt < cutoffUtc).ToList())
            {
                key.Comments.Remove(comment);
                RecordKeyCommentRemoved(key.Id, comment.Id);
                ++cleared;
            }
            foreach (LocKeyTranslation translation in key.Translations)
            {
                foreach (LocComment comment in translation.Comments.Where(c => c.CreatedAt < cutoffUtc).ToList())
                {
                    translation.Comments.Remove(comment);
                    RecordTranslationCommentRemoved(key.Id, translation.LanguageId, comment.Id);
                    ++cleared;
                }
                foreach (LocTranslationSuggestion suggestion in translation.Suggestions)
                {
                    foreach (LocComment comment in suggestion.Comments.Where(c => c.CreatedAt < cutoffUtc).ToList())
                    {
                        suggestion.Comments.Remove(comment);
                        RecordSuggestionCommentRemoved(key.Id, translation.LanguageId, suggestion.Id, comment.Id);
                        ++cleared;
                    }
                }
            }
        }
        return cleared;
    }

    // ── Flag change recording ─────────────────────────────────────────────────

    public void RecordFlagAdded(Guid keyId, LocKeyFlag flag) =>
        AddKeyChange(keyId, EntryChangeType.FlagAdded, string.Empty,
            Newtonsoft.Json.JsonConvert.SerializeObject(flag));

    public void RecordFlagRemoved(Guid keyId, Guid flagId) =>
        AddKeyChange(keyId, EntryChangeType.FlagRemoved, string.Empty, flagId.ToString());

    // ── Tag change recording ──────────────────────────────────────────────────

    public void RecordTagAdded(Guid keyId, string tag) =>
        AddKeyChange(keyId, EntryChangeType.TagAdded, string.Empty, tag);

    public void RecordTagRemoved(Guid keyId, string tag) =>
        AddKeyChange(keyId, EntryChangeType.TagRemoved, string.Empty, tag);

    // ── Variable change recording ─────────────────────────────────────────────

    public void RecordVariableAdded(Guid keyId, LocKeyVariable variable) =>
        AddKeyChange(keyId, EntryChangeType.VariableAdded, string.Empty,
            Newtonsoft.Json.JsonConvert.SerializeObject(variable));

    public void RecordVariableUpdated(Guid keyId, LocKeyVariable variable) =>
        AddKeyChange(keyId, EntryChangeType.VariableUpdated, variable.Id.ToString(),
            Newtonsoft.Json.JsonConvert.SerializeObject(variable));

    public void RecordVariableRemoved(Guid keyId, Guid variableId) =>
        AddKeyChange(keyId, EntryChangeType.VariableRemoved, string.Empty, variableId.ToString());

    private void AddKeyChange(Guid keyId, EntryChangeType type, string entrySubId, string changeData,
                              string prevSourceHashData = "", string prevDestHashData = "")
    {
        ChangedLocKeys.Add(keyId);
        LocEntryChange change = new()
        {
            Type               = type,
            EntryId            = keyId,
            EntrySubId         = entrySubId,
            ChangeData         = changeData,
            PrevSourceHashData = prevSourceHashData,
            PrevDestHashData   = prevDestHashData
        };
        if (CurrentProject!.Metadata.UsesUncommittedChanges)
            CurrentProject.UncommitedChanges.Add(change);

        CaptureUndo(change);
        MarkDirty();
    }

    // ── Enum change recording ─────────────────────────────────────────────────

    /// <summary>Records a brand-new enum (whole object).</summary>
    public void RecordEnumAdded(LocEnum locEnum) =>
        AddEnumChange(EntryChangeType.EnumAdded, locEnum.Id, Newtonsoft.Json.JsonConvert.SerializeObject(locEnum));

    /// <summary>Records an edited enum (whole object — name, description and entries).</summary>
    public void RecordEnumUpdated(LocEnum locEnum) =>
        AddEnumChange(EntryChangeType.EnumUpdated, locEnum.Id, Newtonsoft.Json.JsonConvert.SerializeObject(locEnum));

    public void RecordEnumRemoved(Guid enumId) =>
        AddEnumChange(EntryChangeType.EnumRemoved, enumId, string.Empty);

    private void AddEnumChange(EntryChangeType type, Guid enumId, string changeData)
    {
        LocEntryChange change = new()
        {
            Type       = type,
            EntryId    = enumId,
            ChangeData = changeData
        };
        if (CurrentProject!.Metadata.UsesUncommittedChanges)
            CurrentProject.UncommitedChanges.Add(change);

        CaptureUndo(change);
        MarkDirty();
    }

    /// <summary>Marks a key as edited (offline incremental save) without recording an online change.</summary>
    public void MarkKeyDirty(Guid keyId)
    {
        ChangedLocKeys.Add(keyId);
        MarkDirty();
    }

    public void MarkDirty()
    {
        if (!IsDirty)
        {
            IsDirty = true;
            DirtyStateChanged?.Invoke();
        }
        // Always notify data listeners, even if IsDirty was already true —
        // otherwise edits after the first one in a session go unnoticed by
        // components that only refresh on this event (e.g. progress bars).
        ProjectDataChanged?.Invoke();
    }

    public void MarkClean()
    {
        if (!IsDirty) return;
        IsDirty = false;
        DirtyStateChanged?.Invoke();
    }
}

/// <summary>How a single translation conflict should be resolved. See <see cref="ProjectStateService.ResolveConflictAsync"/>.</summary>
public enum ConflictResolution
{
    /// <summary>Overwrite the server's translation with my queued edit.</summary>
    KeepMine,

    /// <summary>Keep the server's translation and add my edit as a suggestion for voting.</summary>
    SuggestMine,

    /// <summary>Discard my queued edit and keep the server's translation.</summary>
    KeepServer,
}