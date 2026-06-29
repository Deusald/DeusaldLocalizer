using System;

namespace DeusaldLocalizerCommon
{
    /// <summary>A +1 or -1 vote on a suggestion from a Voter.</summary>
    public class SuggestionVoteDto
    {
        public Guid     Id           { get; set; } = Guid.NewGuid();
        public Guid     SuggestionId { get; set; }
        public Guid     UserId       { get; set; }
        public string   UserName     { get; set; } = string.Empty; // Denormalised
        public int      Value        { get; set; } = 1;            // +1 or -1
        public DateTime CastAt       { get; set; } = DateTime.UtcNow;
    }
}