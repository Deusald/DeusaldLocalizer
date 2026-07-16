using System;
using System.Collections.Generic;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// Finds <c>@username</c> mentions inside free-form text (comments, suggestions). Matches against the
    /// project's actual member usernames rather than a generic word pattern, because usernames can contain
    /// dots (e.g. <c>offline.user</c>) that a word-boundary regex would break on. Shared by the mention
    /// highlight renderer and the notification scan so both agree on what counts as a mention.
    /// </summary>
    public static class MentionParser
    {
        /// <summary>
        /// Returns each mention as (Start, Length, Username), where Start points at the leading '@' and
        /// Length spans '@' + the matched username. Longest known username wins at a given position, so
        /// <c>@anna.smith</c> is preferred over <c>@anna</c>.
        /// </summary>
        public static List<(int Start, int Length, string Username)> FindMentions(
            string text, IReadOnlyCollection<string> knownUsernames)
        {
            List<(int Start, int Length, string Username)> result = new();
            if (string.IsNullOrEmpty(text) || knownUsernames == null || knownUsernames.Count == 0)
                return result;

            // Longest first so a longer username is preferred over a shorter one sharing its prefix.
            List<string> ordered = new(knownUsernames);
            ordered.Sort((a, b) => b.Length.CompareTo(a.Length));

            for (int i = 0; i < text.Length; ++i)
            {
                if (text[i] != '@') continue;

                // A '@' glued to a preceding word char is part of that token (e.g. an email), not a mention.
                if (i > 0 && IsUsernameChar(text[i - 1])) continue;

                foreach (string name in ordered)
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    if (i + 1 + name.Length > text.Length) continue;
                    if (string.Compare(text, i + 1, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) != 0) continue;

                    int after = i + 1 + name.Length;
                    // The char right after must not extend the token, else this is a different, longer name.
                    if (after < text.Length && IsUsernameChar(text[after])) continue;

                    result.Add((i, name.Length + 1, name));
                    i = after - 1; // resume past the matched mention
                    break;
                }
            }
            return result;
        }

        /// <summary>True when <paramref name="text"/> mentions <paramref name="username"/>.</summary>
        public static bool MentionsUser(string text, string username, IReadOnlyCollection<string> knownUsernames)
        {
            if (string.IsNullOrEmpty(username)) return false;

            foreach ((int Start, int Length, string Username) mention in FindMentions(text, knownUsernames))
            {
                if (string.Equals(mention.Username, username, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>True for characters that can appear inside a username (letters, digits, '.', '_', '-').</summary>
        public static bool IsUsernameChar(char c) =>
            char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-';
    }
}
