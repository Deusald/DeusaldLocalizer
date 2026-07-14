using System;

namespace DeusaldLocalizerCommon
{
    public class LocEntryChange
    {
        public EntryChangeType Type               { get; set; }
        public Guid            EntryId            { get; set; }
        public string          EntrySubId         { get; set; } = string.Empty;
        public string          ChangeData         { get; set; } = string.Empty;
        public string          PrevSourceHashData { get; set; } = string.Empty;
        public string          PrevDestHashData   { get; set; } = string.Empty;

        // ── Atomic-group linkage (see EntryChangeChainService) ────────────────────
        // When one user action spawns several changes that must be applied together (e.g. editing the source
        // text also flips the "source changed" flag on every other-language translation), the changes form a
        // chain: each one carries the content hash of its immediate neighbours. A standalone change leaves both
        // empty. On load/push the chain is verified; a broken link means the pending changes were tampered with,
        // so the whole queue is dropped rather than partially applied.

        /// <summary>Content hash of the change that must be applied immediately before this one, or empty.</summary>
        public string RequiredBefore { get; set; } = string.Empty;

        /// <summary>Content hash of the change that must be applied immediately after this one, or empty.</summary>
        public string RequiredNext { get; set; } = string.Empty;
    }
}