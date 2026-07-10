using Microsoft.JSInterop;

namespace DeusaldLocalizerWeb;

/// <summary>
/// Web <see cref="IAuthTokenStore"/> backed by <c>localStorage</c>. The value format is
/// "userId|rawAccessToken", keyed by project id and the project's location handle. Standard SPA
/// trade-off: the token is readable by scripts on the origin (XSS-exposed), acceptable for a community
/// tool; the only truly safe boundary remains a successful push.
/// </summary>
public sealed class LocalStorageAuthTokenStore(IJSRuntime js) : IAuthTokenStore
{
    private const string _KEY_PREFIX = "dloc:auth:";

    private readonly IJSInProcessRuntime _Js = (IJSInProcessRuntime)js;

    private static string Key(Guid projectId, string location) => $"{_KEY_PREFIX}{projectId}:{location}";

    public Task<(Guid UserId, string Token)?> GetAsync(Guid projectId, string location)
    {
        string? stored = _Js.Invoke<string?>("localStorage.getItem", Key(projectId, location));
        if (string.IsNullOrEmpty(stored)) return Task.FromResult<(Guid, string)?>(null);

        string[] parts = stored.Split('|', 2);
        if (parts.Length == 2 && Guid.TryParse(parts[0], out Guid userId) && !string.IsNullOrEmpty(parts[1]))
            return Task.FromResult<(Guid, string)?>((userId, parts[1]));

        return Task.FromResult<(Guid, string)?>(null);
    }

    public Task SaveAsync(Guid projectId, string location, Guid userId, string rawToken)
    {
        _Js.InvokeVoid("localStorage.setItem", Key(projectId, location), $"{userId}|{rawToken}");
        return Task.CompletedTask;
    }

    public void Remove(Guid projectId, string location) =>
        _Js.InvokeVoid("localStorage.removeItem", Key(projectId, location));

    public void RemoveAll() => _Js.InvokeVoid("dlocRemoveByPrefix", _KEY_PREFIX);
}