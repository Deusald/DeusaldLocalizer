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
    }
}