using System;
using System.Collections.Generic;

namespace DeusaldLocalizerCommon
{
    public class LocTranslationSuggestion
    {
        public Guid                    Id        { get; set; } = Guid.NewGuid();
        public string                  Text      { get; set; } = string.Empty;
        public Guid                    AuthorId  { get; set; }
        public DateTime                CreatedAt { get; set; } = DateTime.UtcNow;
        public List<LocSuggestionVote> Votes     { get; set; } = new();

        /// <summary>SHA-256 of the main-language source text this suggestion was written against.</summary>
        public string SourceHash { get; set; } = string.Empty;

        /// <summary>Discussion comments about this suggestion.</summary>
        public List<LocComment> Comments { get; set; } = new();
    }
}