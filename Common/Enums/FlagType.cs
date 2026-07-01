namespace DeusaldLocalizerCommon
{
    /// <summary>Structured workflow flags that can be attached to a key.</summary>
    public enum FlagType
    {
        NeedsReview    = 0,
        Outdated       = 1,
        DoNotTranslate = 2,
        InProgress     = 3,
        Disputed       = 4,
    }
}