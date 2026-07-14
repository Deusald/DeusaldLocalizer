using System;
using System.Collections.Generic;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// The current approved (or in-progress) translation text for a key+language pair.
    /// TextHash is SHA-256 of the main language text at the time this was last confirmed,
    /// used to detect when the source has drifted and this needs attention.
    /// </summary>
    public class LocKeyTranslation
    {
        /// <summary>BCP-47 language code (e.g. "de-DE").</summary>
        public string LanguageId { get; set; } = string.Empty;

        /// <summary>SHA-256 of the main-language text this translation was based on.</summary>
        public string BaseTextHash { get; set; } = string.Empty;

        public string                         Text          { get; set; } = string.Empty;
        public TranslationStatus              Status        { get; set; } = TranslationStatus.Untranslated;
        public bool                           SourceChanged { get; set; }
        public DateTime                       UpdatedAt     { get; set; } = DateTime.UtcNow;
        public List<LocTranslationSuggestion> Suggestions   { get; set; } = new();

        /// <summary>Discussion comments about this key+language translation.</summary>
        public List<LocComment> Comments { get; set; } = new();
    }
}