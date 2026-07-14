using System.Collections.Generic;
using JetBrains.Annotations;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// Groups several <see cref="LocEntryChange"/>s that a single user action spawned into an atomic chain and
    /// verifies that integrity later. Each change in a chain stores the content hash of its immediate neighbours
    /// (<see cref="LocEntryChange.RequiredBefore"/> / <see cref="LocEntryChange.RequiredNext"/>); a standalone
    /// change leaves both empty.
    ///
    /// Shared by the client (linking a group as it is recorded, validating the persisted queue on load) and the
    /// backend bot (validating a pushed batch, as defense-in-depth). When validation fails the pending changes
    /// were tampered with, so the caller drops the whole queue / rejects the push rather than partially applying.
    /// </summary>
    [PublicAPI]
    public static class EntryChangeChainService
    {
        /// <summary>
        /// Hex SHA-256 over a change's <em>content</em> fields only — deliberately excluding
        /// <see cref="LocEntryChange.RequiredBefore"/> / <see cref="LocEntryChange.RequiredNext"/>, so the link
        /// fields can reference a neighbour's hash without a circular dependency. Variable-length string fields
        /// are pre-hashed to a fixed-length hex (or empty) before joining, so field boundaries are unambiguous.
        /// </summary>
        public static string ComputeContentHash(LocEntryChange change)
        {
            string canonical = string.Join("|",
                ((int)change.Type).ToString(),
                change.EntryId.ToString(),
                TextHashHelper.Compute(change.EntrySubId),
                TextHashHelper.Compute(change.ChangeData),
                TextHashHelper.Compute(change.PrevSourceHashData),
                TextHashHelper.Compute(change.PrevDestHashData));

            return TextHashHelper.Compute(canonical);
        }

        /// <summary>
        /// Wires <paramref name="group"/> (in application order) into an atomic chain: every change points at
        /// its previous and next neighbour by content hash. A group of fewer than two just has its link fields
        /// cleared (a single change is standalone). Mutates the change objects in place — safe to call after they
        /// were added to the queue, since the queue holds the same references.
        /// </summary>
        public static void LinkChain(IReadOnlyList<LocEntryChange> group)
        {
            // Always clear first so re-linking a reused change (e.g. a staged undo inverse) leaves no stale links.
            foreach (LocEntryChange change in group)
            {
                change.RequiredBefore = string.Empty;
                change.RequiredNext   = string.Empty;
            }
            if (group.Count < 2) return;

            string[] hashes = new string[group.Count];
            for (int x = 0; x < group.Count; ++x)
                hashes[x] = ComputeContentHash(group[x]);

            for (int x = 0; x < group.Count; ++x)
            {
                if (x > 0)               group[x].RequiredBefore = hashes[x - 1];
                if (x < group.Count - 1) group[x].RequiredNext   = hashes[x + 1];
            }
        }

        /// <summary>
        /// Restores the chain invariant after changes were removed from the middle of a queue (e.g. pruning a
        /// redundant edit during sync). Walks <paramref name="changes"/>, keeps every run whose neighbours are
        /// still mutually linked as an intact group, and normalises the links of every other change to standalone
        /// — clearing any dangling reference to a removed neighbour. Idempotent: a queue with intact chains is
        /// left unchanged. After this call <see cref="ValidateChain"/> is guaranteed to pass.
        /// </summary>
        public static void RepairChainLinks(IReadOnlyList<LocEntryChange> changes)
        {
            int i = 0;
            while (i < changes.Count)
            {
                int j = i;
                while (j + 1 < changes.Count && IsLinkedPair(changes[j], changes[j + 1])) ++j;

                List<LocEntryChange> run = new List<LocEntryChange>();
                for (int k = i; k <= j; ++k)
                    run.Add(changes[k]);
                LinkChain(run);   // re-links a genuine run; clears the links of a singleton

                i = j + 1;
            }
        }

        /// <summary>True when two adjacent changes still reference each other by content hash (an intact link).</summary>
        private static bool IsLinkedPair(LocEntryChange a, LocEntryChange b) =>
            !string.IsNullOrEmpty(a.RequiredNext)
         && !string.IsNullOrEmpty(b.RequiredBefore)
         && a.RequiredNext   == ComputeContentHash(b)
         && b.RequiredBefore == ComputeContentHash(a);

        /// <summary>
        /// Verifies every atomic link in <paramref name="changes"/> (in order): each change that declares a
        /// required neighbour must actually have that exact neighbour (by content hash) in the expected position.
        /// Returns false as soon as any link is broken — the queue was tampered with and should be discarded
        /// wholesale. An empty queue, or one of only standalone changes, is trivially intact.
        /// </summary>
        public static bool ValidateChain(IReadOnlyList<LocEntryChange> changes)
        {
            for (int x = 0; x < changes.Count; ++x)
            {
                LocEntryChange change = changes[x];

                if (!string.IsNullOrEmpty(change.RequiredBefore))
                {
                    if (x == 0) return false;
                    if (ComputeContentHash(changes[x - 1]) != change.RequiredBefore) return false;
                }

                if (!string.IsNullOrEmpty(change.RequiredNext))
                {
                    if (x == changes.Count - 1) return false;
                    if (ComputeContentHash(changes[x + 1]) != change.RequiredNext) return false;
                }
            }

            return true;
        }
    }
}
