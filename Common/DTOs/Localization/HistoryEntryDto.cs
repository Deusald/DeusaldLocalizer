using System;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// Append-only audit log entry. Never updated or deleted.
    /// Old/NewValue are JSON snapshots of the changed entity.
    /// </summary>
    public class HistoryEntryDto
    {
        public Guid              Id             { get; set; } = Guid.NewGuid();
        public string            EntityPartName { get; set; } = string.Empty;
        public Guid              EntityPartId   { get; set; }
        public HistoryChangeType ChangeType     { get; set; }
        public string            OldValue       { get; set; } = string.Empty; // JSON snapshot
        public string            NewValue       { get; set; } = string.Empty; // JSON snapshot
        public Guid              UserId         { get; set; }
        public string            UserName       { get; set; } = string.Empty; // Denormalised
        public DateTime          Timestamp      { get; set; } = DateTime.UtcNow;
    }
}