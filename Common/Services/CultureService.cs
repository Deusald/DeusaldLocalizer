using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// Provides a deduplicated, sorted list of CultureInfo entries for use
    /// in language pickers. Uses the system's installed cultures; no DTOs needed.
    /// </summary>
    public static class CultureService
    {
        private static readonly List<CultureEntry> _All;

        static CultureService()
        {
            _All = CultureInfo
                  .GetCultures(CultureTypes.SpecificCultures)
                  .Where(c => !string.IsNullOrEmpty(c.Name))
                  .Select(c => new CultureEntry(c))
                  .OrderBy(c => c.EnglishName)
                  .ToList();
        }

        /// <summary>All available specific cultures, alphabetically by English name.</summary>
        public static IReadOnlyList<CultureEntry> All => _All;

        /// <summary>
        /// Returns cultures whose English name, native name, or BCP-47 code
        /// contains the query (case-insensitive). Empty query returns all.
        /// </summary>
        public static List<CultureEntry> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return _All;

            string q = query.Trim().ToLowerInvariant();
            return _All
                  .Where(c => c.EnglishName.ToLowerInvariant().Contains(q)
                           || c.NativeName.ToLowerInvariant().Contains(q)
                           || c.Code.ToLowerInvariant().Contains(q))
                  .ToList();
        }

        /// <summary>Look up a single entry by BCP-47 code (e.g. "en-US").</summary>
        public static CultureEntry? FindByCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            return _All.FirstOrDefault(c =>
                string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Thin wrapper around a CultureInfo for display purposes.</summary>
    public sealed class CultureEntry
    {
        public CultureEntry(CultureInfo culture)
        {
            Culture     = culture;
            Code        = culture.Name;        // e.g. "en-US"
            EnglishName = culture.EnglishName; // e.g. "English (United States)"
            NativeName  = culture.NativeName;  // e.g. "English (United States)"

            // Strip parenthetical region for a short display name
            int paren = EnglishName.IndexOf('(');
            ShortName = paren > 0
                            ? EnglishName.Substring(0, paren).TrimEnd()
                            : EnglishName;

            // Build a flag emoji from the region part of the BCP-47 tag
            string region = culture.Name.Contains('-')
                                ? culture.Name.Substring(culture.Name.LastIndexOf('-') + 1).ToUpperInvariant()
                                : string.Empty;

            FlagEmoji = RegionToFlag(region);
        }

        public CultureInfo Culture     { get; }
        public string      Code        { get; } // "en-US"
        public string      EnglishName { get; } // "English (United States)"
        public string      NativeName  { get; } // "English (United States)"
        public string      ShortName   { get; } // "English"
        public string      FlagEmoji   { get; } // "🇺🇸"

        /// <summary>Compact label for dropdowns: "English (United States) — en-US"</summary>
        public string DisplayLabel => $"{EnglishName} — {Code}";

        private static string RegionToFlag(string regionCode)
        {
            if (regionCode.Length != 2) return "🌐";
            // Regional indicator symbols: A = U+1F1E6, offset from 'A' (0x41)
            int a = regionCode[0] - 'A' + 0x1F1E6;
            int b = regionCode[1] - 'A' + 0x1F1E6;
            return char.ConvertFromUtf32(a) + char.ConvertFromUtf32(b);
        }

        public override string ToString() => DisplayLabel;
    }
}