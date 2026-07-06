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
