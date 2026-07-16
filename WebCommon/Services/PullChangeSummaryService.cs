using DeusaldLocalizerCommon;

namespace DeusaldLocalizerWeb;

/// <summary>
/// Derives a human-readable list of what a server pull changed by diffing two clean project baselines
/// (the committed server state before the pull vs. after). The sync payload is file-level only, so the
/// client reconstructs the change list itself. Pragmatic by design — it surfaces the change types worth
/// reviewing (translations, suggestions, comments, add/remove) grouped per key, not an exhaustive
/// field-level diff.
/// </summary>
public static class PullChangeSummaryService
{
    public static List<PullChange> Diff(LocProject oldBase, LocProject newBase)
    {
        List<PullChange> changes = [];

        Dictionary<Guid, LocLocalizationKey> oldKeys = oldBase.Keys.ToDictionary(k => k.Id);
        Dictionary<Guid, LocLocalizationKey> newKeys = newBase.Keys.ToDictionary(k => k.Id);

        // Added / removed keys.
        foreach (LocLocalizationKey key in newBase.Keys)
        {
            if (!oldKeys.ContainsKey(key.Id))
                changes.Add(new PullChange { KeyId = key.Id, Scope = PullChangeScope.Key, Description = "Key added" });
        }
        foreach (LocLocalizationKey key in oldBase.Keys)
        {
            if (!newKeys.ContainsKey(key.Id))
                changes.Add(new PullChange { Scope = PullChangeScope.Other, Description = $"Key removed: {key.KeyName}" });
        }

        // Changes within keys present in both baselines.
        foreach (LocLocalizationKey newKey in newBase.Keys)
        {
            if (!oldKeys.TryGetValue(newKey.Id, out LocLocalizationKey? oldKey))
                continue;

            DiffKey(oldKey, newKey, changes);
        }

        DiffNonKeys(oldBase, newBase, changes);
        return changes;
    }

    private static void DiffKey(LocLocalizationKey oldKey, LocLocalizationKey newKey, List<PullChange> changes)
    {
        // Key-scope comments (not tied to a language).
        int addedKeyComments = NewCommentCount(oldKey.Comments, newKey.Comments);
        if (addedKeyComments > 0)
            changes.Add(Comment(newKey.Id, "", addedKeyComments));

        Dictionary<string, LocKeyTranslation> oldTr = oldKey.Translations.ToDictionary(t => t.LanguageId);

        foreach (LocKeyTranslation newTr in newKey.Translations)
        {
            oldTr.TryGetValue(newTr.LanguageId, out LocKeyTranslation? oldTrans);
            DiffTranslation(newKey.Id, oldTrans, newTr, changes);
        }
    }

    private static void DiffTranslation(
        Guid keyId, LocKeyTranslation? oldTr, LocKeyTranslation newTr, List<PullChange> changes)
    {
        string lang = newTr.LanguageId;

        if (oldTr is null)
        {
            if (!string.IsNullOrEmpty(newTr.Text))
                changes.Add(new PullChange { KeyId = keyId, LanguageId = lang, Scope = PullChangeScope.Translation, Description = "Translation added" });
        }
        else if (oldTr.Text != newTr.Text)
        {
            changes.Add(new PullChange { KeyId = keyId, LanguageId = lang, Scope = PullChangeScope.Translation, Description = "Translation updated" });
        }
        else if (oldTr.Status != newTr.Status)
        {
            changes.Add(new PullChange { KeyId = keyId, LanguageId = lang, Scope = PullChangeScope.Translation, Description = $"Marked {newTr.Status.ToString().ToLowerInvariant()}" });
        }

        // Translation-scope comments.
        int addedComments = NewCommentCount(oldTr?.Comments, newTr.Comments);
        if (addedComments > 0)
            changes.Add(Comment(keyId, lang, addedComments));

        DiffSuggestions(keyId, lang, oldTr?.Suggestions, newTr.Suggestions, changes);
    }

