using System;
using System.Collections.Generic;
using DeusaldLocalizerCommon;
using Newtonsoft.Json;

namespace DeusaldLocalizerWeb;

// ── Standalone undo/redo history ─────────────────────────────────────────────
// A session-scoped, mode-agnostic undo stack, independent of the uncommitted-changes queue. Each undoable
// edit is captured as a pair of forward LocEntryChanges — the original (Redo) and a synthesized inverse
// (Undo) — so undo and redo can both be applied through EntryChangeExeService.ExecuteChange and persisted
// the normal way for the current mode. Works in every mode (plain offline, offline uncommitted, online) and
// survives Save/commit/push within a session; it is NOT persisted across app restarts. Every edit is undoable
// except deleting a member (MemberRemoved), which reassigns the member's references to the offline user — a
// lossy operation a single re-add cannot restore — so it advances the baseline but records no undo step.
public partial class ProjectStateService
{
    private sealed class UndoStep
    {
        public LocEntryChange Undo  { get; init; } = null!;
        public LocEntryChange Redo  { get; init; } = null!;
        public string         Label { get; init; } = string.Empty;
    }

    private readonly List<UndoStep> _UndoStack = new();
    private readonly List<UndoStep> _RedoStack = new();

    /// <summary>
    /// A mirror of <see cref="CurrentProject"/> kept in lockstep with every recorded edit. Used to recover
    /// an entity's pre-edit state when synthesizing an inverse change. Reset to a clone of the project
    /// whenever it is (re)loaded, and re-anchored after a sync/push rebuild.
    /// </summary>
    private LocProject? _UndoBaseline;

    /// <summary>True while an undo/redo is being applied, so the replay is not itself captured as a new edit.</summary>
    private bool _ApplyingUndoRedo;

    /// <summary>True when there is at least one edit to undo (works in every mode).</summary>
    public bool CanUndo => _UndoStack.Count > 0;

    /// <summary>True when a previously-undone edit is waiting to be re-applied.</summary>
    public bool CanRedo => _RedoStack.Count > 0;

    /// <summary>Human-readable label of the edit the next undo would reverse (for tooltips); empty when none.</summary>
    public string UndoLabel => _UndoStack.Count > 0 ? _UndoStack[^1].Label : string.Empty;

    /// <summary>Human-readable label of the edit the next redo would re-apply; empty when none.</summary>
    public string RedoLabel => _RedoStack.Count > 0 ? _RedoStack[^1].Label : string.Empty;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <summary>Clears both stacks and re-anchors the baseline to the current project state.</summary>
    private void ResetUndoHistory()
    {
        _UndoStack.Clear();
        _RedoStack.Clear();
        ReanchorUndoBaseline();
    }

    /// <summary>
    /// Re-clones the undo baseline from <see cref="CurrentProject"/> without discarding the stacks. Used after
    /// the project is rebuilt (sync/push/rebuild working copy): the stored undo/redo changes are absolute
    /// snapshots and stay valid, but the baseline that future inverses are derived from must track the new state.
    /// </summary>
    private void ReanchorUndoBaseline() =>
        _UndoBaseline = CurrentProject is null ? null : CloneProject(CurrentProject);

    private static LocProject CloneProject(LocProject project) =>
        JsonConvert.DeserializeObject<LocProject>(JsonConvert.SerializeObject(project))!;

    // ── Capture ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Observes a forward change that the UI has just applied to <see cref="CurrentProject"/>: advances the
    /// baseline to match and, when the change is invertible, pushes a new undo step (clearing the redo stack —
    /// a fresh edit breaks the redo chain). Called by every recording helper, in all modes.
    /// </summary>
    private void CaptureUndo(LocEntryChange forward)
    {
        if (_ApplyingUndoRedo || _UndoBaseline is null) return;

        // Build the inverse against the pre-edit baseline BEFORE advancing it.
        LocEntryChange? inverse = TryBuildInverse(forward, _UndoBaseline);

        // Keep the baseline in step with CurrentProject (which already carries this edit).
        EntryChangeExeService.ExecuteChange(_UndoBaseline, forward, out _);

        // Any genuine new edit breaks the redo chain — even one we cannot undo (key/member creation).
        _RedoStack.Clear();

        if (inverse is null) return;   // not undoable: baseline advanced, no step recorded

        _UndoStack.Add(new UndoStep { Undo = inverse, Redo = forward, Label = DescribeChange(forward) });
    }

