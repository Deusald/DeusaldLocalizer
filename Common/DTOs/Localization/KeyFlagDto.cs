using System;

namespace DeusaldLocalizerCommon
{
    /// <summary>A structured workflow flag on a localization key.</summary>
    public class KeyFlagDto
    {
        public Guid     Id        { get; set; } = Guid.NewGuid();
        public Guid     KeyId     { get; set; }
        public FlagType Type      { get; set; }
        public string   Note      { get; set; } = string.Empty;
        public Guid     CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}