using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace DeusaldLocalizerCommon
{
    public class ImportResult
    {
        public int          KeysUpdated      { get; set; }
        public int          SuggestionsAdded { get; set; }
        public int          KeysSkipped      { get; set; }
        public List<string> Warnings         { get; set; } = new();

        /// <summary>
        /// Suggestions created by this import. The caller records each one so it
        /// lands in UncommitedChanges.
        /// </summary>
        public List<ImportedSuggestion> AppliedSuggestions { get; set; } = new();
    }

    /// <summary>A single suggestion the import produced, paired with the translation it belongs to.</summary>
    public class ImportedSuggestion
    {
        public Guid                     KeyId       { get; set; }
        public LocKeyTranslation        Translation { get; set; } = null!;
        public LocTranslationSuggestion Suggestion  { get; set; } = null!;
    }

    /// <summary>
    /// Reads an xlsx file produced by LocalizationExportService and adds each
    /// changed translation cell as a Suggestion on the matching key+language,
    /// leaving the existing (approved) translation text untouched.
    ///
    /// Rules:
    ///   - Matches rows by KeyId (column A / first column).
    ///   - Only reads translation (language) columns.
    ///   - Skips rows where the KeyId does not exist in the project.
    ///   - Skips empty cells (never proposes an empty string).
    ///   - Skips cells whose text still matches the "#hash" column written at export
    ///     time (the translator never touched that cell).
    ///   - Skips cells whose text already matches the current translation or an
    ///     existing suggestion (nothing new to propose).
    ///   - Adds a LocTranslationSuggestion authored by the importing user without
    ///     altering the translation's own text, status or SourceChanged flag.
    /// </summary>
    public static class LocalizationImportService
    {
        public static ImportResult ImportFromStream(Stream stream, LocProject project, Guid authorId)
        {
            ImportResult result = new ImportResult();

            using XLWorkbook wb    = new XLWorkbook(stream);
            IXLWorksheet     sheet = wb.Worksheets.First();

            // ── Read header row to discover column positions ────────────────
            IXLRow headerRow = sheet.Row(1);

            int                     keyIdCol = -1;
            Dictionary<int, string> langCols = new Dictionary<int, string>();
            // language code -> column holding the SHA-256 written at export time
            Dictionary<string, int> hashCols = new Dictionary<string, int>();

            foreach (IXLCell cell in headerRow.CellsUsed())
            {
                string header = cell.GetString().Trim();
                int    c      = cell.Address.ColumnNumber;

                if (header == "KeyId")
                {
                    keyIdCol = c;
                }
                else if (header.EndsWith(LocalizationExportService.HashHeaderSuffix))
                {
                    string lang = header.Substring(0, header.Length - LocalizationExportService.HashHeaderSuffix.Length);
                    if (project.Metadata.Languages.Contains(lang)) hashCols[lang] = c;
                }
                else if (project.Metadata.Languages.Contains(header))
                {
                    langCols[c] = header;
                }
            }

            if (keyIdCol < 0)
            {
                result.Warnings.Add("Could not find a 'KeyId' column — wrong file format?");
                return result;
            }

            if (langCols.Count == 0)
            {
                result.Warnings.Add("No matching language columns found in this file.");
                return result;
            }

            // ── Process data rows ───────────────────────────────────────────
            int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;

            for (int row = 2; row <= lastRow; row++)
            {
                string rawId = sheet.Cell(row, keyIdCol).GetString().Trim();
                if (string.IsNullOrEmpty(rawId)) continue;

                if (!Guid.TryParse(rawId, out Guid keyId))
                {
                    result.Warnings.Add($"Row {row}: invalid KeyId '{rawId}', skipped.");
                    result.KeysSkipped++;
                    continue;
                }

                LocLocalizationKey? key = project.Keys.Find(k => k.Id == keyId);
                if (key == null)
                {
                    result.KeysSkipped++;
                    continue;
                }

                bool keyTouched = false;

                foreach (KeyValuePair<int, string> langEntry in langCols)
                {
                    int    c        = langEntry.Key;
                    string langCode = langEntry.Value;

                    string text = sheet.Cell(row, c).GetString();
                    if (string.IsNullOrEmpty(text)) continue; // never propose an empty string

                    // If the cell still hashes to the value written at export time the
                    // translator never edited it — nothing to import.
                    if (hashCols.TryGetValue(langCode, out int hashCol))
                    {
                        string exportedHash = sheet.Cell(row, hashCol).GetString().Trim();
                        if (exportedHash.Length > 0 && TextHashHelper.Compute(text) == exportedHash)
                            continue;
                    }

                    if (key.MaxLength != 0 && text.Length > key.MaxLength)
                    {
                        result.Warnings.Add($"Row {row}: invalid length for language {langCode}.");
                        continue;
                    }

                    LocKeyTranslation? translation = key.Translations.Find(t => t.LanguageId == langCode);
                    if (translation == null)
                    {
                        translation = new LocKeyTranslation
                        {
                            LanguageId = langCode
                        };
                        key.Translations.Add(translation);
                    }

                    // Nothing new to propose if the current translation already reads
                    // like this, or an identical suggestion is already pending.
                    if (translation.Text == text) continue;
                    if (translation.Suggestions.Any(s => s.Text == text)) continue;

                    LocTranslationSuggestion suggestion = new LocTranslationSuggestion
                    {
                        Text     = text,
                        AuthorId = authorId
                    };

                    // Only add the proposal — do NOT touch the translation's own text,
                    // status or SourceChanged flag. Editing those here leaves the key
                    // stuck in "Needs review" once the suggestion is later rejected.
                    translation.Suggestions.Add(suggestion);

                    result.AppliedSuggestions.Add(new ImportedSuggestion
                    {
                        KeyId       = key.Id,
                        Translation = translation,
                        Suggestion  = suggestion
                    });

                    result.SuggestionsAdded++;
                    keyTouched = true;
                }

                if (keyTouched)
                {
                    key.UpdatedAt = DateTime.UtcNow;
                    result.KeysUpdated++;
                }
            }

            return result;
        }
    }
}
