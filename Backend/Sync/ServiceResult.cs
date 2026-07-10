namespace DeusaldLocalizerBackend;

public enum RequestOutcome
{
    Ok,
    ProjectNotFound,
    Unauthorized,
}

/// <summary>Carries a service outcome so the controller can map it to the right HTTP status.</summary>
public sealed class ServiceResult<T> where T : class
{
    public RequestOutcome Outcome { get; private init; }
    public T?             Value   { get; private init; }

    public static ServiceResult<T> Ok(T value)    => new() { Outcome = RequestOutcome.Ok, Value = value };
    public static ServiceResult<T> NotFound()     => new() { Outcome = RequestOutcome.ProjectNotFound };
    public static ServiceResult<T> Unauthorized() => new() { Outcome = RequestOutcome.Unauthorized };
}