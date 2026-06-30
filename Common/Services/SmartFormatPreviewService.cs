using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// Renders a simple preview of SmartFormat-style "{variable}" text by
    /// substituting each declared KeyVariableDto with its current sample value.
    /// This is a lightweight preview only — not a full SmartFormat implementation.
    /// </summary>
    public static class SmartFormatPreviewService
    {
        private static readonly Regex _PlaceholderPattern = new(@"\{([a-zA-Z0-9_]+)\}", RegexOptions.Compiled);

        public static string Render(string text, List<KeyVariableDto> variables)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var lookup = new Dictionary<string, string>();
            foreach (var v in variables)
                lookup[v.Name] = string.IsNullOrEmpty(v.SampleValue) ? $"{{{v.Name}}}" : v.SampleValue;

            return _PlaceholderPattern.Replace(text, match =>
            {
                var name = match.Groups[1].Value;
                return lookup.TryGetValue(name, out var value) ? value : match.Value;
            });
        }
    }
}