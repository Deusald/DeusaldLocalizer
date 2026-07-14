namespace DeusaldLocalizerCommon
{
    public enum EntryChangeType
    {
        MemberAdded,
        MemberUpdated,
        MemberRemoved,
        LanguageAdded,
        LanguageRemoved,
        KeyAdded,
        KeyUpdated,
        KeyRemoved,
        CategoryAdded,
        CategoryUpdated,
        CategoryRemoved,
        TranslationUpdated,
        SuggestionAdded,
        SuggestionVoted,
        SuggestionRemoved,
        FlagAdded,
        FlagRemoved,
        TagAdded,
        TagRemoved,
        VariableAdded,
        VariableUpdated,
        VariableRemoved,
        EnumAdded,
        EnumUpdated,
        EnumRemoved
    }

    public static class EntryChangeTypeExtensions
    {
        public static EntryChangeActionType ToActionType(this EntryChangeType entryChangeType)
        {
            return entryChangeType switch
            {
                EntryChangeType.MemberAdded        => EntryChangeActionType.Created,
                EntryChangeType.MemberUpdated      => EntryChangeActionType.Updated,
                EntryChangeType.MemberRemoved      => EntryChangeActionType.Deleted,
                EntryChangeType.LanguageAdded      => EntryChangeActionType.Created,
                EntryChangeType.LanguageRemoved    => EntryChangeActionType.Deleted,
                EntryChangeType.KeyAdded           => EntryChangeActionType.Created,
                EntryChangeType.KeyUpdated         => EntryChangeActionType.Updated,
                EntryChangeType.KeyRemoved         => EntryChangeActionType.Deleted,
                EntryChangeType.CategoryAdded      => EntryChangeActionType.Created,
                EntryChangeType.CategoryUpdated    => EntryChangeActionType.Updated,
                EntryChangeType.CategoryRemoved    => EntryChangeActionType.Deleted,
                EntryChangeType.TranslationUpdated => EntryChangeActionType.Updated,
                EntryChangeType.SuggestionAdded    => EntryChangeActionType.Created,
                EntryChangeType.SuggestionVoted    => EntryChangeActionType.Updated,
                EntryChangeType.SuggestionRemoved  => EntryChangeActionType.Deleted,
                EntryChangeType.FlagAdded          => EntryChangeActionType.Created,
                EntryChangeType.FlagRemoved        => EntryChangeActionType.Deleted,
                EntryChangeType.TagAdded           => EntryChangeActionType.Created,
                EntryChangeType.TagRemoved         => EntryChangeActionType.Deleted,
                EntryChangeType.VariableAdded      => EntryChangeActionType.Created,
                EntryChangeType.VariableUpdated    => EntryChangeActionType.Updated,
                EntryChangeType.VariableRemoved    => EntryChangeActionType.Deleted,
                EntryChangeType.EnumAdded          => EntryChangeActionType.Created,
                EntryChangeType.EnumUpdated        => EntryChangeActionType.Updated,
                EntryChangeType.EnumRemoved        => EntryChangeActionType.Deleted,
                _                                  => EntryChangeActionType.Created
            };
        }
    }
}
