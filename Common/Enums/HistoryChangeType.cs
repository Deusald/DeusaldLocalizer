namespace DeusaldLocalizerCommon
{
    /// <summary>What happened to the entity.</summary>
    public enum HistoryChangeType
    {
        Created       = 0,
        Updated       = 1,
        Deleted       = 2,
        StatusChanged = 3,
        Approved      = 4,
        Rejected      = 5,
    }
}