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
            }
        }
    }
}
