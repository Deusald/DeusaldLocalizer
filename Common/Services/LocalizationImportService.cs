using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace DeusaldLocalizerCommon
{
    public class ImportResult
    {
        public int          KeysUpdated   { get; set; }
        public int          CellsImported { get; set; }
        public int          KeysSkipped   { get; set; }
        public List<string> Warnings      { get; set; } = new();
    }

    /// <summary>
    /// Reads an xlsx file produced by LocalizationExportService and updates
    /// translation text for matching keys (matched by KeyId column).
    ///
    /// Rules:
    ///   - Matches rows by KeyId (column A / first column).
    ///   - Only updates translation columns (columns after SourceHash).
    ///   - Skips rows where the KeyId does not exist in the project.
    ///   - Skips cells that are empty (does not clear existing translations).
    ///   - Sets status to Draft on import; reviewer/admin should re-approve.
    /// </summary>
    public static class LocalizationImportService
    {
        public static ImportResult ImportFromStream(Stream stream, ProjectDto project, UserDto currentUser)
        {
            ImportResult result = new ImportResult();

            using XLWorkbook wb    = new XLWorkbook(stream);
            IXLWorksheet     sheet = wb.Worksheets.First();

            // ── Read header row to discover column positions ────────────────
            IXLRow headerRow = sheet.Row(1);

            int                     keyIdCol = -1;
            Dictionary<int, string> langCols = new Dictionary<int, string>();

            foreach (IXLCell cell in headerRow.CellsUsed())
            {
                string header = cell.GetString().Trim();
                int    c      = cell.Address.ColumnNumber;

                if (header == "KeyId") keyIdCol                          = c;
                else if (project.Languages.Contains(header)) langCols[c] = header;
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

                LocalizationKeyDto? key = project.Keys.Find(k => k.Id == keyId);
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
                    if (string.IsNullOrEmpty(text)) continue; // never clear existing

                    if (key.MaxLength != 0 && text.Length > key.MaxLength)
                    {
                        result.Warnings.Add($"Row {row}: invalid length for language {langCode}.");
                        continue;
                    }

                    TranslationDto? existing = key.Translations.Find(t => t.LanguageId == langCode);
                    if (existing == null)
                    {
                        existing = new TranslationDto
                        {
                            KeyId      = key.Id,
                            LanguageId = langCode,
                        };
                        key.Translations.Add(existing);
                    }

                    if (existing.Text == text) continue; // no change needed

                    existing.Text      = text;
                    existing.UpdatedBy = currentUser.Id;
                    existing.UpdatedAt = DateTime.UtcNow;
                    existing.Status    = TranslationStatus.Draft;

                    // Record which source version this was based on
                    TranslationDto? src = key.Translations.Find(t => t.LanguageId == project.MainLanguageId);
                    existing.BaseTextHash = TextHashHelper.Compute(src?.Text ?? "");

                    result.CellsImported++;
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