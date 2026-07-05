namespace DeusaldLocalizerCommon
{
    public enum EntryChangeType { }

    public static class EntryChangeTypeExtensions
    {
        public static EntryChangeActionType ToActionType(this EntryChangeType entryChangeType)
        {
            return entryChangeType switch
            {
                _ => EntryChangeActionType.Created
            };
        }
    }
}