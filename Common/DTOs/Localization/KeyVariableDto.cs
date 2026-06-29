using System;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// Declares a SmartFormat variable for a key.
    /// SampleValue is used in the live preview so translators see realistic output.
    /// </summary>
    public class KeyVariableDto
    {
        public Guid   Id          { get; set; } = Guid.NewGuid();
        public Guid   KeyId       { get; set; }
        public string Name        { get; set; } = string.Empty; // e.g. "playerName"
        public string SampleValue { get; set; } = string.Empty; // e.g. "Alice"
        public string Type        { get; set; } = "string";     // string | number | date
    }
}