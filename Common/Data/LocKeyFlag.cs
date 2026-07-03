using System;

namespace DeusaldLocalizerCommon
{
    public class LocKeyFlag
    {
        public Guid     Id        { get; set; } = Guid.NewGuid();
        public FlagType Type      { get; set; }
        public string   Note      { get; set; } = string.Empty;
        public Guid     CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}