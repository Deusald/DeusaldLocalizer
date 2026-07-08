using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace DeusaldLocalizerCommon
{
    /// <summary>The kind of conflict a pending change has against the current repo state.</summary>
    public enum EntryConflictType
    {
        None,
        SourceChanged, // the source text drifted on the repo since the change was based on it
        DestChanged,   // the destination translation was edited on the repo concurrently
    }

    /// <summary>A single pending change that conflicts with the current (freshly-synced) project state.</summary>
    public class EntryChangeConflict
    {
        public EntryConflictType Type       { get; set; }
        public Guid              KeyId      { get; set; }
        public string           LanguageId { get; set; } = string.Empty;
        public string           KeyName    { get; set; } = string.Empty;
        public string           Message    { get; set; } = string.Empty;
    }

    /// <summary>
    /// Single source of truth for translation-conflict detection, shared by the client
    /// (re-applying its uncommitted changes after a sync, for UX) and the backend bot
    /// (re-validating a push against the freshly-pulled repo, as defense-in-depth).
    ///
    /// Only <see cref="EntryChangeType.TranslationUpdated"/> changes can conflict. Two cases,
    /// both compared against a "baseline" — the current repo state *before*
    /// any of these changes are applied:
    ///   • SourceChanged — the source text drifted (someone else edited the main language).
    ///   • DestChanged   — the destination translation was edited by someone else.
    /// </summary>
    [PublicAPI]
    public static class EntryChangeConflictService
    {
        /// <summary>
        /// Validates <paramref name="changes"/> (in order) against <paramref name="baseline"/>.
        /// Returns one conflict per offending change; empty list when everything is clean.
        /// </summary>
        public static List<EntryChangeConflict> Validate(LocProject baseline, IEnumerable<LocEntryChange> changes)
        {
            List<EntryChangeConflict> conflicts   = new List<EntryChangeConflict>();
            HashSet<string>           touched     = new HashSet<string>();
            string                    sourceLang  = baseline.Metadata.MainLanguageId;

            foreach (LocEntryChange change in changes)
            {
                if (change.Type != EntryChangeType.TranslationUpdated) continue;

                LocLocalizationKey? key = baseline.Keys.Find(k => k.Id == change.EntryId);
                if (key == null) continue; // key not on the baseline — nothing to conflict with

                string lang       = change.EntrySubId;
                string touchedKey = change.EntryId + ":" + lang;

                // ── Source drift — only meaningful for non-source translations ──────
                // (An edit to the source language legitimately changes the source itself,
                //  so its own PrevSourceHashData must not be checked against the source text.)
                if (lang != sourceLang && !string.IsNullOrEmpty(change.PrevSourceHashData))
                {
                    LocKeyTranslation? src                = key.Translations.Find(t => t.LanguageId == sourceLang);
                    string             baselineSourceHash = TextHashHelper.Compute(src?.Text ?? string.Empty);

                    if (change.PrevSourceHashData != baselineSourceHash)
                    {
                        conflicts.Add(new EntryChangeConflict
                        {
                            Type       = EntryConflictType.SourceChanged,
                            KeyId      = key.Id,
                            LanguageId = lang,
                            KeyName    = key.KeyName,
                            Message    = "Source text for '" + key.KeyName + "' changed on the server since your edit.",
                        });
                        touched.Add(touchedKey);
                        continue;
                    }
                }

                // ── Concurrent destination edit ────────────────────────────────────
                // Skip when this batch already modified the same (key, language) earlier:
                // later same-target changes chain off the user's own prior (already-checked)
                // change, not off external repo state.
                if (!touched.Contains(touchedKey))
                {
                    LocKeyTranslation? dest             = key.Translations.Find(t => t.LanguageId == lang);
                    string             baselineDestHash = TextHashHelper.Compute(dest?.Text ?? string.Empty);

                    if (change.PrevDestHashData != baselineDestHash)
                    {
                        conflicts.Add(new EntryChangeConflict
                        {
                            Type       = EntryConflictType.DestChanged,
                            KeyId      = key.Id,
                            LanguageId = lang,
                            KeyName    = key.KeyName,
                            Message    = "Translation '" + lang + "' for '" + key.KeyName + "' was changed on the server since your edit.",
                        });
                    }
                }

                touched.Add(touchedKey);
            }

            return conflicts;
        }
    }
}
