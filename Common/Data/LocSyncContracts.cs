using System;
using System.Collections.Generic;

namespace DeusaldLocalizerCommon
{
    // ── Sync (pull) ────────────────────────────────────────────────────────────

    public class SyncRequest
    {
        /// <summary>The <c>SyncId</c> the client currently holds (the repo version it last saw).</summary>
        public Guid SyncId { get; set; }
    }

    public enum SyncStatus
    {
        UpToDate,   // client already has the latest commit
        Updated,    // client is behind; ChangedFiles/DeletedFiles carry the delta
        FullResync, // client's SyncId is unknown — ChangedFiles carries every project file
    }

    public class SyncFile
    {
        /// <summary>Repo-relative path with forward slashes (e.g. <c>Keys/{guid}.json</c>).</summary>
        public string Path    { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public class SyncResponse
    {
        public SyncStatus     Status       { get; set; }
        public Guid           NewSyncId    { get; set; }
        public List<SyncFile> ChangedFiles { get; set; } = new List<SyncFile>();
        public List<string>   DeletedFiles { get; set; } = new List<string>();
    }

    // ── Push ───────────────────────────────────────────────────────────────────

    public class PushRequest
    {
        /// <summary>The <c>SyncId</c> the client currently holds (for logging / diagnostics).</summary>
        public Guid                 SyncId  { get; set; }
        public List<LocEntryChange> Changes { get; set; } = new List<LocEntryChange>();
    }

    public enum PushStatus
    {
        Success,  // changes committed and pushed; NewSyncId is the new repo version
        Conflict, // one or more changes conflict with the current repo (Conflicts populated)
        Failed,   // the repo changed during processing — nothing applied, retry after a sync
    }

    public class PushResponse
    {
        public PushStatus                Status    { get; set; }
        public Guid                      NewSyncId { get; set; }
        public List<EntryChangeConflict> Conflicts { get; set; } = new List<EntryChangeConflict>();
        public string?                   Message   { get; set; }
    }
}
