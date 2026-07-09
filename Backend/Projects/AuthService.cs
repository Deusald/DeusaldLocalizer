using DeusaldLocalizerCommon;

namespace DeusaldLocalizerBackend;

/// <summary>
/// Authenticates a request against the project's members. The client sends the member's
/// <c>UserId</c> plus the raw access token; we verify it against the stored BCrypt hash.
/// </summary>
public sealed class AuthService
{
    /// <summary>
    /// Returns the authenticated member, or null when the user is unknown, banned, or the token
    /// does not verify.
    /// </summary>
    public LocProjectMember? Authenticate(LocProject project, Guid userId, string rawToken)
    {
        if (userId == Guid.Empty || string.IsNullOrEmpty(rawToken)) return null;

        LocProjectMember? member = project.ProjectMembers.Find(m => m.UserId == userId);
        if (member == null || member.IsBanned) return null;

        return AccessTokenService.VerifyToken(rawToken, member.HashedAccessToken) ? member : null;
    }

    /// <summary>
    /// Authenticates by username instead of <c>UserId</c>. Used for the first-time full download,
    /// where the connecting user only holds a username + one-time token and does not yet know their
    /// <c>UserId</c>. Returns null when the user is unknown, banned, or the token does not verify.
    /// </summary>
    public LocProjectMember? AuthenticateByUsername(LocProject project, string username, string rawToken)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(rawToken)) return null;

        LocProjectMember? member = project.ProjectMembers.Find(m =>
            string.Equals(m.Username, username, StringComparison.OrdinalIgnoreCase));
        if (member == null || member.IsBanned) return null;

        return AccessTokenService.VerifyToken(rawToken, member.HashedAccessToken) ? member : null;
    }
}
