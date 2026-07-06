using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Maui.Storage;

namespace App;

/// <summary>
/// Wraps <see cref="SecureStorage"/> for per-project sign-in credentials.
/// The stored value format is "userId|rawAccessToken", keyed by project Id
/// <em>and</em> the folder path the project was loaded from. Keying on the path
/// as well means two local copies of the same project (identical <c>Metadata.Id</c>)
/// each remember their own signed-in user — handy for testing two accounts side by
/// side. The trade-off is that moving a project folder forgets its cached login.
/// All calls swallow platform exceptions (SecureStorage may be unavailable) —
/// a failure just means the user has to sign in again next time.
/// </summary>
[PublicAPI]
public static class AuthTokenStorage
{
    private const string _KEY_PREFIX = "dloc:auth:";

    private static string Key(Guid projectId, string projectPath) => $"{_KEY_PREFIX}{projectId}:{PathHash(projectPath)}";

    /// <summary>
    /// Stable short hash of the project's folder path, so the SecureStorage key stays valid and
    /// character-safe regardless of the path's contents. The path is normalized (absolute, no
    /// trailing separators, case-insensitive) so the same folder always maps to the same hash.
    /// </summary>
    private static string PathHash(string projectPath)
    {
        string normalized;
        try
        {
            normalized = Path.GetFullPath(projectPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
        }
        catch
        {
            normalized = projectPath.ToLowerInvariant();
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    /// <summary>Returns the cached (userId, rawToken) for a project copy, or null if none/invalid.</summary>
    public static async Task<(Guid UserId, string Token)?> GetAsync(Guid projectId, string projectPath)
    {
        try
        {
            string? stored = await SecureStorage.Default.GetAsync(Key(projectId, projectPath));
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

    public static async Task SaveAsync(Guid projectId, string projectPath, Guid userId, string rawToken)
    {
        try
        {
            await SecureStorage.Default.SetAsync(Key(projectId, projectPath), $"{userId}|{rawToken}");
        }
        catch
        {
            // Non-fatal — the user will just need to sign in again next time.
        }
    }

    public static void Remove(Guid projectId, string projectPath)
    {
        try
        {
            SecureStorage.Default.Remove(Key(projectId, projectPath));
        }
        catch
        {
            // Ignore — nothing to clean up if storage is unavailable.
        }
    }
}
