using System;

namespace DeusaldLocalizerCommon
{
    public class LocSuggestionVote
    {
        public Guid     Id     { get; set; } = Guid.NewGuid();
        public Guid     UserId { get; set; }
        public int      Value  { get; set; } = 1; // +1 or -1
        public DateTime CastAt { get; set; } = DateTime.UtcNow;
    }
}