using System;
using System.Collections.Generic;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// A project-level enum type definition.
    /// Maps integer values to string names so variables of type EnumInt/EnumString
    /// can resolve to a typed CLR value that SmartFormat's conditional/select formatters
    /// can branch on.
    /// </summary>
    public class LocEnumDto
    {
        public Guid   Id          { get; set; } = Guid.NewGuid();
        public Guid   ProjectId   { get; set; }
        public string Name        { get; set; } = string.Empty; // e.g. "Gender"
        public string Description { get; set; } = string.Empty;

        /// <summary>Ordered entries. IntValue must be unique within this enum.</summary>
        public List<LocEnumEntryDto> Entries { get; set; } = new();
    }

    /// <summary>One entry inside a LocEnumDto (e.g. IntValue=0, StringValue="Male").</summary>
    public class LocEnumEntryDto
    {
        public int    IntValue    { get; set; }
        public string StringValue { get; set; } = string.Empty;
    }
}