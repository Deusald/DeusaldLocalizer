using System;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// A free-form discussion note left by a member. Comments attach to three things — a whole
    /// localization key, a single key+language translation, or a translation suggestion — and are
    /// immutable once posted (edit = delete + re-add), mirroring the suggestion model.
    /// </summary>
    public class LocComment
    {
        public Guid     Id        { get; set; } = Guid.NewGuid();
        public string   Text      { get; set; } = string.Empty;
        public Guid     AuthorId  { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
