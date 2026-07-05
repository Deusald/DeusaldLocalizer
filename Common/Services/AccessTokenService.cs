using System;
using System.Security.Cryptography;
using JetBrains.Annotations;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// Central place for access-token hashing, verification and generation.
    /// Members only ever store a BCrypt hash in <see cref="LocProjectMember.HashedAccessToken"/>;
    /// the raw token is shown to the user once and never persisted.
    /// </summary>
    [PublicAPI]
    public static class AccessTokenService
    {
        private const int _TOKEN_BYTES = 24;

        /// <summary>BCrypt-hashes a raw access token for storage.</summary>
        public static string HashToken(string rawToken) => BCrypt.Net.BCrypt.HashPassword(rawToken);

        /// <summary>Verifies a raw token against a stored hash. An empty hash never verifies.</summary>
        public static bool VerifyToken(string rawToken, string hashedToken)
        {
            if (string.IsNullOrEmpty(hashedToken)) return false;
            return BCrypt.Net.BCrypt.Verify(rawToken, hashedToken);
        }

        /// <summary>
        /// The initial hash handed to a freshly created member: the hash of their own
        /// username. This lets them sign in the first time using their username as the
        /// access token, at which point they are prompted to store a real one.
        /// </summary>
        public static string InitialTokenHash(string username) => HashToken(username);

        /// <summary>
        /// True when the member still carries the initial (username-based) token —
        /// i.e. they have never generated a real access token for themselves.
        /// </summary>
        public static bool IsInitialToken(LocProjectMember member) =>
            VerifyToken(member.Username, member.HashedAccessToken);

        /// <summary>Generates a new cryptographically-random, URL-safe access token.</summary>
        public static string GenerateToken()
        {
            byte[] bytes = new byte[_TOKEN_BYTES];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);

            return Convert.ToBase64String(bytes)
                          .Replace('+', '-')
                          .Replace('/', '_')
                          .TrimEnd('=');
        }
    }
}
