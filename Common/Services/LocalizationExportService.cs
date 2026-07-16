using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// Exports localization keys and their translations to a structured xlsx file.
    /// Admin-only: the caller must verify permissions before invoking.
    ///
    /// Column layout:
    ///   KeyId | KeyName | KeyDescription | [Tags] | MaxLength | SourceHash | [lang1] | [lang1 #hash] | ...
    /// Languages are ordered source-language first, then the rest alphabetical.
    /// Each language column is followed by a "#hash" column holding an SHA-256 of the
    /// exported text, so the importer can tell which cells the translator actually changed.
    /// </summary>
    public static class LocalizationExportService
    {
        /// <summary>Header suffix that marks the per-language hash column (e.g. "en #hash").</summary>
        public const string HASH_HEADER_SUFFIX = " #hash";

        public static MemoryStream ExportToStream(LocProject project, LocExportOptions? options = null)
        {
            using XLWorkbook wb    = new XLWorkbook();
            IXLWorksheet     sheet = wb.AddWorksheet("Translations");

            // ── Build ordered language list: source first, rest alphabetical ──
            List<string> languages = BuildLanguageList(project, options);

            bool includeTagsCol = options is { IncludeTagsColumn: true };

            // ── Header row ──────────────────────────────────────────────────
            int col = 1;
            sheet.Cell(1, col++).Value = "KeyId";
            sheet.Cell(1, col++).Value = "KeyName";
            sheet.Cell(1, col++).Value = "KeyDescription";

            int tagsCol = -1;
            if (includeTagsCol)
            {
                tagsCol                    = col;
                sheet.Cell(1, col++).Value = "Tags";
            }

            int maxLengthCol = col;
            sheet.Cell(1, col++).Value = "MaxLength";
            sheet.Cell(1, col++).Value = "SourceHash";

            int       langStartCol = col;
            List<int> langTextCols = new List<int>();
            foreach (string lang in languages)
            {
                langTextCols.Add(col);
                sheet.Cell(1, col++).Value = lang;
                sheet.Cell(1, col++).Value = lang + HASH_HEADER_SUFFIX;
            }

            // ── Style header ────────────────────────────────────────────────
            IXLRange headerRange = sheet.Range(1, 1, 1, col - 1);
            headerRange.Style.Font.Bold            = true;
            headerRange.Style.Font.FontColor       = XLColor.White;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#2d2e3f");
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            // Freeze header row
            sheet.SheetView.FreezeRows(1);

            // ── Data rows ───────────────────────────────────────────────────
            int                             row          = 2;
            IEnumerable<LocLocalizationKey> exportedKeys = project.Keys;
            if (options != null)
                exportedKeys = exportedKeys.Where(k => PassesFilter(k, options));
            foreach (LocLocalizationKey key in exportedKeys.OrderBy(k => FullKeyName(k, project)))
            {
                // Get the source translation's BaseTextHash as "SourceHash"
                LocKeyTranslation? sourceTrans = key.Translations
                                                    .Find(t => t.LanguageId == project.Metadata.MainLanguageId);
                string sourceHash = sourceTrans?.BaseTextHash ?? string.Empty;

                col                          = 1;
                sheet.Cell(row, col++).Value = key.Id.ToString();
                sheet.Cell(row, col++).Value = FullKeyName(key, project);
                sheet.Cell(row, col++).Value = key.Description;
                if (includeTagsCol)
                    sheet.Cell(row, col++).Value = string.Join(", ", key.Tags);
                sheet.Cell(row, col++).Value = key.MaxLength == 0 ? (int?)null : key.MaxLength;
                sheet.Cell(row, col++).Value = sourceHash;

                foreach (string lang in languages)
                {
                    LocKeyTranslation? translation = key.Translations.Find(t => t.LanguageId == lang);
                    string             text        = translation?.Text ?? string.Empty;
                    sheet.Cell(row, col++).Value = text;
                    sheet.Cell(row, col++).Value = TextHashHelper.Compute(text);
                }

                row++;
            }

            // ── Column widths ───────────────────────────────────────────────
            sheet.Column(1).Width = 38; // KeyId (UUID)
            sheet.Column(2).Width = 40; // KeyName
            sheet.Column(3).Width = 40; // KeyDescription
            if (tagsCol > 0)
                sheet.Column(tagsCol).Width = 26;      // Tags
            sheet.Column(maxLengthCol).Width     = 12; // MaxLength
            sheet.Column(maxLengthCol + 1).Width = 66; // SourceHash (SHA-256 hex)

            // Language columns — wide text with wrap; hash columns narrow and de-emphasized.
            for (int c = langStartCol; c < col; c++)
            {
                bool isHashCol = (c - langStartCol) % 2 == 1;
                if (isHashCol)
                {
                    sheet.Column(c).Width                     = 16;
                    sheet.Column(c).Style.NumberFormat.Format = "@";
                    sheet.Column(c).Style.Font.FontColor      = XLColor.FromHtml("#888888");
                }
                else
                {
                    sheet.Column(c).Width                    = 50;
                    sheet.Column(c).Style.Alignment.WrapText = true;
                }
            }

            // KeyId column: monospace-style by setting number format to text
            sheet.Column(1).Style.NumberFormat.Format = "@";

            // ── Conditional formatting: red background when LEN > MaxLength ─
            // The MaxLength column holds the limit; 0/blank means no limit so we skip those.
            // For each language TEXT column we add one CF range covering all data rows.
            // The formula uses an absolute column, relative row so Excel evaluates it per-row.
            if (row > 2) // only when there are data rows
            {
                int    lastDataRow = row - 1;
                string maxLenCol   = XLHelper.GetColumnLetterFromNumber(maxLengthCol);
                foreach (int langCol in langTextCols)
                {
                    // Get the Excel column letter for the anchor cell of this range
                    string cellRef = XLHelper.GetColumnLetterFromNumber(langCol) + "2";

                    // Formula: MaxLength > 0 AND LEN > MaxLength
                    string formula = $"AND(${maxLenCol}2>0,LEN({cellRef})>${maxLenCol}2)";

                    IXLRange cfRange = sheet.Range(2, langCol, lastDataRow, langCol);
                    cfRange.AddConditionalFormat()
                           .WhenIsTrue(formula)
                           .Fill.SetBackgroundColor(XLColor.FromHtml("#5c1a17"));

                    // Also set the font color so text stays readable on the dark red bg
                    cfRange.AddConditionalFormat()
                           .WhenIsTrue(formula)
                           .Font.SetFontColor(XLColor.FromHtml("#ff9492"));
                }
            }

            // ── Save to stream ───────────────────────────────────────────────
            MemoryStream stream = new MemoryStream();
            wb.SaveAs(stream);
            stream.Position = 0;
            return stream;
        }

        /// <summary>
        /// Builds the ordered export language list: source language first, remaining project
        /// languages alphabetical. When <paramref name="options"/> selects a subset (non-empty
        /// <see cref="LocExportOptions.Languages"/>) only those languages are kept.
        /// Shared by the Excel and C# exporters so both honour the same ordering and filter.
        /// </summary>
        internal static List<string> BuildLanguageList(LocProject project, LocExportOptions? options)
        {
            bool hasLangFilter = options is { Languages: { Count: > 0 } };
            HashSet<string> selectedLangs = hasLangFilter
                                                ? new HashSet<string>(options!.Languages)
                                                : new HashSet<string>();

            List<string> languages = new List<string>();
            if (!hasLangFilter || selectedLangs.Contains(project.Metadata.MainLanguageId))
                languages.Add(project.Metadata.MainLanguageId);
            foreach (string lang in project.Metadata.Languages.OrderBy(l => l))
            {
                if (lang == project.Metadata.MainLanguageId) continue;
                if (!hasLangFilter || selectedLangs.Contains(lang))
                    languages.Add(lang);
            }
            return languages;
        }

        internal static bool PassesFilter(LocLocalizationKey key, LocExportOptions options)
        {
            // Flags: no-flag keys ride on IncludeNoFlags; otherwise any excluded flag drops the key.
            if (key.Flags.Count == 0)
            {
                if (!options.IncludeNoFlags) return false;
            }
            else if (key.Flags.Any(f => options.ExcludeFlags.Contains(f.Type)))
                return false;

            // Tags: same shape as flags.
            if (key.Tags.Count == 0)
            {
                if (!options.IncludeNoTags) return false;
            }
            else if (key.Tags.Any(t => options.ExcludeTags.Contains(t)))
                return false;

            // Modified-after: drop keys untouched since the cutoff.
            if (options.ModifiedAfter.HasValue && !ModifiedAfter(key, options.ModifiedAfter.Value))
                return false;

            return true;
        }

        /// <summary>
        /// True when the key, or any of its translations, was updated at or after <paramref name="cutoff"/>.
        /// </summary>
        internal static bool ModifiedAfter(LocLocalizationKey key, System.DateTime cutoff)
        {
            if (key.UpdatedAt >= cutoff) return true;
            foreach (LocKeyTranslation translation in key.Translations)
                if (translation.UpdatedAt >= cutoff)
                    return true;
            return false;
        }

        internal static string FullKeyName(LocLocalizationKey key, LocProject project)
        {
            LocCategory? cat = project.Categories.Find(c => c.Id == key.CategoryId);
            if (cat == null) return key.KeyName;

            List<string> parts   = new List<string> { cat.Name };
            LocCategory  current = cat;

            while (current.ParentCategoryId != null)
            {
                LocCategory? parent = project.Categories.Find(c => c.Id == current.ParentCategoryId);
                if (parent == null) break;
                parts.Insert(0, parent.Name);
                current = parent;
            }

            return string.Join("/", parts) + "." + key.KeyName;
        }
    }
}