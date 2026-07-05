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

                    commitString = $"Add member {member.Username}";
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
                    }

                    commitString = $"Update member {existing.Username} ({change.EntrySubId})";
                    break;
                }
                case EntryChangeType.LanguageAdded:
                {
                    string code = change.ChangeData;
                    if (!string.IsNullOrEmpty(code) && !project.Metadata.Languages.Contains(code))
                        project.Metadata.Languages.Add(code);

                    commitString = $"Add language {code}";
                    break;
                }
                case EntryChangeType.LanguageRemoved:
                {
                    string code = change.ChangeData;
                    project.Metadata.Languages.Remove(code);

                    commitString = $"Remove language {code}";
                    break;
                }
                case EntryChangeType.KeyAdded:
                {
                    LocLocalizationKey? key = JsonConvert.DeserializeObject<LocLocalizationKey>(change.ChangeData);
                    if (key == null) return;

                    if (project.Keys.All(k => k.Id != key.Id))
                        project.Keys.Add(key);

                    commitString = $"Add key {key.KeyName}";
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
                    }

                    commitString = $"Update key {key.KeyName} ({change.EntrySubId})";
                    break;
                }
                case EntryChangeType.CategoryAdded:
                {
                    LocCategory? category = JsonConvert.DeserializeObject<LocCategory>(change.ChangeData);
                    if (category == null) return;

                    if (project.Categories.All(c => c.Id != category.Id))
                        project.Categories.Add(category);

                    commitString = $"Add category {category.Name}";
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
                            category.ParentCategoryId = Guid.TryParse(change.ChangeData, out Guid parentId) ? parentId : (Guid?)null;
                            break;
                    }

                    commitString = $"Update category {category.Name} ({change.EntrySubId})";
                    break;
                }
                case EntryChangeType.CategoryRemoved:
                {
                    LocCategory? category = project.Categories.Find(c => c.Id == change.EntryId);
                    string       name     = category?.Name ?? change.EntryId.ToString();
                    if (category != null) project.Categories.Remove(category);

                    commitString = $"Remove category {name}";
                    break;
                }
            }
        }
    }
}
