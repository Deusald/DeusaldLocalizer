using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Newtonsoft.Json;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// Server-authoritative sanitation for changes whose payload carries identity or vote tallies a
    /// crafted client could forge. The bot runs this on every pushed change (against the freshly-pulled
    /// project) before it is applied, so a member can only ever speak for themselves:
    ///   • SuggestionAdded — the author is forced to whoever is pushing, and a brand-new suggestion
    ///                       carries no votes (any votes bundled in the payload are stripped).
    ///   • SuggestionVoted — the suggestion is rebuilt from the authoritative server copy with ONLY the
    ///                       acting member's own vote applied (clamped to ±1). Text, author, creation
    ///                       time and everyone else's votes are taken from the server, never the payload,
    ///                       so a "vote" can neither rewrite a suggestion nor stuff the ballot.
    ///   • CommentAdded    — the comment's author is forced to whoever is pushing.
    /// The honest client always produces payloads that survive this untouched; a forged one is neutralized
    /// rather than rejected, so a legitimate batch is never blocked.
    /// </summary>
    [PublicAPI]
    public static class EntryChangeAuthorityService
    {
        /// <summary>Rewrites <paramref name="change"/>'s payload in place so it can only reflect <paramref name="member"/>'s own action.</summary>
        public static void Normalize(LocProject project, LocProjectMember member, LocEntryChange change)
        {
            switch (change.Type)
            {
                case EntryChangeType.SuggestionAdded:
                {
                    LocTranslationSuggestion? incoming = JsonConvert.DeserializeObject<LocTranslationSuggestion>(change.ChangeData);
                    if (incoming == null) return;

                    incoming.AuthorId = member.UserId;
                    incoming.Votes    = new List<LocSuggestionVote>();
                    change.ChangeData = JsonConvert.SerializeObject(incoming);
                    break;
                }
                case EntryChangeType.SuggestionVoted:
                {
                    LocTranslationSuggestion? incoming = JsonConvert.DeserializeObject<LocTranslationSuggestion>(change.ChangeData);
                    if (incoming == null) return;

                    LocLocalizationKey?       key      = project.Keys.Find(k => k.Id == change.EntryId);
                    LocKeyTranslation?        dest     = key?.Translations.Find(t => t.LanguageId == change.EntrySubId);
                    LocTranslationSuggestion? existing = dest?.Suggestions.Find(s => s.Id == incoming.Id);
                    if (existing == null) return; // a vote never creates or resurrects a suggestion

                    LocTranslationSuggestion rebuilt = new LocTranslationSuggestion
                    {
                        Id         = existing.Id,
                        Text       = existing.Text,
                        AuthorId   = existing.AuthorId,
                        CreatedAt  = existing.CreatedAt,
                        SourceHash = existing.SourceHash,
                        Votes      = existing.Votes.Where(v => v.UserId != member.UserId).ToList(),
                    };

                    LocSuggestionVote? mine = incoming.Votes.Find(v => v.UserId == member.UserId);
                    if (mine != null)
                        rebuilt.Votes.Add(new LocSuggestionVote
                        {
                            Id     = mine.Id,
                            UserId = member.UserId,
                            Value  = mine.Value < 0 ? -1 : 1,
                            CastAt = mine.CastAt,
                        });

                    change.ChangeData = JsonConvert.SerializeObject(rebuilt);
                    break;
                }
                case EntryChangeType.CommentAdded:
                {
                    LocCommentRef? commentRef = JsonConvert.DeserializeObject<LocCommentRef>(change.ChangeData);
                    if (commentRef?.Comment == null) return;

                    // The author is whoever is pushing — a crafted payload cannot attribute a comment to
                    // someone else. Text, id and timestamp are the member's own to set.
                    commentRef.Comment.AuthorId = member.UserId;
                    change.ChangeData           = JsonConvert.SerializeObject(commentRef);
                    break;
                }
            }
        }
    }
}
