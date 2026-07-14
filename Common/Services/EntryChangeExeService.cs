using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace DeusaldLocalizerCommon
{
    public static class EntryChangeExeService
    {
        public static void ExecuteChange(LocProject project, LocEntryChange change, out string commitString)
        {
            commitString = string.Empty;

            switch (change.Type)
            {
                case EntryChangeType.MemberAdded:
                {
                    LocProjectMember? member = JsonConvert.DeserializeObject<LocProjectMember>(change.ChangeData);
                    if (member == null) return;

                    if (project.ProjectMembers.All(m => m.UserId != member.UserId))
                        project.ProjectMembers.Add(member);

                    commitString = $"content(Localization): Added member {member.Username}";
                    break;
                }
                case EntryChangeType.MemberUpdated:
                {
                    LocProjectMember? existing = project.ProjectMembers.Find(m => m.UserId == change.EntryId);
                    if (existing == null) return;

                    switch (change.EntrySubId)
                    {
                        case nameof(LocProjectMember.Username):
                            existing.Username = change.ChangeData;
                            break;
                        case nameof(LocProjectMember.ReviewLanguagePermissions):
                            existing.ReviewLanguagePermissions =
                                JsonConvert.DeserializeObject<HashSet<string>>(change.ChangeData) ?? new HashSet<string>();
                            break;
                        case nameof(LocProjectMember.IsBanned):
                            existing.IsBanned = bool.TryParse(change.ChangeData, out bool banned) && banned;
                            break;
                        case nameof(LocProjectMember.HashedAccessToken):
                            existing.HashedAccessToken = change.ChangeData;
                            break;
                        case nameof(LocProjectMember.MustResetAccessToken):
                            existing.MustResetAccessToken = bool.TryParse(change.ChangeData, out bool mustReset) && mustReset;
                            break;
                    }

                    commitString = $"content(Localization): Updated member {existing.Username} ({change.EntrySubId})";
                    break;
                }
                case EntryChangeType.LanguageAdded:
                {
                    string code = change.ChangeData;
                    if (!string.IsNullOrEmpty(code) && !project.Metadata.Languages.Contains(code))
                        project.Metadata.Languages.Add(code);

                    commitString = $"content(Localization): Added language {code}";
                    break;
                }
                case EntryChangeType.LanguageRemoved:
                {
                    string code = change.ChangeData;
                    project.Metadata.Languages.Remove(code);

                    commitString = $"content(Localization): Removed language {code}";
                    break;
                }
                case EntryChangeType.KeyAdded:
                {
                    LocLocalizationKey? key = JsonConvert.DeserializeObject<LocLocalizationKey>(change.ChangeData);
                    if (key == null) return;

                    if (project.Keys.All(k => k.Id != key.Id))
                        project.Keys.Add(key);

                    commitString = $"content(Localization): Added key {key.KeyName}";
                    break;
                }
                case EntryChangeType.KeyUpdated:
                {
                    LocLocalizationKey? key = project.Keys.Find(k => k.Id == change.EntryId);
                    if (key == null) return;

                    switch (change.EntrySubId)
                    {
                        case nameof(LocLocalizationKey.KeyName):
                            key.KeyName = change.ChangeData;
                            break;
                        case nameof(LocLocalizationKey.CategoryId):
                            if (Guid.TryParse(change.ChangeData, out Guid categoryId)) key.CategoryId = categoryId;
                            break;
                        case nameof(LocLocalizationKey.MaxLength):
                            if (int.TryParse(change.ChangeData, out int maxLength)) key.MaxLength = maxLength;
                            break;
                        case nameof(LocLocalizationKey.Description):
                            key.Description = change.ChangeData;
                            break;
                    }

                    commitString = $"content(Localization): Updated key {key.KeyName} ({change.EntrySubId})";
                    break;
                }
                case EntryChangeType.KeyRemoved:
                {
                    LocLocalizationKey? key  = project.Keys.Find(k => k.Id == change.EntryId);
                    string              name = key?.KeyName ?? change.EntryId.ToString();
                    if (key != null) project.Keys.Remove(key);

                    commitString = $"content(Localization): Removed key {name}";
                    break;
                }
                case EntryChangeType.CategoryAdded:
                {
                    LocCategory? category = JsonConvert.DeserializeObject<LocCategory>(change.ChangeData);
                    if (category == null) return;

                    if (project.Categories.All(c => c.Id != category.Id))
                        project.Categories.Add(category);

                    commitString = $"content(Localization): Added category {category.Name}";
                    break;
                }
                case EntryChangeType.CategoryUpdated:
                {
                    LocCategory? category = project.Categories.Find(c => c.Id == change.EntryId);
                    if (category == null) return;

                    switch (change.EntrySubId)
                    {
                        case nameof(LocCategory.Name):
                            category.Name = change.ChangeData;
                            break;
                        case nameof(LocCategory.Description):
                            category.Description = change.ChangeData;
                            break;
                        case nameof(LocCategory.ParentCategoryId):
                            category.ParentCategoryId = Guid.TryParse(change.ChangeData, out Guid parentId) ? parentId : null;
                            break;
                    }

                    commitString = $"content(Localization): Updated category {category.Name} ({change.EntrySubId})";
                    break;
                }
                case EntryChangeType.CategoryRemoved:
                {
                    LocCategory? category = project.Categories.Find(c => c.Id == change.EntryId);
                    string       name     = category?.Name ?? change.EntryId.ToString();
                    if (category != null) project.Categories.Remove(category);

                    commitString = $"content(Localization): Removed category {name}";
                    break;
                }
                case EntryChangeType.TranslationUpdated:
                {
                    LocLocalizationKey? key = project.Keys.Find(k => k.Id == change.EntryId);
                    if (key == null) return;

                    LocKeyTranslation? incoming = JsonConvert.DeserializeObject<LocKeyTranslation>(change.ChangeData);
                    if (incoming == null) return;

                    // Server owns the target language: the push is authorized against EntrySubId, so the
                    // payload must not be able to redirect the write to a different (e.g. source) language.
                    incoming.LanguageId = change.EntrySubId;

                    LocKeyTranslation? existing = key.Translations.Find(t => t.LanguageId == change.EntrySubId);
                    if (existing == null)
                    {
                        key.Translations.Add(incoming);
                    }
                    else
                    {
                        // Copy scalar fields only — suggestion changes are recorded separately,
                        // so the existing suggestions list must be preserved here.
                        existing.Text          = incoming.Text;
                        existing.Status        = incoming.Status;
                        existing.SourceChanged = incoming.SourceChanged;
                        existing.BaseTextHash  = incoming.BaseTextHash;
                        existing.UpdatedAt     = incoming.UpdatedAt;
                    }

                    commitString = $"content(Localization): Updated translation {change.EntrySubId} for key {key.KeyName}";
                    break;
                }
                case EntryChangeType.SuggestionAdded:
                {
                    LocLocalizationKey? key = project.Keys.Find(k => k.Id == change.EntryId);
                    if (key == null) return;
                    if (!project.Metadata.Languages.Contains(change.EntrySubId)) return; // ignore unknown languages

                    LocTranslationSuggestion? suggestion = JsonConvert.DeserializeObject<LocTranslationSuggestion>(change.ChangeData);
                    if (suggestion == null) return;

                    LocKeyTranslation translation = GetOrCreateTranslation(key, change.EntrySubId);
                    if (translation.Suggestions.All(s => s.Id != suggestion.Id))
                        translation.Suggestions.Add(suggestion);

                    commitString = $"content(Localization): Added suggestion for {change.EntrySubId} on key {key.KeyName}";
                    break;
                }
                case EntryChangeType.SuggestionVoted:
                {
                    LocLocalizationKey? key = project.Keys.Find(k => k.Id == change.EntryId);
                    if (key == null) return;

                    LocTranslationSuggestion? incoming = JsonConvert.DeserializeObject<LocTranslationSuggestion>(change.ChangeData);
                    if (incoming == null) return;

                    LocKeyTranslation? translation = key.Translations.Find(t => t.LanguageId == change.EntrySubId);
                    if (translation == null) return;

                    // A vote only updates an existing suggestion; it must never create one.
                    int idx                                    = translation.Suggestions.FindIndex(s => s.Id == incoming.Id);
                    if (idx >= 0) translation.Suggestions[idx] = incoming;

                    commitString = $"content(Localization): Voted on suggestion for {change.EntrySubId} on key {key.KeyName}";
                    break;
                }
                case EntryChangeType.SuggestionRemoved:
                {
                    LocLocalizationKey? key = project.Keys.Find(k => k.Id == change.EntryId);
                    if (key == null) return;
                    if (!Guid.TryParse(change.ChangeData, out Guid suggestionId)) return;

                    LocKeyTranslation? translation = key.Translations.Find(t => t.LanguageId == change.EntrySubId);
                    translation?.Suggestions.RemoveAll(s => s.Id == suggestionId);

                    commitString = $"content(Localization): Removed suggestion for {change.EntrySubId} on key {key.KeyName}";
                    break;
                }
                case EntryChangeType.FlagAdded:
                {
                    LocLocalizationKey? key = project.Keys.Find(k => k.Id == change.EntryId);
                    if (key == null) return;

                    LocKeyFlag? flag = JsonConvert.DeserializeObject<LocKeyFlag>(change.ChangeData);
                    if (flag == null) return;

                    if (key.Flags.All(f => f.Id != flag.Id)) key.Flags.Add(flag);

                    commitString = $"content(Localization): Added flag to key {key.KeyName}";
                    break;
                }
                case EntryChangeType.FlagRemoved:
                {
                    LocLocalizationKey? key = project.Keys.Find(k => k.Id == change.EntryId);
                    if (key == null) return;
                    if (!Guid.TryParse(change.ChangeData, out Guid flagId)) return;

                    key.Flags.RemoveAll(f => f.Id == flagId);

                    commitString = $"content(Localization): Removed flag from key {key.KeyName}";
                    break;
                }
                case EntryChangeType.TagAdded:
                {
                    LocLocalizationKey? key = project.Keys.Find(k => k.Id == change.EntryId);
                    if (key == null) return;

                    if (!string.IsNullOrEmpty(change.ChangeData) && !key.Tags.Contains(change.ChangeData))
                        key.Tags.Add(change.ChangeData);

                    commitString = $"content(Localization): Added tag {change.ChangeData} to key {key.KeyName}";
                    break;
                }
                case EntryChangeType.TagRemoved:
                {
                    LocLocalizationKey? key = project.Keys.Find(k => k.Id == change.EntryId);
                    if (key == null) return;

                    key.Tags.Remove(change.ChangeData);

                    commitString = $"content(Localization): Removed tag {change.ChangeData} from key {key.KeyName}";
                    break;
                }
                case EntryChangeType.VariableAdded:
                {
                    LocLocalizationKey? key = project.Keys.Find(k => k.Id == change.EntryId);
                    if (key == null) return;

                    LocKeyVariable? variable = JsonConvert.DeserializeObject<LocKeyVariable>(change.ChangeData);
                    if (variable == null) return;

                    if (key.Variables.All(v => v.Id != variable.Id)) key.Variables.Add(variable);

                    commitString = $"content(Localization): Added variable to key {key.KeyName}";
                    break;
                }
                case EntryChangeType.VariableUpdated:
                {
                    LocLocalizationKey? key = project.Keys.Find(k => k.Id == change.EntryId);
                    if (key == null) return;

                    LocKeyVariable? incoming = JsonConvert.DeserializeObject<LocKeyVariable>(change.ChangeData);
                    if (incoming == null) return;

                    int idx = key.Variables.FindIndex(v => v.Id == incoming.Id);
                    if (idx >= 0) key.Variables[idx] = incoming;
                    else key.Variables.Add(incoming);

                    commitString = $"content(Localization): Updated variable on key {key.KeyName}";
                    break;
                }
                case EntryChangeType.VariableRemoved:
                {
                    LocLocalizationKey? key = project.Keys.Find(k => k.Id == change.EntryId);
                    if (key == null) return;
                    if (!Guid.TryParse(change.ChangeData, out Guid variableId)) return;

                    key.Variables.RemoveAll(v => v.Id == variableId);

                    commitString = $"content(Localization): Removed variable from key {key.KeyName}";
                    break;
                }
                case EntryChangeType.EnumAdded:
                {
                    LocEnum? locEnum = JsonConvert.DeserializeObject<LocEnum>(change.ChangeData);
                    if (locEnum == null) return;

                    if (project.Enums.All(e => e.Id != locEnum.Id)) project.Enums.Add(locEnum);

                    commitString = $"content(Localization): Added enum {locEnum.Name}";
                    break;
                }
                case EntryChangeType.EnumUpdated:
                {
                    LocEnum? incoming = JsonConvert.DeserializeObject<LocEnum>(change.ChangeData);
                    if (incoming == null) return;

                    int idx = project.Enums.FindIndex(e => e.Id == incoming.Id);
                    if (idx >= 0) project.Enums[idx] = incoming;
                    else project.Enums.Add(incoming);

                    commitString = $"content(Localization): Updated enum {incoming.Name}";
                    break;
                }
                case EntryChangeType.EnumRemoved:
                {
                    LocEnum? locEnum = project.Enums.Find(e => e.Id == change.EntryId);
                    string   name    = locEnum?.Name ?? change.EntryId.ToString();
                    if (locEnum != null) project.Enums.Remove(locEnum);

                    commitString = $"content(Localization): Removed enum {name}";
                    break;
                }
                case EntryChangeType.MemberRemoved:
                {
                    LocProjectMember? member = project.ProjectMembers.Find(m => m.UserId == change.EntryId);
                    string            name   = member?.Username ?? change.EntryId.ToString();
                    if (member != null) project.ProjectMembers.Remove(member);

                    ReassignMemberReferences(project, change.EntryId, LocProjectMember.OfflineMember.UserId);

                    commitString = $"content(Localization): Removed member {name}";
                    break;
                }
            }
        }

        /// <summary>
        /// Reassigns every reference to a removed member (<paramref name="fromUserId"/>) — suggestion authors,
        /// votes, and flag creators across all keys — to <paramref name="toUserId"/> (the offline user). Keeps
        /// contributions attributed to a valid identity once the original member is gone; runs identically on
        /// the client and the bot so both derive the same state.
        /// </summary>
        private static void ReassignMemberReferences(LocProject project, Guid fromUserId, Guid toUserId)
        {
            foreach (LocLocalizationKey key in project.Keys)
            {
                foreach (LocKeyFlag flag in key.Flags)
                {
                    if (flag.CreatedBy == fromUserId) flag.CreatedBy = toUserId;
                }
                foreach (LocKeyTranslation translation in key.Translations)
                {
                    foreach (LocTranslationSuggestion suggestion in translation.Suggestions)
                    {
                        if (suggestion.AuthorId == fromUserId) suggestion.AuthorId = toUserId;
                        foreach (LocSuggestionVote vote in suggestion.Votes)
                        {
                            if (vote.UserId == fromUserId) vote.UserId = toUserId;
                        }
                    }
                }
            }
        }

        private static LocKeyTranslation GetOrCreateTranslation(LocLocalizationKey key, string languageId)
        {
            LocKeyTranslation? translation = key.Translations.Find(t => t.LanguageId == languageId);
            if (translation == null)
            {
                translation = new LocKeyTranslation { LanguageId = languageId };
                key.Translations.Add(translation);
            }
            return translation;
        }
    }
}