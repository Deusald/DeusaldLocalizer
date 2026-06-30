using System;
using System.Collections.Generic;

namespace DeusaldLocalizerCommon
{
    /// <summary>A proposed translation text submitted by a Contributor for review.</summary>
    public class SuggestionDto
    {
        public Guid Id    { get; set; } = Guid.NewGuid();
        public Guid KeyId { get; set; }

        /// <summary>BCP-47 language code (e.g. "de-DE").</summary>
        public string LanguageId { get; set; } = string.Empty;

        public string           Text       { get; set; } = string.Empty;
        public Guid             AuthorId   { get; set; }
        public string           AuthorName { get; set; } = string.Empty; // Denormalised
        public SuggestionStatus Status     { get; set; } = SuggestionStatus.Pending;
        public string           Note       { get; set; } = string.Empty;
        public DateTime         CreatedAt  { get; set; } = DateTime.UtcNow;

        /// <summary>Votes cast on this suggestion.</summary>
        public List<SuggestionVoteDto> Votes { get; set; } = new();
    }
}