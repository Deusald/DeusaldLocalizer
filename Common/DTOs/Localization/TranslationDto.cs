using System;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// The current approved (or in-progress) translation text for a key+language pair.
    /// TextHash is SHA-256 of the main language text at the time this was last confirmed,
    /// used to detect when the source has drifted and this needs attention.
    /// </summary>
    public class TranslationDto
    {
        public Guid   Id         { get; set; } = Guid.NewGuid();
        public Guid   KeyId      { get; set; }
        public string LanguageId { get; set; } = string.Empty;
        public string Text       { get; set; } = string.Empty;

        /// <summary>SHA-256 of the main-language text this translation was based on.</summary>
        public string BaseTextHash { get; set; } = string.Empty;

        public TranslationStatus Status { get; set; } = TranslationStatus.Draft;

        /// <summary>
        /// True when the main-language source text has changed since this translation
        /// was last confirmed — the translator needs to review and re-confirm.
        /// </summary>
        public bool NeedsAttention { get; set; } = false;

        public Guid     UpdatedBy { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}