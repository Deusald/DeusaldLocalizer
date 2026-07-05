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

    // ── Member change recording ──────────────────────────────────────────────
    
    /// <summary>Records a brand-new member (whole object).</summary>
    public void RecordMemberAdded(LocProjectMember member)
    {
        if (CurrentProject!.Metadata.IsOnline)
        {
            CurrentProject.UncommitedChanges.Add(new LocEntryChange
            {
                Type       = EntryChangeType.MemberAdded,
                EntryId    = member.UserId,
                ChangeData = Newtonsoft.Json.JsonConvert.SerializeObject(member)
            });
        }

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

    private void AddMemberFieldChange(LocProjectMember member, string fieldName, string changeData)
    {
        if (CurrentProject!.Metadata.IsOnline)
        {
            CurrentProject.UncommitedChanges.Add(new LocEntryChange
            {
                Type       = EntryChangeType.MemberUpdated,
                EntryId    = member.UserId,
                EntrySubId = fieldName,
                ChangeData = changeData
            });
        }

        MarkDirty();
    }

    // ── Language change recording ─────────────────────────────────────────────

    public void RecordLanguageAdded(string code) =>
        AddLanguageChange(EntryChangeType.LanguageAdded, code);

    public void RecordLanguageRemoved(string code) =>
        AddLanguageChange(EntryChangeType.LanguageRemoved, code);

    private void AddLanguageChange(EntryChangeType type, string code)
    {
        if (CurrentProject!.Metadata.IsOnline)
        {
            CurrentProject.UncommitedChanges.Add(new LocEntryChange
            {
                Type       = type,
                EntryId    = CurrentProject.Metadata.Id,
                ChangeData = code
            });
        }

        MarkDirty();
    }

    // ── Key change recording ──────────────────────────────────────────────────

    /// <summary>Records a brand-new key (whole object).</summary>
    public void RecordKeyAdded(LocLocalizationKey key)
    {
        ChangedLocKeys.Add(key.Id);
        if (CurrentProject!.Metadata.IsOnline)
        {
            CurrentProject.UncommitedChanges.Add(new LocEntryChange
            {
                Type       = EntryChangeType.KeyAdded,
                EntryId    = key.Id,
                ChangeData = Newtonsoft.Json.JsonConvert.SerializeObject(key)
            });
        }

        MarkDirty();
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
        if (CurrentProject!.Metadata.IsOnline)
        {
            CurrentProject.UncommitedChanges.Add(new LocEntryChange
            {
                Type       = EntryChangeType.KeyUpdated,
                EntryId    = key.Id,
                EntrySubId = fieldName,
                ChangeData = changeData
            });
        }

        MarkDirty();
    }

    // ── Category change recording ─────────────────────────────────────────────

    /// <summary>Records a brand-new category (whole object).</summary>
    public void RecordCategoryAdded(LocCategory category)
    {
        if (CurrentProject!.Metadata.IsOnline)
        {
            CurrentProject.UncommitedChanges.Add(new LocEntryChange
            {
                Type       = EntryChangeType.CategoryAdded,
                EntryId    = category.Id,
                ChangeData = Newtonsoft.Json.JsonConvert.SerializeObject(category)
            });
        }

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
        if (CurrentProject!.Metadata.IsOnline)
        {
            CurrentProject.UncommitedChanges.Add(new LocEntryChange
            {
                Type    = EntryChangeType.CategoryRemoved,
                EntryId = category.Id
            });
        }

        MarkDirty();
    }

    private void AddCategoryFieldChange(LocCategory category, string fieldName, string changeData)
    {
        if (CurrentProject!.Metadata.IsOnline)
        {
            CurrentProject.UncommitedChanges.Add(new LocEntryChange
            {
                Type       = EntryChangeType.CategoryUpdated,
                EntryId    = category.Id,
                EntrySubId = fieldName,
                ChangeData = changeData
            });
        }

        MarkDirty();
    }

    // ── Key description recording ─────────────────────────────────────────────

    public void RecordKeyDescriptionChanged(LocLocalizationKey key) =>
        AddKeyFieldChange(key, nameof(LocLocalizationKey.Description), key.Description);

    // ── Translation change recording ──────────────────────────────────────────

    public void RecordTranslationUpdated(Guid keyId, LocKeyTranslation translation) =>
        AddKeyChange(keyId, EntryChangeType.TranslationUpdated, translation.LanguageId,
            Newtonsoft.Json.JsonConvert.SerializeObject(translation), translation.BaseTextHash);

    // ── Suggestion change recording ───────────────────────────────────────────

    public void RecordSuggestionAdded(Guid keyId, string languageId, LocTranslationSuggestion suggestion) =>
        AddKeyChange(keyId, EntryChangeType.SuggestionAdded, languageId,
            Newtonsoft.Json.JsonConvert.SerializeObject(suggestion));

    public void RecordSuggestionVoted(Guid keyId, string languageId, LocTranslationSuggestion suggestion) =>
        AddKeyChange(keyId, EntryChangeType.SuggestionVoted, languageId,
            Newtonsoft.Json.JsonConvert.SerializeObject(suggestion));

    public void RecordSuggestionRemoved(Guid keyId, string languageId, Guid suggestionId) =>
        AddKeyChange(keyId, EntryChangeType.SuggestionRemoved, languageId, suggestionId.ToString());

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

    private void AddKeyChange(Guid keyId, EntryChangeType type, string entrySubId, string changeData, string prevHashData = "")
    {
        ChangedLocKeys.Add(keyId);
        if (CurrentProject!.Metadata.IsOnline)
        {
            CurrentProject.UncommitedChanges.Add(new LocEntryChange
            {
                Type         = type,
                EntryId      = keyId,
                EntrySubId   = entrySubId,
                ChangeData   = changeData,
                PrevHashData = prevHashData
            });
        }

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
        if (CurrentProject!.Metadata.IsOnline)
        {
            CurrentProject.UncommitedChanges.Add(new LocEntryChange
            {
                Type       = type,
                EntryId    = enumId,
                ChangeData = changeData
            });
        }

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