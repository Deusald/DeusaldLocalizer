namespace DeusaldLocalizerCommon
{
    public enum EntryChangeType
    {
        MemberAdded,
        MemberUpdated,
        LanguageAdded,
        LanguageRemoved
    }

    public static class EntryChangeTypeExtensions
    {
        public static EntryChangeActionType ToActionType(this EntryChangeType entryChangeType)
        {
            return entryChangeType switch
            {
                EntryChangeType.MemberAdded     => EntryChangeActionType.Created,
                EntryChangeType.MemberUpdated   => EntryChangeActionType.Updated,
                EntryChangeType.LanguageAdded   => EntryChangeActionType.Created,
                EntryChangeType.LanguageRemoved => EntryChangeActionType.Deleted,
                _                               => EntryChangeActionType.Created
            };
        }
    }
}