    // ── Apply ────────────────────────────────────────────────────────────────

    public void UndoLastChange()
    {
        if (!CanUndo || CurrentProject is null) return;
        UndoStep step = _UndoStack[^1];
        _UndoStack.RemoveAt(_UndoStack.Count - 1);

        ApplyToWorkingCopy(step.Undo);

        // If the edit being undone is still pending (its forward change is the queue tail, not yet pushed),
        // cancel it outright — no need to stage a forward+inverse pair that cancel out. Otherwise (already
        // pushed, or committed offline) stage the inverse as a fresh change so the revert can still be pushed.
        StageOrCancel(cancelIf: step.Redo, otherwiseStage: step.Undo);

        _RedoStack.Add(step);
        MarkDirty();
        ProjectChanged?.Invoke();
        ProjectDataChanged?.Invoke();
    }

    public void RedoLastChange()
    {
        if (!CanRedo || CurrentProject is null) return;
        UndoStep step = _RedoStack[^1];
        _RedoStack.RemoveAt(_RedoStack.Count - 1);

        ApplyToWorkingCopy(step.Redo);

        // Mirror of undo: if the last undo had merely staged the inverse, drop it again; otherwise re-stage
        // the original edit.
        StageOrCancel(cancelIf: step.Undo, otherwiseStage: step.Redo);

        _UndoStack.Add(step);
        MarkDirty();
        ProjectChanged?.Invoke();
        ProjectDataChanged?.Invoke();
    }

    /// <summary>
    /// Reverts/redoes the visible state by applying one change to the working copy (and the baseline, kept in
    /// lockstep) and dirty-tracking the affected key. Does not touch the uncommitted queue — see
    /// <see cref="StageOrCancel"/> for that. Like a normal edit it only marks dirty; persistence is on Save/Push.
    /// </summary>
    private void ApplyToWorkingCopy(LocEntryChange change)
    {
        if (CurrentProject is null) return;

        _ApplyingUndoRedo = true;
        try
        {
            EntryChangeExeService.ExecuteChange(CurrentProject, change, out _);
            if (_UndoBaseline is not null)
                EntryChangeExeService.ExecuteChange(_UndoBaseline, change, out _);

            if (IsKeyScoped(change.Type)) ChangedLocKeys.Add(change.EntryId);
        }
        finally
        {
            _ApplyingUndoRedo = false;
        }
    }

    /// <summary>
    /// Keeps the uncommitted queue minimal across undo/redo (uncommitted-mode projects only): if
    /// <paramref name="cancelIf"/> is still the pending tail, remove it; otherwise append
    /// <paramref name="otherwiseStage"/> as a fresh pending change. Reference identity is used, so a change
    /// that has since been pushed (and cleared from the queue) falls through to staging.
    /// </summary>
    private void StageOrCancel(LocEntryChange cancelIf, LocEntryChange otherwiseStage)
    {
        if (CurrentProject is null || !CurrentProject.Metadata.UsesUncommittedChanges) return;

        List<LocEntryChange> queue = CurrentProject.UncommitedChanges;
        if (queue.Count > 0 && ReferenceEquals(queue[^1], cancelIf))
            queue.RemoveAt(queue.Count - 1);
        else
            queue.Add(otherwiseStage);
    }

    private static bool IsKeyScoped(EntryChangeType type) => type switch
    {
        EntryChangeType.KeyAdded           => true,
        EntryChangeType.KeyUpdated         => true,
        EntryChangeType.KeyRemoved         => true,
        EntryChangeType.TranslationUpdated => true,
        EntryChangeType.SuggestionAdded    => true,
        EntryChangeType.SuggestionVoted    => true,
        EntryChangeType.SuggestionRemoved  => true,
        EntryChangeType.FlagAdded          => true,
        EntryChangeType.FlagRemoved        => true,
        EntryChangeType.TagAdded           => true,
        EntryChangeType.TagRemoved         => true,
        EntryChangeType.VariableAdded      => true,
        EntryChangeType.VariableUpdated    => true,
        EntryChangeType.VariableRemoved    => true,
        _                                  => false
    };

