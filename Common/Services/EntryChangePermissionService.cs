using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace DeusaldLocalizerCommon
{
    /// <summary>A single change the acting member is not authorized to make.</summary>
    public class EntryChangePermissionError
    {
        public EntryChangeType Type       { get; set; }
        public Guid            EntryId    { get; set; }
        public string         EntrySubId { get; set; } = string.Empty;
        public string         Message    { get; set; } = string.Empty;
    }

    /// <summary>
    /// Single source of truth for authorizing a member's changes, mirroring the role gating the
    /// App enforces in its UI (admin-only management, per-language reviewer rights, open suggesting).
    /// The client hides disallowed actions; the backend re-checks every pushed change here so a
    /// crafted request cannot bypass the UI. Banned members are already rejected at authentication,
    /// but are treated as unauthorized here too for defense-in-depth.
    ///
    /// Tiers:
    ///   • Admin           — structural/management actions (members, languages, keys, categories,
    ///                        tags, variables, enums) and editing the source/main language.
    ///   • Reviewer        — confirm/remove translations for a language the member reviews; manage
    ///                        key-level flags if the member reviews any language.
    ///   • Any member      — propose and vote on suggestions.
    /// </summary>
    [PublicAPI]
    public static class EntryChangePermissionService
    {
        /// <summary>
        /// Validates <paramref name="changes"/> against <paramref name="member"/>'s role in
        /// <paramref name="project"/>. Returns one error per disallowed change; empty when all are allowed.
        /// </summary>
        public static List<EntryChangePermissionError> Validate(
            LocProject project, LocProjectMember member, IEnumerable<LocEntryChange> changes)
        {
            List<EntryChangePermissionError> errors = new List<EntryChangePermissionError>();

            foreach (LocEntryChange change in changes)
            {
                if (IsAllowed(project, member, change)) continue;

                errors.Add(new EntryChangePermissionError
                {
                    Type       = change.Type,
                    EntryId    = change.EntryId,
                    EntrySubId = change.EntrySubId,
                    Message    = "You do not have permission to perform '" + change.Type + "' (requires "
                               + RequiredRole(project, change) + ").",
                });
            }

            return errors;
        }

        /// <summary>True when <paramref name="member"/> may apply <paramref name="change"/> to <paramref name="project"/>.</summary>
        public static bool IsAllowed(LocProject project, LocProjectMember member, LocEntryChange change)
        {
            if (member.IsBanned) return false;
            if (member.IsAdmin)  return true; // admins may do anything

            switch (change.Type)
            {
                // Any (non-banned) member may propose alternatives and vote on them.
                case EntryChangeType.SuggestionAdded:
                case EntryChangeType.SuggestionVoted:
                    return true;

                // Confirming a translation is reviewer-only. Editing the source/main language is
                // admin-only — for a non-admin it is never allowed, whatever their review languages.
                case EntryChangeType.TranslationUpdated:
                    return change.EntrySubId != project.Metadata.MainLanguageId
                        && member.ReviewLanguagePermissions.Contains(change.EntrySubId);

                // Accepting/rejecting a suggestion is reviewer-only for that language.
                case EntryChangeType.SuggestionRemoved:
                    return member.ReviewLanguagePermissions.Contains(change.EntrySubId);

                // Flags are key-level (not per-language): any reviewer, or an admin, may manage them.
                case EntryChangeType.FlagAdded:
                case EntryChangeType.FlagRemoved:
                    return member.ReviewLanguagePermissions.Count > 0;

                // A member may rotate their own access token, but touch nothing else about members.
                case EntryChangeType.MemberUpdated:
                    return change.EntryId == member.UserId
                        && change.EntrySubId == nameof(LocProjectMember.HashedAccessToken);

                // Everything else (members, languages, keys, categories, tags, variables, enums) is admin-only.
                default:
                    return false;
            }
        }

        private static string RequiredRole(LocProject project, LocEntryChange change)
        {
            switch (change.Type)
            {
                case EntryChangeType.SuggestionAdded:
                case EntryChangeType.SuggestionVoted:
                    return "any member";
                case EntryChangeType.TranslationUpdated:
                    return change.EntrySubId == project.Metadata.MainLanguageId
                        ? "admin"
                        : "reviewer of '" + change.EntrySubId + "'";
                case EntryChangeType.SuggestionRemoved:
                    return "reviewer of '" + change.EntrySubId + "'";
                case EntryChangeType.FlagAdded:
                case EntryChangeType.FlagRemoved:
                    return "reviewer";
                default:
                    return "admin";
            }
        }
    }
}
