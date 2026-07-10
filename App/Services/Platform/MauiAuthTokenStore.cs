using System.Security.Cryptography;
using System.Text;
using DeusaldLocalizerWeb;
using JetBrains.Annotations;

namespace App;

/// <summary>
/// MAUI <see cref="IAuthTokenStore"/> backed by <see cref="SecureStorage"/>. The stored value format is
/// "userId|rawAccessToken", keyed by project Id <em>and</em> the folder path the project was loaded from.
/// Keying on the path as well means two local copies of the same project (identical <c>Metadata.Id</c>)
/// each remember their own signed-in user.
/// <para>
/// <see cref="SecureStorage"/> is Keychain-backed on macOS and needs a code-signing entitlement, so on an
/// <em>unsigned</em> build every call fails (errSecMissingEntitlement). When that happens we transparently
/// fall back to <see cref="Preferences"/> so sign-in still survives a restart — the token was previously
/// silently dropped, which read as "you must be authenticated" on the next sync/push. Preferences are not
/// encrypted, so this is a deliberate downgrade that only kicks in when the OS secure store is unavailable.
/// </para>
/// </summary>
[UsedImplicitly]
public sealed class MauiAuthTokenStore : IAuthTokenStore
{
    private const string _KEY_PREFIX = "dloc:auth:";
    private const string _INDEX_KEY  = "dloc:auth:index"; // tracks Preferences-fallback keys for RemoveAll.

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
        string key = Key(projectId, location);

        string? stored = null;
        try
        {
            stored = await SecureStorage.Default.GetAsync(key);
        }
        catch
        {
            // SecureStorage unavailable — fall through to the Preferences fallback below.
        }

        if (string.IsNullOrEmpty(stored))
            stored = PreferencesGet(key);

        return Parse(stored);
    }

    public async Task SaveAsync(Guid projectId, string location, Guid userId, string rawToken)
    {
        string key   = Key(projectId, location);
        string value = $"{userId}|{rawToken}";
        try
        {
            await SecureStorage.Default.SetAsync(key, value);
            // Secure store took it — drop any stale plaintext copy so the two can't diverge.
            PreferencesRemove(key);
        }
        catch
        {
            // Keychain unavailable (e.g. unsigned macOS) — persist to Preferences so sign-in survives.
            PreferencesSet(key, value);
        }
    }

    public void Remove(Guid projectId, string location)
    {
        string key = Key(projectId, location);
        try
        {
            SecureStorage.Default.Remove(key);
        }
        catch
        {
            // Ignore — nothing to clean up if the secure store is unavailable.
        }
        PreferencesRemove(key);
    }

    public void RemoveAll()
    {
        try
        {
            SecureStorage.Default.RemoveAll();
        }
        catch
        {
            // Ignore — nothing to clean up if the secure store is unavailable.
        }

        foreach (string key in PreferencesIndex())
            Preferences.Default.Remove(key);
        Preferences.Default.Remove(_INDEX_KEY);
    }

    private static (Guid UserId, string Token)? Parse(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return null;

        string[] parts = stored.Split('|', 2);
        if (parts.Length == 2 && Guid.TryParse(parts[0], out Guid userId) && !string.IsNullOrEmpty(parts[1]))
            return (userId, parts[1]);
        return null;
    }

    // ── Preferences fallback (plaintext) ──────────────────────────────────────
    // Kept behind a small index so RemoveAll can wipe every fallback token without clearing
    // unrelated preferences.

    private static string? PreferencesGet(string key)
    {
        string value = Preferences.Default.Get(key, string.Empty);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static void PreferencesSet(string key, string value)
    {
        Preferences.Default.Set(key, value);
        HashSet<string> index = PreferencesIndex();
        if (index.Add(key))
            Preferences.Default.Set(_INDEX_KEY, string.Join('\n', index));
    }

    private static void PreferencesRemove(string key)
    {
        if (Preferences.Default.Get(key, string.Empty).Length == 0) return;

        Preferences.Default.Remove(key);
        HashSet<string> index = PreferencesIndex();
        if (index.Remove(key))
            Preferences.Default.Set(_INDEX_KEY, string.Join('\n', index));
    }

    private static HashSet<string> PreferencesIndex()
    {
        string raw = Preferences.Default.Get(_INDEX_KEY, string.Empty);
        return string.IsNullOrEmpty(raw)
            ? new HashSet<string>()
            : new HashSet<string>(raw.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }
}