    private static void DiffSuggestions(
        Guid keyId, string lang,
        List<LocTranslationSuggestion>? oldSuggestions, List<LocTranslationSuggestion> newSuggestions,
        List<PullChange> changes)
    {
        Dictionary<Guid, LocTranslationSuggestion> oldById =
            oldSuggestions?.ToDictionary(s => s.Id) ?? new Dictionary<Guid, LocTranslationSuggestion>();

        foreach (LocTranslationSuggestion newSug in newSuggestions)
        {
            if (!oldById.TryGetValue(newSug.Id, out LocTranslationSuggestion? oldSug))
            {
                changes.Add(new PullChange { KeyId = keyId, LanguageId = lang, Scope = PullChangeScope.Suggestion, Description = "New suggestion" });
                continue;
            }

            if (VoteScore(oldSug.Votes) != VoteScore(newSug.Votes))
                changes.Add(new PullChange { KeyId = keyId, LanguageId = lang, Scope = PullChangeScope.Suggestion, Description = "Suggestion votes changed" });

            int addedComments = NewCommentCount(oldSug.Comments, newSug.Comments);
            if (addedComments > 0)
                changes.Add(Comment(keyId, lang, addedComments));
        }

        // A suggestion disappearing means it was accepted or rejected.
        Dictionary<Guid, LocTranslationSuggestion> newById = newSuggestions.ToDictionary(s => s.Id);
        if (oldSuggestions != null)
        {
            foreach (LocTranslationSuggestion oldSug in oldSuggestions)
            {
                if (!newById.ContainsKey(oldSug.Id))
                    changes.Add(new PullChange { KeyId = keyId, LanguageId = lang, Scope = PullChangeScope.Suggestion, Description = "Suggestion resolved" });
            }
        }
    }

    private static void DiffNonKeys(LocProject oldBase, LocProject newBase, List<PullChange> changes)
    {
        // Members (added only — the common online change).
        HashSet<Guid> oldMembers = oldBase.ProjectMembers.Select(m => m.UserId).ToHashSet();
        foreach (LocProjectMember member in newBase.ProjectMembers)
        {
            if (!oldMembers.Contains(member.UserId))
                changes.Add(new PullChange { Scope = PullChangeScope.Other, Description = $"Member added: {member.Username}" });
        }

        // Languages.
        foreach (string lang in newBase.Metadata.Languages.Except(oldBase.Metadata.Languages))
            changes.Add(new PullChange { Scope = PullChangeScope.Other, Description = $"Language added: {lang}" });
        foreach (string lang in oldBase.Metadata.Languages.Except(newBase.Metadata.Languages))
            changes.Add(new PullChange { Scope = PullChangeScope.Other, Description = $"Language removed: {lang}" });

        // Categories (structural add/remove; renames fold into add/remove of ids being equal so only count/name).
        Dictionary<Guid, LocCategory> oldCats = oldBase.Categories.ToDictionary(c => c.Id);
        Dictionary<Guid, LocCategory> newCats = newBase.Categories.ToDictionary(c => c.Id);
        foreach (LocCategory cat in newBase.Categories)
        {
            if (!oldCats.ContainsKey(cat.Id))
                changes.Add(new PullChange { Scope = PullChangeScope.Other, Description = $"Category added: {cat.Name}" });
            else if (oldCats[cat.Id].Name != cat.Name)
                changes.Add(new PullChange { Scope = PullChangeScope.Other, Description = $"Category renamed: {cat.Name}" });
        }
        foreach (LocCategory cat in oldBase.Categories)
        {
            if (!newCats.ContainsKey(cat.Id))
                changes.Add(new PullChange { Scope = PullChangeScope.Other, Description = $"Category removed: {cat.Name}" });
        }
    }

    private static PullChange Comment(Guid keyId, string lang, int count) => new()
    {
        KeyId       = keyId,
        LanguageId  = lang,
        Scope       = PullChangeScope.Comment,
        Description = count == 1 ? "Comment added" : $"{count} comments added",
    };

    /// <summary>Count of comments present in <paramref name="updated"/> but not in <paramref name="original"/> (matched by id).</summary>
    private static int NewCommentCount(List<LocComment>? original, List<LocComment>? updated)
    {
        if (updated is null || updated.Count == 0) return 0;
        HashSet<Guid> existing = original?.Select(c => c.Id).ToHashSet() ?? [];
        return updated.Count(c => !existing.Contains(c.Id));
    }

    private static int VoteScore(List<LocSuggestionVote> votes) => votes.Sum(v => v.Value);
}
