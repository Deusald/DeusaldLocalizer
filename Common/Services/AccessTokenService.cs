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
