using System;
using System.Collections.Generic;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// A single translatable string identified by a stable GUID.
    /// KeyName is the human-readable identifier used in code (e.g. "ui.button.save").
    /// </summary>
    public class LocalizationKeyDto
    {
        public Guid     Id          { get; set; } = Guid.NewGuid();
        public Guid     ProjectId   { get; set; }
        public Guid     CategoryId  { get; set; }
        public string   KeyName     { get; set; } = string.Empty;
        public string   Description { get; set; } = string.Empty;
        public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt   { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Maximum character length for all translations of this key.
        /// 0 means no limit enforced.
        /// </summary>
        public int MaxLength { get; set; } = 0;

        /// <summary>Free-form tags for search/filter (e.g. ["ui", "button"]).</summary>
        public List<string> Tags { get; set; } = new();

        /// <summary>Structured workflow flags.</summary>
        public List<KeyFlagDto> Flags { get; set; } = new();

        /// <summary>Declared SmartFormat variables so translators can preview the result.</summary>
        public List<KeyVariableDto> Variables { get; set; } = new();

        /// <summary>All translations for this key across every language.</summary>
        public List<TranslationDto> Translations { get; set; } = new();

        /// <summary>Full audit history for this key.</summary>
        public List<HistoryEntryDto> History { get; set; } = new();
    }
}