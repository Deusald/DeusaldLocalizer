using System;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// Routing envelope for a comment change, serialized into <see cref="LocEntryChange.ChangeData"/>.
    /// The owning key is <see cref="LocEntryChange.EntryId"/>; this narrows the change to a specific
    /// comment list on that key (see <see cref="CommentScope"/>). <see cref="Comment"/> carries the payload
    /// for an add; <see cref="CommentId"/> identifies the target for a removal.
    /// </summary>
    public class LocCommentRef
    {
        public CommentScope Scope        { get; set; }
        public string       LanguageId   { get; set; } = string.Empty; // Translation & Suggestion scopes
        public Guid         SuggestionId { get; set; }                 // Suggestion scope
        public LocComment?  Comment      { get; set; }                 // CommentAdded payload
        public Guid         CommentId    { get; set; }                 // CommentRemoved target
    }
}
