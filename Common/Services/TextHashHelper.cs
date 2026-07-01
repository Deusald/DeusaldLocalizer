using System.Security.Cryptography;
using System.Text;

namespace DeusaldLocalizerCommon
{
    public static class TextHashHelper
    {
        /// <summary>Returns a hex-encoded SHA-256 hash of the given text, or empty string if text is null/empty.</summary>
        public static string Compute(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            byte[] inputBytes = Encoding.UTF8.GetBytes(text);
            byte[] hashBytes;

            using (SHA256 sha = SHA256.Create())
            {
                hashBytes = sha.ComputeHash(inputBytes);
            }

            StringBuilder sb = new StringBuilder(hashBytes.Length * 2);
            foreach (byte b in hashBytes)
                sb.Append(b.ToString("x2"));

            return sb.ToString();
        }
    }
}