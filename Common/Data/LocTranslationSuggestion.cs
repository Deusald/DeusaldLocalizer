using System;
using System.Collections.Generic;

namespace DeusaldLocalizerCommon
{
    public class LocTranslationSuggestion
    {
        public Guid                    Id        { get; set; } = Guid.NewGuid();
        public string                  Text      { get; set; } = string.Empty;
        public Guid                    AuthorId  { get; set; }
        public SuggestionStatus        Status    { get; set; } = SuggestionStatus.Pending;
        public DateTime                CreatedAt { get; set; } = DateTime.UtcNow;
        public List<LocSuggestionVote> Votes     { get; set; } = new();
    }
}