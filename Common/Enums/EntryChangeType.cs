namespace DeusaldLocalizerCommon
{
    public enum EntryChangeType
    {
        MemberAdded,
        MemberUpdated
    }

    public static class EntryChangeTypeExtensions
    {
        public static EntryChangeActionType ToActionType(this EntryChangeType entryChangeType)
        {
            return entryChangeType switch
            {
                EntryChangeType.MemberAdded   => EntryChangeActionType.Created,
                EntryChangeType.MemberUpdated => EntryChangeActionType.Updated,
                _                             => EntryChangeActionType.Created
            };
        }
    }
}
