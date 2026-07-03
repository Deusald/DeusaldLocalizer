using System;
using System.Collections.Generic;

namespace DeusaldLocalizerCommon
{
    public class LocProjectMetadata
    {
        public Guid   Id          { get; set; } = Guid.NewGuid();
        public string Name        { get; set; } = string.Empty;
        public string Slug        { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ApiUrl      { get; set; } = string.Empty;

        /// <summary>BCP-47 code of the main/source language (e.g. "en-US").</summary>
        public string MainLanguageId { get; set; } = string.Empty;

        public Guid     SyncId        { get; set; } = Guid.NewGuid();
        public DateTime UpdatedAt     { get; set; } = DateTime.UtcNow;
        public int      FormatVersion { get; set; } = 1;

        /// <summary>BCP-47 codes of every language in this project (includes the main language).</summary>
        public List<string> Languages { get; set; } = new();
    }
}