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

        /// <summary>
        /// Offline-only opt-in: when true, edits are staged in the uncommitted-changes queue (exactly like
        /// an online project) instead of being written straight to the key files, and must be applied
        /// explicitly. Ignored while online — online projects always stage their changes. Persisted so the
        /// mode survives reopening the project.
        /// </summary>
        public bool UncommittedMode { get; set; }

        /// <summary>BCP-47 code of the main/source language (e.g. "en-US").</summary>
        public string MainLanguageId { get; set; } = string.Empty;

        public Guid     SyncId        { get; set; } = Guid.NewGuid();
        public DateTime UpdatedAt     { get; set; } = DateTime.UtcNow;
        public int      FormatVersion { get; set; } = 1;

        /// <summary>BCP-47 codes of every language in this project (includes the main language).</summary>
        public List<string> Languages { get; } = new();

        public bool IsOnline => !string.IsNullOrEmpty(ApiUrl);

        /// <summary>
        /// True when edits are staged in the uncommitted-changes queue rather than written directly to the
        /// key files: always online, and offline whenever <see cref="UncommittedMode"/> is enabled.
        /// </summary>
        public bool UsesUncommittedChanges => IsOnline || UncommittedMode;
    }
}