    // ── Inverse synthesis ────────────────────────────────────────────────────

    /// <summary>
    /// Builds the change that reverses <paramref name="c"/>, reading any needed pre-edit values from
    /// <paramref name="baseline"/> (still holding the state from before the edit). Returns null when the
    /// change is not invertible (key/member creation) or the baseline is missing the referenced entity.
    /// </summary>
    private static LocEntryChange? TryBuildInverse(LocEntryChange c, LocProject baseline)
    {
        switch (c.Type)
        {
            case EntryChangeType.KeyAdded:
                return new LocEntryChange { Type = EntryChangeType.KeyRemoved, EntryId = c.EntryId };

            case EntryChangeType.KeyRemoved:
            {
                LocLocalizationKey? key = baseline.Keys.Find(k => k.Id == c.EntryId);
                return key is null
                           ? null
                           : new LocEntryChange { Type = EntryChangeType.KeyAdded, EntryId = c.EntryId, ChangeData = Json(key) };
            }

            case EntryChangeType.MemberAdded:
                return new LocEntryChange { Type = EntryChangeType.MemberRemoved, EntryId = c.EntryId };

            // Deleting a member reassigns its suggestion/vote/flag references to the offline user, which a
            // single re-add cannot restore — so member deletion is intentionally not undoable.
            case EntryChangeType.MemberRemoved:
                return null;

            case EntryChangeType.KeyUpdated:
            {
                LocLocalizationKey? key = baseline.Keys.Find(k => k.Id == c.EntryId);
                if (key is null) return null;
                string? before = c.EntrySubId switch
                {
                    nameof(LocLocalizationKey.KeyName)     => key.KeyName,
                    nameof(LocLocalizationKey.CategoryId)  => key.CategoryId.ToString(),
                    nameof(LocLocalizationKey.MaxLength)   => key.MaxLength.ToString(),
                    nameof(LocLocalizationKey.Description) => key.Description,
                    _                                      => null
                };
                return before is null ? null : Field(EntryChangeType.KeyUpdated, c.EntryId, c.EntrySubId, before);
            }

            case EntryChangeType.TranslationUpdated:
            {
                LocLocalizationKey? key    = baseline.Keys.Find(k => k.Id == c.EntryId);
                LocKeyTranslation?  before = key?.Translations.Find(t => t.LanguageId == c.EntrySubId);
                // If no translation existed before, revert to an empty one for that language.
                before ??= new LocKeyTranslation { LanguageId = c.EntrySubId };
                // Mirror RecordTranslationUpdated's hash contract so an online push can still conflict-check:
                // prev source = the restored translation's base hash; prev dest = hash of the text we are
                // reverting away from (the current/after text carried by the forward change).
                string afterText = TryDeserialize<LocKeyTranslation>(c.ChangeData)?.Text ?? string.Empty;
                return new LocEntryChange
                {
                    Type               = EntryChangeType.TranslationUpdated,
                    EntryId            = c.EntryId,
                    EntrySubId         = c.EntrySubId,
                    ChangeData         = Json(before),
                    PrevSourceHashData = before.BaseTextHash,
                    PrevDestHashData   = TextHashHelper.Compute(afterText)
                };
            }

            case EntryChangeType.CategoryAdded:
                return new LocEntryChange { Type = EntryChangeType.CategoryRemoved, EntryId = c.EntryId };

            case EntryChangeType.CategoryRemoved:
            {
                LocCategory? cat = baseline.Categories.Find(x => x.Id == c.EntryId);
                return cat is null
                           ? null
                           : new LocEntryChange { Type = EntryChangeType.CategoryAdded, EntryId = c.EntryId, ChangeData = Json(cat) };
            }

            case EntryChangeType.CategoryUpdated:
            {
                LocCategory? cat = baseline.Categories.Find(x => x.Id == c.EntryId);
                if (cat is null) return null;
                string? before = c.EntrySubId switch
                {
                    nameof(LocCategory.Name)             => cat.Name,
                    nameof(LocCategory.Description)      => cat.Description,
                    nameof(LocCategory.ParentCategoryId) => cat.ParentCategoryId?.ToString() ?? string.Empty,
                    _                                    => null
                };
                return before is null ? null : Field(EntryChangeType.CategoryUpdated, c.EntryId, c.EntrySubId, before);
            }

            case EntryChangeType.EnumAdded:
                return new LocEntryChange { Type = EntryChangeType.EnumRemoved, EntryId = c.EntryId };

            case EntryChangeType.EnumRemoved:
            {
                LocEnum? e = baseline.Enums.Find(x => x.Id == c.EntryId);
                return e is null
                           ? null
                           : new LocEntryChange { Type = EntryChangeType.EnumAdded, EntryId = c.EntryId, ChangeData = Json(e) };
            }

            case EntryChangeType.EnumUpdated:
            {
                LocEnum? e = baseline.Enums.Find(x => x.Id == c.EntryId);
                return e is null
                           ? null
                           : new LocEntryChange { Type = EntryChangeType.EnumUpdated, EntryId = c.EntryId, ChangeData = Json(e) };
            }

            case EntryChangeType.LanguageAdded:
                return new LocEntryChange { Type = EntryChangeType.LanguageRemoved, EntryId = c.EntryId, ChangeData = c.ChangeData };

            case EntryChangeType.LanguageRemoved:
                return new LocEntryChange { Type = EntryChangeType.LanguageAdded, EntryId = c.EntryId, ChangeData = c.ChangeData };

            case EntryChangeType.MemberUpdated:
            {
                LocProjectMember? m = baseline.ProjectMembers.Find(x => x.UserId == c.EntryId);
                if (m is null) return null;
                string? before = c.EntrySubId switch
                {
                    nameof(LocProjectMember.Username)                  => m.Username,
                    nameof(LocProjectMember.ReviewLanguagePermissions) => Json(m.ReviewLanguagePermissions),
                    nameof(LocProjectMember.IsBanned)                  => m.IsBanned.ToString(),
                    nameof(LocProjectMember.HashedAccessToken)         => m.HashedAccessToken,
                    nameof(LocProjectMember.MustResetAccessToken)      => m.MustResetAccessToken.ToString(),
                    _                                                  => null
                };
                return before is null ? null : Field(EntryChangeType.MemberUpdated, c.EntryId, c.EntrySubId, before);
            }

            case EntryChangeType.SuggestionAdded:
            {
                LocTranslationSuggestion? s = TryDeserialize<LocTranslationSuggestion>(c.ChangeData);
                return s is null
                           ? null
                           : new LocEntryChange { Type = EntryChangeType.SuggestionRemoved, EntryId = c.EntryId, EntrySubId = c.EntrySubId, ChangeData = s.Id.ToString() };
            }

            case EntryChangeType.SuggestionRemoved:
            {
                if (!Guid.TryParse(c.ChangeData, out Guid sid)) return null;
                LocTranslationSuggestion? s = FindSuggestion(baseline, c.EntryId, c.EntrySubId, sid);
                return s is null
                           ? null
                           : new LocEntryChange { Type = EntryChangeType.SuggestionAdded, EntryId = c.EntryId, EntrySubId = c.EntrySubId, ChangeData = Json(s) };
            }

            case EntryChangeType.SuggestionVoted:
            {
                LocTranslationSuggestion? after = TryDeserialize<LocTranslationSuggestion>(c.ChangeData);
                if (after is null) return null;
                LocTranslationSuggestion? before = FindSuggestion(baseline, c.EntryId, c.EntrySubId, after.Id);
                return before is null
                           ? null
                           : new LocEntryChange { Type = EntryChangeType.SuggestionVoted, EntryId = c.EntryId, EntrySubId = c.EntrySubId, ChangeData = Json(before) };
            }

            case EntryChangeType.FlagAdded:
            {
                LocKeyFlag? f = TryDeserialize<LocKeyFlag>(c.ChangeData);
                return f is null
                           ? null
                           : new LocEntryChange { Type = EntryChangeType.FlagRemoved, EntryId = c.EntryId, ChangeData = f.Id.ToString() };
            }

            case EntryChangeType.FlagRemoved:
            {
                if (!Guid.TryParse(c.ChangeData, out Guid fid)) return null;
                LocLocalizationKey? key = baseline.Keys.Find(k => k.Id == c.EntryId);
                LocKeyFlag?         f   = key?.Flags.Find(x => x.Id == fid);
                return f is null
                           ? null
                           : new LocEntryChange { Type = EntryChangeType.FlagAdded, EntryId = c.EntryId, ChangeData = Json(f) };
            }

            case EntryChangeType.TagAdded:
                return new LocEntryChange { Type = EntryChangeType.TagRemoved, EntryId = c.EntryId, ChangeData = c.ChangeData };

            case EntryChangeType.TagRemoved:
                return new LocEntryChange { Type = EntryChangeType.TagAdded, EntryId = c.EntryId, ChangeData = c.ChangeData };

            case EntryChangeType.VariableAdded:
            {
                LocKeyVariable? v = TryDeserialize<LocKeyVariable>(c.ChangeData);
                return v is null
                           ? null
                           : new LocEntryChange { Type = EntryChangeType.VariableRemoved, EntryId = c.EntryId, ChangeData = v.Id.ToString() };
            }

            case EntryChangeType.VariableUpdated:
            {
                if (!Guid.TryParse(c.EntrySubId, out Guid vid)) return null;
                LocLocalizationKey? key = baseline.Keys.Find(k => k.Id == c.EntryId);
                LocKeyVariable?     v   = key?.Variables.Find(x => x.Id == vid);
                return v is null
                           ? null
                           : new LocEntryChange { Type = EntryChangeType.VariableUpdated, EntryId = c.EntryId, EntrySubId = c.EntrySubId, ChangeData = Json(v) };
            }

            case EntryChangeType.VariableRemoved:
            {
                if (!Guid.TryParse(c.ChangeData, out Guid vid)) return null;
                LocLocalizationKey? key = baseline.Keys.Find(k => k.Id == c.EntryId);
                LocKeyVariable?     v   = key?.Variables.Find(x => x.Id == vid);
                return v is null
                           ? null
                           : new LocEntryChange { Type = EntryChangeType.VariableAdded, EntryId = c.EntryId, ChangeData = Json(v) };
            }

            default:
                return null;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string Json(object o) => JsonConvert.SerializeObject(o);

    private static T? TryDeserialize<T>(string json) where T : class =>
        string.IsNullOrEmpty(json) ? null : JsonConvert.DeserializeObject<T>(json);

    private static LocEntryChange Field(EntryChangeType type, Guid id, string subId, string data) =>
        new() { Type = type, EntryId = id, EntrySubId = subId, ChangeData = data };

    private static LocTranslationSuggestion? FindSuggestion(LocProject project, Guid keyId, string lang, Guid suggestionId)
    {
        LocLocalizationKey? key = project.Keys.Find(k => k.Id == keyId);
        LocKeyTranslation?  tr  = key?.Translations.Find(t => t.LanguageId == lang);
        return tr?.Suggestions.Find(s => s.Id == suggestionId);
    }

    private static string DescribeChange(LocEntryChange c) => c.Type switch
    {
        EntryChangeType.TranslationUpdated => "translation edit",
        EntryChangeType.KeyUpdated         => "key change",
        EntryChangeType.KeyAdded           => "add key",
        EntryChangeType.KeyRemoved         => "delete key",
        EntryChangeType.MemberAdded        => "add member",
        EntryChangeType.CategoryAdded      => "add category",
        EntryChangeType.CategoryUpdated    => "category change",
        EntryChangeType.CategoryRemoved    => "delete category",
        EntryChangeType.EnumAdded          => "add enum",
        EntryChangeType.EnumUpdated        => "enum change",
        EntryChangeType.EnumRemoved        => "delete enum",
        EntryChangeType.LanguageAdded      => "add language",
        EntryChangeType.LanguageRemoved    => "remove language",
        EntryChangeType.SuggestionAdded    => "add suggestion",
        EntryChangeType.SuggestionVoted    => "vote",
        EntryChangeType.SuggestionRemoved  => "remove suggestion",
        EntryChangeType.FlagAdded          => "add flag",
        EntryChangeType.FlagRemoved        => "remove flag",
        EntryChangeType.TagAdded           => "add tag",
        EntryChangeType.TagRemoved         => "remove tag",
        EntryChangeType.VariableAdded      => "add variable",
        EntryChangeType.VariableUpdated    => "variable change",
        EntryChangeType.VariableRemoved    => "remove variable",
        EntryChangeType.MemberUpdated      => "member change",
        _                                  => "change"
    };
}
