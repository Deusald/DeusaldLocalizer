namespace DeusaldLocalizerCommon
{
    public enum EntryChangeType
    {
        MemberAdded,
        MemberUpdated,
        LanguageAdded,
        LanguageRemoved,
        KeyAdded,
        KeyUpdated,
        CategoryAdded,
        CategoryUpdated,
        CategoryRemoved
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
                EntryChangeType.KeyAdded        => EntryChangeActionType.Created,
                EntryChangeType.KeyUpdated      => EntryChangeActionType.Updated,
                EntryChangeType.CategoryAdded   => EntryChangeActionType.Created,
                EntryChangeType.CategoryUpdated => EntryChangeActionType.Updated,
                EntryChangeType.CategoryRemoved => EntryChangeActionType.Deleted,
                _                               => EntryChangeActionType.Created
            };
        }
    }
}
