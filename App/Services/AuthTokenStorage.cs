using System;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Maui.Storage;

namespace App;

/// <summary>
/// Wraps <see cref="SecureStorage"/> for per-project sign-in credentials.
/// The stored value format is "userId|rawAccessToken", keyed by project Id.
/// All calls swallow platform exceptions (SecureStorage may be unavailable) —
/// a failure just means the user has to sign in again next time.
/// </summary>
[PublicAPI]
public static class AuthTokenStorage
{
    private const string _KEY_PREFIX = "dloc:auth:";

    private static string Key(Guid projectId) => $"{_KEY_PREFIX}{projectId}";

    /// <summary>Returns the cached (userId, rawToken) for a project, or null if none/invalid.</summary>
    public static async Task<(Guid UserId, string Token)?> GetAsync(Guid projectId)
    {
        try
        {
            string? stored = await SecureStorage.Default.GetAsync(Key(projectId));
            if (string.IsNullOrEmpty(stored)) return null;

            string[] parts = stored.Split('|', 2);
            if (parts.Length == 2 && Guid.TryParse(parts[0], out Guid userId) && !string.IsNullOrEmpty(parts[1]))
                return (userId, parts[1]);
        }
        catch
        {
            // SecureStorage unavailable — treat as no cached credentials.
        }
        return null;
    }

    public static async Task SaveAsync(Guid projectId, Guid userId, string rawToken)
    {
        try
        {
            await SecureStorage.Default.SetAsync(Key(projectId), $"{userId}|{rawToken}");
        }
        catch
        {
            // Non-fatal — the user will just need to sign in again next time.
        }
    }

    public static void Remove(Guid projectId)
    {
        try
        {
            SecureStorage.Default.Remove(Key(projectId));
        }
        catch
        {
            // Ignore — nothing to clean up if storage is unavailable.
        }
    }
}
