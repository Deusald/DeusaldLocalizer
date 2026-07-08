using System.Security.Cryptography;
using System.Text;
using DeusaldLocalizerWeb;
using JetBrains.Annotations;

namespace App;

/// <summary>
/// MAUI <see cref="IAuthTokenStore"/> backed by <see cref="SecureStorage"/>. The stored value format is
/// "userId|rawAccessToken", keyed by project Id <em>and</em> the folder path the project was loaded from.
/// Keying on the path as well means two local copies of the same project (identical <c>Metadata.Id</c>)
/// each remember their own signed-in user. All calls swallow platform exceptions (SecureStorage may be
/// unavailable) — a failure just means the user has to sign in again next time.
/// </summary>
[UsedImplicitly]
public sealed class MauiAuthTokenStore : IAuthTokenStore
{
    private const string _KEY_PREFIX = "dloc:auth:";

    private static string Key(Guid projectId, string location) => $"{_KEY_PREFIX}{projectId}:{PathHash(location)}";

    /// <summary>
    /// Stable short hash of the project's folder path, so the SecureStorage key stays valid and
    /// character-safe regardless of the path's contents. The path is normalized (absolute, no
    /// trailing separators, case-insensitive) so the same folder always maps to the same hash.
    /// </summary>
    private static string PathHash(string location)
    {
        string normalized;
        try
        {
            normalized = Path.GetFullPath(location).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
        }
        catch
        {
            normalized = location.ToLowerInvariant();
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    public async Task<(Guid UserId, string Token)?> GetAsync(Guid projectId, string location)
    {
        try
        {
            string? stored = await SecureStorage.Default.GetAsync(Key(projectId, location));
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

    public async Task SaveAsync(Guid projectId, string location, Guid userId, string rawToken)
    {
        try
        {
            await SecureStorage.Default.SetAsync(Key(projectId, location), $"{userId}|{rawToken}");
        }
        catch
        {
            // Non-fatal — the user will just need to sign in again next time.
        }
    }

    public void Remove(Guid projectId, string location)
    {
        try
        {
            SecureStorage.Default.Remove(Key(projectId, location));
        }
        catch
        {
            // Ignore — nothing to clean up if storage is unavailable.
        }
    }

    public void RemoveAll()
    {
        try
        {
            SecureStorage.Default.RemoveAll();
        }
        catch
        {
            // Ignore — nothing to clean up if storage is unavailable.
        }
    }
}
