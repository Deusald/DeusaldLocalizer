using System;
using System.Collections.Generic;
using System.Linq;
using SmartFormat;
using SmartFormat.Core.Parsing;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// Builds a <c>Dictionary&lt;string, object&gt;</c> from a key's declared variables
    /// and their current preview values. This dictionary is passed directly to
    /// Smart.Format() so all SmartFormat formatters (plural, conditional, list, etc.)
    /// receive properly typed CLR values rather than strings.
    /// </summary>
    public static class VariablePreviewService
    {
        public static string Render(string text, List<KeyVariableDto> variables,
                                    Dictionary<Guid, string>? previewOverrides,
                                    List<LocEnumDto>? projectEnums)
        {
            HashSet<string> badPlaceholders  = new HashSet<string>();
            HashSet<string> parsingErrorText = new HashSet<string>();

            Smart.Default.OnFormattingFailure     += OnDefaultOnOnFormattingFailure;
            Smart.Default.Parser.OnParsingFailure += OnParserOnOnParsingFailure;

            try
            {
                return Smart.Format(text, Build(variables, previewOverrides, projectEnums));
            }
            catch (Exception)
            {
                string badPlaceholder = string.Join("\n", badPlaceholders);
                string parsingError   = string.Join("\n", parsingErrorText);
                return $"Bad Placeholder Errors:\n{badPlaceholder}\n{parsingError}";
            }
            finally
            {
                Smart.Default.OnFormattingFailure     -= OnDefaultOnOnFormattingFailure;
                Smart.Default.Parser.OnParsingFailure -= OnParserOnOnParsingFailure;
            }

            void OnDefaultOnOnFormattingFailure(object sender, FormattingErrorEventArgs args)
            {
                badPlaceholders.Add(args.Placeholder);
            }

            void OnParserOnOnParsingFailure(object sender, ParsingErrorEventArgs args)
            {
                parsingErrorText.Add(args.Errors.MessageShort);
            }
        }

        /// <param name="variables">The key's declared variables.</param>
        /// <param name="previewOverrides">
        ///   Per-session local overrides keyed by <see cref="KeyVariableDto.Id"/>.
        ///   Values take precedence over <see cref="KeyVariableDto.DefaultPreviewValue"/>.
        ///   Pass null or empty to use only defaults.
        /// </param>
        /// <param name="projectEnums">All LocEnumDtos on the project, for enum resolution.</param>
        private static Dictionary<string, object> Build(
            List<KeyVariableDto> variables,
            Dictionary<Guid, string>? previewOverrides,
            List<LocEnumDto>? projectEnums)
        {
            Dictionary<string, object> dict = new Dictionary<string, object>();

            foreach (KeyVariableDto? v in variables)
            {
                if (string.IsNullOrEmpty(v.Name)) continue;

                string raw = (previewOverrides != null && previewOverrides.TryGetValue(v.Id, out string? ov))
                                 ? ov
                                 : v.DefaultPreviewValue;

                object value = ConvertValue(v, raw, projectEnums);
                dict[v.Name] = value;
            }

            return dict;
        }

        private static object ConvertValue(KeyVariableDto v, string raw, List<LocEnumDto>? enums)
        {
            switch (v.Type)
            {
                case KeyVariableType.Int:
                    return int.TryParse(raw, out int i) ? (object)i : 0;

                case KeyVariableType.Float:
                    return float.TryParse(raw, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float f)
                               ? (object)f
                               : 0f;

                case KeyVariableType.Bool:
                    return raw.ToLowerInvariant() is "true" or "1" or "yes";

                case KeyVariableType.StringArray:
                    return SplitCsv(raw);

                case KeyVariableType.IntArray:
                    return SplitCsv(raw)
                          .Select(s => int.TryParse(s, out int n) ? n : 0)
                          .ToArray();

                case KeyVariableType.FloatArray:
                    return SplitCsv(raw)
                          .Select(s => float.TryParse(s,
                                           System.Globalization.NumberStyles.Float,
                                           System.Globalization.CultureInfo.InvariantCulture, out float ff)
                                           ? ff
                                           : 0f)
                          .ToArray();

                case KeyVariableType.BoolArray:
                    return SplitCsv(raw)
                          .Select(s => s?.ToLowerInvariant() is "true" or "1" or "yes")
                          .ToArray();

                case KeyVariableType.EnumInt:
                {
                    if (v.EnumId == null || enums == null) return 0;
                    LocEnumDto? locEnum = enums.Find(e => e.Id == v.EnumId);
                    if (locEnum == null) return 0;
                    if (!int.TryParse(raw, out int intVal)) return 0;
                    LocEnumEntryDto? entry = locEnum.Entries.Find(e => e.IntValue == intVal);
                    return entry?.IntValue ?? 0;
                }

                case KeyVariableType.EnumString:
                {
                    if (v.EnumId == null || enums == null) return string.Empty;
                    LocEnumDto? locEnum = enums.Find(e => e.Id == v.EnumId);
                    if (locEnum == null) return string.Empty;
                    if (!int.TryParse(raw, out int intVal2)) return string.Empty;
                    LocEnumEntryDto? entry = locEnum.Entries.Find(e => e.IntValue == intVal2);
                    return entry?.StringValue ?? string.Empty;
                }

                default: // String
                    return raw;
            }
        }

        private static string[] SplitCsv(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return Array.Empty<string>();
            return raw.Split(',')
                      .Select(s => s.Trim())
                      .Where(s => !string.IsNullOrEmpty(s))
                      .ToArray();
        }
    }
}