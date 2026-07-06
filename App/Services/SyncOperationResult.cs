using DeusaldLocalizerCommon;
using JetBrains.Annotations;

namespace App;

public enum SyncOutcome
{
    UpToDate,
    Updated,
    FullResync,
    NotOnline,
    NoCredentials,
    Failed,
}

[PublicAPI]
public sealed class SyncOperationResult
{
    public SyncOutcome Outcome      { get; set; }
    public int         ChangedFiles { get; set; }
    public int         Conflicts    { get; set; }
    public string?     Error        { get; set; }
}

public enum PushOutcome
{
    Success,
    BlockedByConflicts, // local conflicts detected before sending
    Conflict,           // server rejected because of conflicts
    Failed,             // repo moved during processing, or a transport error
    NotOnline,
    NoCredentials,
}

[PublicAPI]
public sealed class PushOperationResult
{
    public PushOutcome Outcome   { get; set; }
    public int         Conflicts { get; set; }
    public string?     Message   { get; set; }
}

[PublicAPI]
public sealed class InitialTokenResult
{
    /// <summary>The freshly generated raw token to show the user once. Null when the rotation failed.</summary>
    public string?     RawToken { get; set; }

    /// <summary>The project reloaded after the rotation was pushed. Only set when <see cref="RawToken"/> is.</summary>
    public LocProject? Project  { get; set; }

    /// <summary>Human-readable failure reason, set only when <see cref="RawToken"/> is null.</summary>
    public string?     Error    { get; set; }
}
