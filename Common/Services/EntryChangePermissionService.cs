using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Newtonsoft.Json;

namespace DeusaldLocalizerCommon
{
    /// <summary>A single change the acting member is not authorized to make.</summary>
    public class EntryChangePermissionError
    {
        public EntryChangeType Type       { get; set; }
        public Guid            EntryId    { get; set; }
        public string          EntrySubId { get; set; } = string.Empty;
        public string          Message    { get; set; } = string.Empty;
    }

    /// <summary>
    /// Single source of truth for authorizing a member's changes, mirroring the role gating the
    /// App enforces in its UI (admin-only management, per-language reviewer rights, open suggesting).
    /// The client hides disallowed actions; the backend re-checks every pushed change here so a
    /// crafted request cannot bypass the UI. Banned members are already rejected at authentication,
    /// but are treated as unauthorized here too for defense-in-depth.
    ///
    /// Tiers:
    ///   • Admin           — structural/management actions (adding/editing/deleting members, languages,
    ///                        keys, categories, tags, variables, enums) and editing the source/main language.
    ///   • Reviewer        — confirm/remove translations for a language the member reviews; manage
    ///                        key-level flags if the member reviews any language.
    ///   • Any member      — propose and vote on suggestions; leave comments (and delete their own).
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

            // Track comments this member adds earlier in the batch: a member may delete a comment they only
            // just posted (add-then-delete before pushing), which the pristine-project author check below
            // cannot see because that comment is not applied to the project until later in the push.
            HashSet<Guid> addedThisBatch = new HashSet<Guid>();

            foreach (LocEntryChange change in changes)
            {
                if (change.Type == EntryChangeType.CommentAdded)
                {
                    LocCommentRef? added = JsonConvert.DeserializeObject<LocCommentRef>(change.ChangeData);
                    if (added?.Comment != null) addedThisBatch.Add(added.Comment.Id);
                }

                if (IsAllowed(project, member, change)) continue;

                // A member's own just-added comment is theirs to delete even before it lands on the server.
                if (change.Type == EntryChangeType.CommentRemoved && !member.IsBanned)
                {
                    LocCommentRef? removed = JsonConvert.DeserializeObject<LocCommentRef>(change.ChangeData);
                    if (removed != null && addedThisBatch.Contains(removed.CommentId)) continue;
                }

                errors.Add(new EntryChangePermissionError
                {
                    Type       = change.Type,
                    EntryId    = change.EntryId,
                    EntrySubId = change.EntrySubId,
                    Message = "You do not have permission to perform '" + change.Type + "' (requires "
                            + RequiredRole(project, change) + ").",
                });
            }

            return errors;
        }

        /// <summary>True when <paramref name="member"/> may apply <paramref name="change"/> to <paramref name="project"/>.</summary>
        public static bool IsAllowed(LocProject project, LocProjectMember member, LocEntryChange change)
        {
            if (member.IsBanned) return false;
            if (member.IsAdmin) return true; // admins may do anything

            switch (change.Type)
            {
                // Any (non-banned) member may propose alternatives, vote on them, and leave comments.
                case EntryChangeType.SuggestionAdded:
                case EntryChangeType.SuggestionVoted:
                case EntryChangeType.CommentAdded:
                    return true;

                // A comment may only be deleted by its own author (admins already returned true above,
                // and drive the bulk "clear old comments" maintenance tool).
                case EntryChangeType.CommentRemoved:
                    return IsOwnComment(project, member, change);

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

                // A member may rotate their own access token (and clear their own reset flag as part of
                // that first-sign-in rotation), but touch nothing else about members.
                case EntryChangeType.MemberUpdated:
                    return change.EntryId == member.UserId
                        && (change.EntrySubId == nameof(LocProjectMember.HashedAccessToken)
                         || change.EntrySubId == nameof(LocProjectMember.MustResetAccessToken));

                // Deleting a key or a member is a destructive structural action — admin-only. (Falls under the
                // default below too, but called out explicitly so the gating is unmistakable.)
                case EntryChangeType.KeyRemoved:
                case EntryChangeType.MemberRemoved:
                    return false;

                // Everything else (members, languages, keys, categories, tags, variables, enums) is admin-only.
                default:
                    return false;
            }
        }

        /// <summary>True when <paramref name="change"/> deletes a comment authored by <paramref name="member"/>.</summary>
        private static bool IsOwnComment(LocProject project, LocProjectMember member, LocEntryChange change)
        {
            LocCommentRef? commentRef = JsonConvert.DeserializeObject<LocCommentRef>(change.ChangeData);
            if (commentRef == null) return false;

            LocLocalizationKey? key = project.Keys.Find(k => k.Id == change.EntryId);
            if (key == null) return false;

            LocComment? comment = CommentLocator.Find(key, commentRef, commentRef.CommentId);
            return comment != null && comment.AuthorId == member.UserId;
        }

        private static string RequiredRole(LocProject project, LocEntryChange change)
        {
            switch (change.Type)
            {
                case EntryChangeType.SuggestionAdded:
                case EntryChangeType.SuggestionVoted:
                case EntryChangeType.CommentAdded:
                    return "any member";
                case EntryChangeType.CommentRemoved:
                    return "the comment's author or an admin";
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