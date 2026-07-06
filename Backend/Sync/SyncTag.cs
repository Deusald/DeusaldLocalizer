namespace DeusaldLocalizerBackend;

/// <summary>
/// The marker written into every commit message of a push batch and searched for on sync.
/// Format must stay identical on the write (push) and read (log --grep) sides.
/// </summary>
public static class SyncTag
{
    public static string For(Guid syncId) => "SyncId: " + syncId;
}
