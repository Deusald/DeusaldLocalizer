using CommunityToolkit.Maui.Storage;
using DeusaldLocalizerCommon;
using JetBrains.Annotations;

namespace App;

/// <summary>
/// Holds the currently open project and active user for the lifetime of the app session.
/// Inject as a singleton so all pages share the same state.
/// </summary>
[PublicAPI]
public class ProjectStateService
{
    // ── State ────────────────────────────────────────────────────────────────

    /// <summary>The currently loaded project. Null when no project is open.</summary>
    public ProjectDto? CurrentProject { get; private set; }

    /// <summary>Path on disk where the current project was loaded from / last saved to.</summary>
    public string? CurrentFilePath { get; private set; }

    /// <summary>The currently authenticated user. Always set — offline user when no real login.</summary>
    public UserDto CurrentUser { get; private set; } = UserDto.CreateOfflineUser();

    /// <summary>True when a project is open and ready to use.</summary>
    public bool HasProject => CurrentProject is not null;

    /// <summary>True when there are unsaved changes.</summary>
    public bool IsDirty { get; private set; }
    
    public BaseBackendService BackendService { get; private set; } = null!;

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

    // ── Actions ──────────────────────────────────────────────────────────────

    public void LoadOfflineProject(ProjectDto project, string filePath)
    {
        CurrentProject  = project;
        CurrentFilePath = filePath;
        IsDirty         = false;
        BackendService  = new BaseBackendService(this);
        ProjectChanged?.Invoke();
        DirtyStateChanged?.Invoke();
    }

    public async Task SaveAsync()
    {
        if (string.IsNullOrEmpty(CurrentFilePath))
        {
            // New project never saved — use FileSaver to pick a location.
            using var stream = new MemoryStream();
            await DlocFileService.SaveToStreamAsync(CurrentProject!, stream);
            stream.Position = 0;

            string          fileName = string.IsNullOrEmpty(CurrentProject!.Slug) ? "project" : CurrentProject.Slug;
            FileSaverResult result   = await FileSaver.Default.SaveAsync($"{fileName}{DlocFileService.FILE_EXTENSION}", stream);

            if (result.IsSuccessful)
            {
                UpdateFilePath(result.FilePath);
                MarkClean();
            }
        }
        else
        {
            await DlocFileService.SaveAsync(CurrentProject!, CurrentFilePath);
            MarkClean();
        }

        if (!string.IsNullOrEmpty(CurrentFilePath)) RecentProjectsService.UpdateRecentProjects(CurrentProject!, CurrentFilePath, false);
    }

    public void CreateNewProject(ProjectDto project)
    {
        CurrentProject  = project;
        CurrentFilePath = null;
        IsDirty         = true;
        ProjectChanged?.Invoke();
        DirtyStateChanged?.Invoke();
    }

    public void CloseProject()
    {
        CurrentProject  = null;
        CurrentFilePath = null;
        IsDirty         = false;
        ProjectChanged?.Invoke();
        DirtyStateChanged?.Invoke();
    }

    public void UpdateFilePath(string filePath)
    {
        CurrentFilePath = filePath;
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

    public void LoginAsOffline()
    {
        CurrentUser = UserDto.CreateOfflineUser();
    }
    
    /// <summary>
    /// Returns the member record for the current user in the open project,
    /// or null if the user is not a member (or no project is open).
    /// </summary>
    public ProjectMemberDto? GetCurrentMember()
    {
        if (CurrentProject is null) return null;
        return CurrentProject.Members.Find(m => m.UserId == CurrentUser.Id);
    }

    /// <summary>Checks whether the current user has ALL of the specified permission flags.</summary>
    public bool CurrentUserHas(PermissionFlags flags)
    {
        ProjectMemberDto? member = GetCurrentMember();
        if (member is null) return false;
        return (member.Permissions & flags) == flags;
    }

    public bool IsCurrentUserSuperAdmin()
    {
        ProjectMemberDto? member = GetCurrentMember();
        return member is not null && member.IsSuperAdmin;
    }
}