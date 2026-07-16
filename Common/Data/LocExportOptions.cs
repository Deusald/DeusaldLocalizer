using System;
using System.Collections.Generic;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// Filter options for <see cref="LocalizationExportService"/>.
    /// Flags and tags use a simple include/exclude model (no tri-state): every flag
    /// and tag is exported by default, and only the ones listed here are excluded.
    /// A key is dropped when it carries any excluded flag/tag, or when it has no
    /// flags/tags at all and the matching "no flags"/"no tags" bucket is excluded.
    /// Only languages listed in <see cref="Languages"/> are written as columns
    /// (empty = every project language). Ordering is always source-first, rest alphabetical.
    /// </summary>
    public class LocExportOptions
    {
        /// <summary>Flags whose keys are dropped from the export.</summary>
        public HashSet<FlagType> ExcludeFlags { get; } = new();

        /// <summary>Tags whose keys are dropped from the export.</summary>
        public HashSet<string> ExcludeTags { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>When false, keys that carry no flags at all are dropped.</summary>
        public bool IncludeNoFlags { get; set; } = true;

        /// <summary>When false, keys that carry no tags at all are dropped.</summary>
        public bool IncludeNoTags { get; set; } = true;

        /// <summary>When true, a "Tags" column listing each key's tags is written.</summary>
        public bool IncludeTagsColumn { get; set; }

        /// <summary>
        /// When set, only keys modified on or after this instant are exported. A key counts as
        /// modified when its own <see cref="LocLocalizationKey.UpdatedAt"/> or any of its
        /// translations' <see cref="LocKeyTranslation.UpdatedAt"/> is at or after the cutoff.
        /// </summary>
        public DateTime? ModifiedAfter { get; set; }

        public List<string> Languages { get; } = new();
    }
}
