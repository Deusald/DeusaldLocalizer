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
    ///   KeyId | KeyName | KeyDescription | MaxLength | SourceHash | [lang1] | [lang2] | ...
    /// Languages are ordered source-language first, then the rest alphabetically.
    /// </summary>
    public static class LocalizationExportService
    {
        public static MemoryStream ExportToStream(ProjectDto project)
        {
            using XLWorkbook wb    = new XLWorkbook();
            IXLWorksheet     sheet = wb.AddWorksheet("Translations");

            // ── Build ordered language list: source first, rest alphabetical ──
            List<string> languages = new List<string>();
            languages.Add(project.MainLanguageId);
            foreach (string lang in project.Languages.OrderBy(l => l))
            {
                if (lang != project.MainLanguageId)
                    languages.Add(lang);
            }

            // ── Header row ──────────────────────────────────────────────────
            int col = 1;
            sheet.Cell(1, col++).Value = "KeyId";
            sheet.Cell(1, col++).Value = "KeyName";
            sheet.Cell(1, col++).Value = "KeyDescription";
            sheet.Cell(1, col++).Value = "MaxLength";
            sheet.Cell(1, col++).Value = "SourceHash";

            int langStartCol = col;
            foreach (string lang in languages)
                sheet.Cell(1, col++).Value = lang;

            // ── Style header ────────────────────────────────────────────────
            IXLRange headerRange = sheet.Range(1, 1, 1, col - 1);
            headerRange.Style.Font.Bold            = true;
            headerRange.Style.Font.FontColor       = XLColor.White;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#2d2e3f");
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            // Freeze header row
            sheet.SheetView.FreezeRows(1);

            // ── Data rows ───────────────────────────────────────────────────
            int row = 2;
            foreach (LocalizationKeyDto key in project.Keys.OrderBy(k => FullKeyName(k, project)))
            {
                // Get the source translation's BaseTextHash as "SourceHash"
                TranslationDto? sourceTrans = key.Translations
                    .Find(t => t.LanguageId == project.MainLanguageId);
                string sourceHash = sourceTrans?.BaseTextHash ?? string.Empty;

                col = 1;
                sheet.Cell(row, col++).Value = key.Id.ToString();
                sheet.Cell(row, col++).Value = FullKeyName(key, project);
                sheet.Cell(row, col++).Value = key.Description;
                sheet.Cell(row, col++).Value = key.MaxLength == 0 ? (int?)null : key.MaxLength;
                sheet.Cell(row, col++).Value = sourceHash;

                foreach (string lang in languages)
                {
                    TranslationDto? translation = key.Translations.Find(t => t.LanguageId == lang);
                    sheet.Cell(row, col++).Value = translation?.Text ?? string.Empty;
                }

                row++;
            }

            // ── Column widths ───────────────────────────────────────────────
            sheet.Column(1).Width = 38;  // KeyId (UUID)
            sheet.Column(2).Width = 40;  // KeyName
            sheet.Column(3).Width = 40;  // KeyDescription
            sheet.Column(4).Width = 12;  // MaxLength
            sheet.Column(5).Width = 66;  // SourceHash (SHA-256 hex)

            // Language columns — wider to show translation text
            for (int c = langStartCol; c < col; c++)
                sheet.Column(c).Width = 50;

            // Wrap text in translation columns so multi-line strings are readable
            for (int c = langStartCol; c < col; c++)
                sheet.Column(c).Style.Alignment.WrapText = true;

            // KeyId column: monospace-style by setting number format to text
            sheet.Column(1).Style.NumberFormat.Format = "@";

            // ── Conditional formatting: red background when LEN > MaxLength ─
            // Column D (col 4) holds MaxLength; 0 means no limit so we skip those.
            // For each language column we add one CF range covering all data rows.
            // The formula uses $D{row} (absolute column, relative row) so Excel
            // evaluates it per-row when applied across the range.
            if (row > 2) // only when there are data rows
            {
                int lastDataRow = row - 1;
                for (int langCol = langStartCol; langCol < col; langCol++)
                {
                    // Get the Excel column letter for the anchor cell of this range
                    string cellRef = XLHelper.GetColumnLetterFromNumber(langCol) + "2";

                    // Formula: cell is non-empty AND MaxLength > 0 AND LEN > MaxLength
                    string formula = $"AND($D2>0,LEN({cellRef})>$D2)";

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

        private static string FullKeyName(LocalizationKeyDto key, ProjectDto project)
        {
            CategoryDto? cat = project.Categories.Find(c => c.Id == key.CategoryId);
            if (cat == null) return key.KeyName;

            List<string> parts   = new List<string> { cat.Name };
            CategoryDto  current = cat;

            while (current.ParentCategoryId != null)
            {
                CategoryDto? parent = project.Categories.Find(c => c.Id == current.ParentCategoryId);
                if (parent == null) break;
                parts.Insert(0, parent.Name);
                current = parent;
            }

            return string.Join("/", parts) + "." + key.KeyName;
        }
    }
}