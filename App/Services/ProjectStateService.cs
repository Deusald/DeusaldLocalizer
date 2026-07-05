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
    public LocProject? CurrentProject { get; private set; }

    /// <summary>Path on disk where the current project was loaded from / last saved to.</summary>
    public string? CurrentProjectPath { get; private set; }

    /// <summary>The currently authenticated user. Always set — offline user when no real login.</summary>
    public LocProjectMember CurrentUser { get; private set; } = LocProjectMember.OfflineMember;

    public Guid AccessToken { get; set; } = Guid.Empty;

    public HashSet<Guid> ChangedLocKeys { get; } = new();

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
        CurrentUser = LocProjectMember.OfflineMember;
        ProjectChanged?.Invoke();
        DirtyStateChanged?.Invoke();
    }

    public void LoadProject(LocProject project, string folderPath, Guid userId, Guid accessToken)
    {
        CurrentProject     = project;
        CurrentProjectPath = folderPath;
        IsDirty            = false;
        ChangedLocKeys.Clear();
        CurrentUser = userId == LocProjectMember.OfflineMember.UserId ? LocProjectMember.OfflineMember : project.ProjectMembers.Find(m => m.UserId == userId)!;
        AccessToken = accessToken;
        foreach (LocEntryChange uncommitedChange in project.UncommitedChanges) EntryChangeExeService.ExecuteChange(project, uncommitedChange, out _);
        ProjectChanged?.Invoke();
        DirtyStateChanged?.Invoke();
    }

    public void CloseProject()
    {
        CurrentProject     = null;
        CurrentProjectPath = null;
        IsDirty            = false;
        ChangedLocKeys.Clear();
        CurrentUser = LocProjectMember.OfflineMember;
        ProjectChanged?.Invoke();
        DirtyStateChanged?.Invoke();
    }

    public async Task SaveAsync()
    {
        if (CurrentProject!.Metadata.IsOnline)
        {
            // We should only save uncommited changes
            await ProjectFileService.SaveUncommittedOnlyAsync(CurrentProject, CurrentProjectPath!);
            return;
        }

        if (string.IsNullOrEmpty(CurrentProjectPath))
        {
            FolderPickerResult result = await FolderPicker.Default.PickAsync();

            if (result.IsSuccessful)
            {
                CurrentProjectPath = result.Folder.Path;
                await ProjectFileService.SaveAsync(CurrentProject, CurrentProjectPath);
                MarkClean();
            }
        }
        else
        {
            await ProjectFileService.SaveIncrementalAsync(CurrentProject, CurrentProjectPath, ChangedLocKeys);
            MarkClean();
        }

        if (!string.IsNullOrEmpty(CurrentProjectPath)) RecentProjectsService.UpdateRecentProjects(CurrentProject!, CurrentProjectPath, CurrentProject.Metadata.IsOnline);
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