using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;

namespace DeusaldLocalizerCommon
{
    /// <summary>The kind of project mutation a single imported change represents.</summary>
    public enum ImportChangeKind
    {
        CategoryAdded,
        KeyAdded,
        KeyCategoryChanged,
        KeyDescriptionChanged,
        KeyMaxLengthChanged,
        TagAdded,
        TagRemoved,
        TranslationUpdated,
        SuggestionAdded
    }

    /// <summary>
    /// One mutation the import performed on the project, carrying just enough context for the
    /// caller to replay it through the matching ProjectState.Record… method (in list order).
    /// </summary>
    public class ImportedChange
    {
        public ImportChangeKind          Kind         { get; set; }
        public Guid                      KeyId        { get; set; }
        public LocLocalizationKey?       Key          { get; set; }
        public LocCategory?              Category     { get; set; }
        public string                    LanguageId   { get; set; } = string.Empty;
        public LocKeyTranslation?        Translation  { get; set; }
        public LocTranslationSuggestion? Suggestion   { get; set; }
        public string                    Tag          { get; set; } = string.Empty;
        public string?                   PrevDestHash { get; set; }
    }

    public class ImportResult
    {
        public int          KeysCreated         { get; set; }
        public int          KeysUpdated         { get; set; }
        public int          TranslationsUpdated { get; set; }
        public int          SuggestionsAdded    { get; set; }
        public int          CategoriesCreated   { get; set; }
        public int          KeysSkipped         { get; set; }
        public List<string> Warnings            { get; set; } = new();

        /// <summary>
        /// Every mutation this import applied to the project, in the order it must be recorded so
        /// categories land before the keys that reference them and keys before their translations.
        /// </summary>
        public List<ImportedChange> Changes { get; set; } = new();
    }

    /// <summary>
    /// Reads an xlsx file produced by <see cref="LocalizationExportService"/> and applies it to a
    /// project. Translation cells are always considered; everything else (creating keys, moving
    /// categories, updating descriptions/tags/max length, touching the source column) is opt-in via
    /// <see cref="LocImportOptions"/>.
    ///
    /// Rules:
    ///   - Matches rows by KeyId. Unknown/blank ids are skipped unless CreateNewKeys is set.
    ///   - Skips empty cells and cells that still hash to the "#hash" column written at export time
    ///     (the translator never touched them).
    ///   - Enforces the key's MaxLength; over-long cells are warned and skipped.
    ///   - The main/source-language column is ignored unless UpdateSourceText is set.
    ///   - Existing keys import as suggestions or direct text per ImportAsSuggestions; brand-new keys
    ///     always seed their translations directly (there is no baseline to propose against).
    /// </summary>
    public static class LocalizationImportService
    {
        /// <summary>
        /// Reads and applies the xlsx. <paramref name="onProgress"/>, when supplied, is invoked with a
        /// 0..1 fraction as rows are processed; the method yields after each report so a single-threaded
        /// (WASM) caller can repaint a progress bar between chunks of work.
        /// </summary>
        public static async Task<ImportResult> ImportFromStreamAsync(Stream stream, LocProject project, Guid authorId,
                                                                    LocImportOptions? options = null, Action<double>? onProgress = null)
        {
            options ??= new LocImportOptions();

            using XLWorkbook wb    = new XLWorkbook(stream);
            IXLWorksheet     sheet = wb.Worksheets.First();

            ImportContext ctx = new ImportContext
            {
                Project  = project,
                Sheet    = sheet,
                Options  = options,
                AuthorId = authorId,
                MainLang = project.Metadata.MainLanguageId,
                LangCols = new Dictionary<int, string>(),
                HashCols = new Dictionary<string, int>(),
                Changes  = new List<ImportedChange>(),
                Result   = new ImportResult()
            };

            // ── Read header row to discover column positions ────────────────
            foreach (IXLCell cell in sheet.Row(1).CellsUsed())
            {
                string header = cell.GetString().Trim();
                int    c      = cell.Address.ColumnNumber;

                switch (header)
                {
                    case "KeyId":
                        ctx.KeyIdCol = c;
                        continue;
                    case "KeyName":
                        ctx.KeyNameCol = c;
                        continue;
                    case "KeyDescription":
                        ctx.DescCol = c;
                        continue;
                    case "Tags":
                        ctx.TagsCol = c;
                        continue;
                    case "MaxLength":
                        ctx.MaxLenCol = c;
                        continue;
                    case "SourceHash": continue;
                }

                if (header.EndsWith(LocalizationExportService.HASH_HEADER_SUFFIX))
                {
                    string lang                                                       = header.Substring(0, header.Length - LocalizationExportService.HASH_HEADER_SUFFIX.Length);
                    if (project.Metadata.Languages.Contains(lang)) ctx.HashCols[lang] = c;
                }
                else if (project.Metadata.Languages.Contains(header))
                {
                    ctx.LangCols[c] = header;
                    if (header == ctx.MainLang) ctx.SourceCol = c;
                }
            }

            if (ctx.KeyIdCol <= 0)
            {
                ctx.Result.Warnings.Add("Could not find a 'KeyId' column — wrong file format?");
                return ctx.Result;
            }

            if (ctx.LangCols.Count == 0)
            {
                ctx.Result.Warnings.Add("No matching language columns found in this file.");
                return ctx.Result;
            }

            // ── Warn about options that need a column the sheet does not carry ──
            if ((options.CreateNewKeys || options.UpdateCategories) && ctx.KeyNameCol <= 0)
                ctx.Result.Warnings.Add("No 'KeyName' column — keys cannot be created or re-categorised.");
            if (options.UpdateDescriptions && ctx.DescCol <= 0)
                ctx.Result.Warnings.Add("No 'KeyDescription' column — descriptions left unchanged.");
            if (options.UpdateTags && ctx.TagsCol <= 0)
                ctx.Result.Warnings.Add("No 'Tags' column — tags left unchanged.");
            if (options.UpdateMaxLength && ctx.MaxLenCol <= 0)
                ctx.Result.Warnings.Add("No 'MaxLength' column — max length left unchanged.");
            if (options.UpdateSourceText && ctx.SourceCol <= 0)
                ctx.Result.Warnings.Add("No source-language column — source text left unchanged.");

            // ── Process data rows ───────────────────────────────────────────
            int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
            int total   = lastRow - 1;
            int chunk   = total > 0 ? Math.Max(1, total / 100) : 1;
            for (int row = 2; row <= lastRow; row++)
            {
                ProcessRow(ctx, row);
                if (onProgress != null && (row - 2) % chunk == 0)
                {
                    onProgress((double)(row - 1) / lastRow);
                    await Task.Yield();
                }
            }
            onProgress?.Invoke(1.0);

            ctx.Result.Changes = ctx.Changes;
            return ctx.Result;
        }

        private static void ProcessRow(ImportContext ctx, int row)
        {
            string rawId = ctx.Sheet.Cell(row, ctx.KeyIdCol).GetString().Trim();

            if (string.IsNullOrEmpty(rawId))
            {
                if (!ctx.Options.CreateNewKeys) return; // legacy: blank-id rows are ignored
                BuildNewKey(ctx, row, Guid.NewGuid());
                return;
            }

            if (!Guid.TryParse(rawId, out var keyId))
            {
                ctx.Result.Warnings.Add($"Row {row}: invalid KeyId '{rawId}', skipped.");
                ctx.Result.KeysSkipped++;
                return;
            }

            LocLocalizationKey? key = ctx.Project.Keys.Find(k => k.Id == keyId);
            if (key == null)
            {
                if (!ctx.Options.CreateNewKeys)
                {
                    ctx.Result.KeysSkipped++;
                    return;
                }
                BuildNewKey(ctx, row, keyId);
                return;
            }

            UpdateExistingKey(ctx, row, key);
        }

        private static void UpdateExistingKey(ImportContext ctx, int row, LocLocalizationKey key)
        {
            bool touched = UpdateKeyMetadata(ctx, row, key);

            string newSourceHash     = string.Empty;
            bool   sourceTextChanged = false;
            if (ctx.Options.UpdateSourceText && ctx.SourceCol > 0)
            {
                (bool srcTouched, bool textChanged, string newHash) =  ApplySourceCell(ctx, row, key);
                touched                                             |= srcTouched;
                sourceTextChanged                                   =  textChanged;
                newSourceHash                                       =  newHash;
            }

            foreach (KeyValuePair<int, string> langEntry in ctx.LangCols)
            {
                if (langEntry.Key == ctx.SourceCol) continue;
                touched |= ApplyTranslationCell(ctx, row, key, langEntry.Key, langEntry.Value);
            }

            // Re-flag the other languages against the new source only after the fresh
            // translations are in place, so a language imported this pass doesn't get
            // a redundant "source changed" entry.
            if (sourceTextChanged)
                touched |= CascadeSourceChanged(ctx, key, newSourceHash);

            if (touched)
            {
                key.UpdatedAt = DateTime.UtcNow;
                ctx.Result.KeysUpdated++;
            }
        }

        private static bool UpdateKeyMetadata(ImportContext ctx, int row, LocLocalizationKey key)
        {
            bool touched = false;

            if (ctx.Options.UpdateCategories && ctx.KeyNameCol > 0)
            {
                string fullName = ctx.Sheet.Cell(row, ctx.KeyNameCol).GetString().Trim();
                if (fullName.Length > 0)
                {
                    (string categoryPath, _) = ParseFullKeyName(fullName);
                    Guid categoryId = ResolveCategoryId(ctx, categoryPath, createIfMissing: true);
                    if (key.CategoryId != categoryId)
                    {
                        key.CategoryId = categoryId;
                        ctx.Changes.Add(new ImportedChange
                        {
                            Kind = ImportChangeKind.KeyCategoryChanged, KeyId = key.Id, Key = key
                        });
                        touched = true;
                    }
                }
            }

            if (ctx.Options.UpdateDescriptions && ctx.DescCol > 0)
            {
                string desc = ctx.Sheet.Cell(row, ctx.DescCol).GetString();
                if (key.Description != desc)
                {
                    key.Description = desc;
                    ctx.Changes.Add(new ImportedChange
                    {
                        Kind = ImportChangeKind.KeyDescriptionChanged, KeyId = key.Id, Key = key
                    });
                    touched = true;
                }
            }

            if (ctx.Options.UpdateMaxLength && ctx.MaxLenCol > 0)
            {
                int newMax = ReadMaxLength(ctx, row);
                if (key.MaxLength != newMax)
                {
                    key.MaxLength = newMax;
                    ctx.Changes.Add(new ImportedChange
                    {
                        Kind = ImportChangeKind.KeyMaxLengthChanged, KeyId = key.Id, Key = key
                    });
                    touched = true;
                }
            }

            if (ctx.Options.UpdateTags && ctx.TagsCol > 0)
            {
                List<string> desired = ParseTags(ctx.Sheet.Cell(row, ctx.TagsCol).GetString());

                foreach (string existing in key.Tags.ToList())
                {
                    if (!desired.Contains(existing, StringComparer.OrdinalIgnoreCase))
                    {
                        key.Tags.Remove(existing);
                        ctx.Changes.Add(new ImportedChange { Kind = ImportChangeKind.TagRemoved, KeyId = key.Id, Tag = existing });
                        touched = true;
                    }
                }

                foreach (string tag in desired)
                {
                    if (!key.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    {
                        key.Tags.Add(tag);
                        ctx.Changes.Add(new ImportedChange { Kind = ImportChangeKind.TagAdded, KeyId = key.Id, Tag = tag });
                        touched = true;
                    }
                }
            }

            return touched;
        }

        /// <summary>Applies a non-source translation cell as a suggestion or as direct text.</summary>
        private static bool ApplyTranslationCell(ImportContext ctx, int row, LocLocalizationKey key,
                                                 int col, string langCode)
        {
            string text = ctx.Sheet.Cell(row, col).GetString();
            if (string.IsNullOrEmpty(text)) return false;
            if (UnchangedSinceExport(ctx, row, langCode, text)) return false;

            if (key.MaxLength != 0 && text.Length > key.MaxLength)
            {
                ctx.Result.Warnings.Add($"Row {row}: '{langCode}' exceeds max length {key.MaxLength}, skipped.");
                return false;
            }

            LocKeyTranslation translation = key.Translations.Find(t => t.LanguageId == langCode)
                                         ?? AddTranslation(key, langCode);

            if (ctx.Options.ImportAsSuggestions)
            {
                if (translation.Text == text) return false;
                if (translation.Suggestions.Any(s => s.Text == text)) return false;

                LocKeyTranslation? src = key.Translations.Find(t => t.LanguageId == ctx.MainLang);
                LocTranslationSuggestion suggestion = new LocTranslationSuggestion
                {
                    Text       = text,
                    AuthorId   = ctx.AuthorId,
                    SourceHash = TextHashHelper.Compute(src?.Text ?? string.Empty)
                };
                translation.Suggestions.Add(suggestion);
                ctx.Changes.Add(new ImportedChange
                {
                    Kind = ImportChangeKind.SuggestionAdded, KeyId = key.Id, LanguageId = langCode, Suggestion = suggestion
                });
                ctx.Result.SuggestionsAdded++;
                return true;
            }

            if (translation.Text == text) return false;

            string             oldDestHash   = TextHashHelper.Compute(translation.Text);
            LocKeyTranslation? currentSource = key.Translations.Find(t => t.LanguageId == ctx.MainLang);

            translation.Text          = text;
            translation.UpdatedAt     = DateTime.UtcNow;
            translation.SourceChanged = false;
            translation.BaseTextHash  = TextHashHelper.Compute(currentSource?.Text ?? string.Empty);
            translation.Status        = TranslationStatus.Approved;

            ctx.Changes.Add(new ImportedChange
            {
                Kind        = ImportChangeKind.TranslationUpdated, KeyId = key.Id, LanguageId = langCode,
                Translation = translation, PrevDestHash                  = oldDestHash
            });
            ctx.Result.TranslationsUpdated++;
            return true;
        }

        /// <summary>Applies the source-language cell. Returns whether it changed and the new source hash (direct mode only).</summary>
        private static (bool touched, bool textChanged, string newHash) ApplySourceCell(ImportContext ctx, int row, LocLocalizationKey key)
        {
            string text = ctx.Sheet.Cell(row, ctx.SourceCol).GetString();
            if (string.IsNullOrEmpty(text)) return (false, false, string.Empty);
            if (UnchangedSinceExport(ctx, row, ctx.MainLang, text)) return (false, false, string.Empty);

            if (key.MaxLength != 0 && text.Length > key.MaxLength)
            {
                ctx.Result.Warnings.Add($"Row {row}: source exceeds max length {key.MaxLength}, skipped.");
                return (false, false, string.Empty);
            }

            LocKeyTranslation source = key.Translations.Find(t => t.LanguageId == ctx.MainLang)
                                    ?? AddTranslation(key, ctx.MainLang);

            if (ctx.Options.ImportAsSuggestions)
            {
                if (source.Text == text || source.Suggestions.Any(s => s.Text == text)) return (false, false, string.Empty);

                LocTranslationSuggestion suggestion = new LocTranslationSuggestion
                {
                    Text = text, AuthorId = ctx.AuthorId, SourceHash = TextHashHelper.Compute(source.Text)
                };
                source.Suggestions.Add(suggestion);
                ctx.Changes.Add(new ImportedChange
                {
                    Kind = ImportChangeKind.SuggestionAdded, KeyId = key.Id, LanguageId = ctx.MainLang, Suggestion = suggestion
                });
                ctx.Result.SuggestionsAdded++;
                return (true, false, string.Empty);
            }

            if (source.Text == text) return (false, false, string.Empty);

            string oldHash = TextHashHelper.Compute(source.Text);
            string newHash = TextHashHelper.Compute(text);

            source.Text         = text;
            source.UpdatedAt    = DateTime.UtcNow;
            source.Status       = TranslationStatus.Approved;
            source.BaseTextHash = newHash;

            ctx.Changes.Add(new ImportedChange
            {
                Kind        = ImportChangeKind.TranslationUpdated, KeyId = key.Id, LanguageId = ctx.MainLang,
                Translation = source, PrevDestHash                       = oldHash
            });
            ctx.Result.TranslationsUpdated++;
            return (true, oldHash != newHash, newHash);
        }

        /// <summary>After the source text changed, flip SourceChanged on every other language whose baseline drifted.</summary>
        private static bool CascadeSourceChanged(ImportContext ctx, LocLocalizationKey key, string newHash)
        {
            bool touched = false;
            foreach (LocKeyTranslation other in key.Translations)
            {
                if (other.LanguageId == ctx.MainLang) continue;
                if (string.IsNullOrEmpty(other.BaseTextHash)) continue;

                bool sourceChanged = other.BaseTextHash != newHash;
                if (other.SourceChanged != sourceChanged)
                {
                    other.SourceChanged = sourceChanged;
                    ctx.Changes.Add(new ImportedChange
                    {
                        Kind       = ImportChangeKind.TranslationUpdated, KeyId = key.Id,
                        LanguageId = other.LanguageId, Translation              = other
                    });
                    touched = true;
                }
            }
            return touched;
        }

        private static void BuildNewKey(ImportContext ctx, int row, Guid keyId)
        {
            if (ctx.KeyNameCol <= 0)
            {
                ctx.Result.Warnings.Add($"Row {row}: cannot create key without a 'KeyName' column.");
                ctx.Result.KeysSkipped++;
                return;
            }

            (string categoryPath, string keyName) = ParseFullKeyName(ctx.Sheet.Cell(row, ctx.KeyNameCol).GetString().Trim());
            if (keyName.Length == 0)
            {
                ctx.Result.Warnings.Add($"Row {row}: cannot create a key with an empty name.");
                ctx.Result.KeysSkipped++;
                return;
            }

            LocLocalizationKey key = new LocLocalizationKey
            {
                Id         = keyId,
                CategoryId = ResolveCategoryId(ctx, categoryPath, createIfMissing: ctx.Options.UpdateCategories),
                KeyName    = keyName
            };

            if (ctx.DescCol > 0) key.Description = ctx.Sheet.Cell(row, ctx.DescCol).GetString();
            if (ctx.MaxLenCol > 0) key.MaxLength = ReadMaxLength(ctx, row);
            if (ctx.TagsCol > 0) key.Tags        = ParseTags(ctx.Sheet.Cell(row, ctx.TagsCol).GetString());

            // Seed the source first so the other languages can base their hash on it.
            string sourceText = ctx.SourceCol > 0 ? ctx.Sheet.Cell(row, ctx.SourceCol).GetString() : string.Empty;
            string sourceHash = TextHashHelper.Compute(sourceText);
            AddSeedTranslation(ctx, row, key, ctx.MainLang, sourceText, sourceHash);

            foreach (KeyValuePair<int, string> langEntry in ctx.LangCols)
            {
                if (langEntry.Key == ctx.SourceCol) continue;
                AddSeedTranslation(ctx, row, key, langEntry.Value, ctx.Sheet.Cell(row, langEntry.Key).GetString(), sourceHash);
            }

            ctx.Project.Keys.Add(key);
            ctx.Changes.Add(new ImportedChange { Kind = ImportChangeKind.KeyAdded, KeyId = key.Id, Key = key });
            ctx.Result.KeysCreated++;
        }

        /// <summary>Adds an approved translation to a brand-new key, honouring the key's max length.</summary>
        private static void AddSeedTranslation(ImportContext ctx, int row, LocLocalizationKey key,
                                               string langCode, string text, string sourceHash)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (key.MaxLength != 0 && text.Length > key.MaxLength)
            {
                ctx.Result.Warnings.Add($"Row {row}: '{langCode}' exceeds max length {key.MaxLength}, skipped.");
                return;
            }

            key.Translations.Add(new LocKeyTranslation
            {
                LanguageId   = langCode,
                Text         = text,
                Status       = TranslationStatus.Approved,
                BaseTextHash = langCode == ctx.MainLang ? TextHashHelper.Compute(text) : sourceHash
            });
        }

        /// <summary>Resolves a "cat/sub" path to a category id, creating the chain when allowed. Empty path / unresolved = root.</summary>
        private static Guid ResolveCategoryId(ImportContext ctx, string categoryPath, bool createIfMissing)
        {
            if (string.IsNullOrWhiteSpace(categoryPath)) return Guid.Empty;

            Guid? parentId = null;
            foreach (string rawSegment in categoryPath.Split('/'))
            {
                string segment = rawSegment.Trim();
                if (segment.Length == 0) continue;

                LocCategory? match = ctx.Project.Categories.Find(c =>
                    c.ParentCategoryId == parentId && string.Equals(c.Name, segment, StringComparison.Ordinal));

                if (match == null)
                {
                    if (!createIfMissing) return Guid.Empty;

                    match = new LocCategory { Name = segment, ParentCategoryId = parentId };
                    ctx.Project.Categories.Add(match);
                    ctx.Changes.Add(new ImportedChange { Kind = ImportChangeKind.CategoryAdded, Category = match });
                    ctx.Result.CategoriesCreated++;
                }

                parentId = match.Id;
            }

            return parentId ?? Guid.Empty;
        }

        private static bool UnchangedSinceExport(ImportContext ctx, int row, string langCode, string text)
        {
            if (!ctx.HashCols.TryGetValue(langCode, out int hashCol)) return false;
            string exportedHash = ctx.Sheet.Cell(row, hashCol).GetString().Trim();
            return exportedHash.Length > 0 && TextHashHelper.Compute(text) == exportedHash;
        }

        private static LocKeyTranslation AddTranslation(LocLocalizationKey key, string langCode)
        {
            LocKeyTranslation translation = new LocKeyTranslation { LanguageId = langCode };
            key.Translations.Add(translation);
            return translation;
        }

        private static int ReadMaxLength(ImportContext ctx, int row)
        {
            string raw = ctx.Sheet.Cell(row, ctx.MaxLenCol).GetString().Trim();
            if (raw.Length == 0) return 0;
            return int.TryParse(raw, out int value) && value > 0 ? value : 0;
        }

        private static List<string> ParseTags(string raw) =>
            raw.Split(',')
               .Select(t => t.Trim())
               .Where(t => t.Length > 0)
               .Distinct(StringComparer.OrdinalIgnoreCase)
               .ToList();

        private static (string categoryPath, string keyName) ParseFullKeyName(string full)
        {
            int dot = full.LastIndexOf('.');
            return dot < 0
                       ? (string.Empty, full.Trim())
                       : (full.Substring(0, dot).Trim(), full.Substring(dot + 1).Trim());
        }

        /// <summary>Mutable per-import scratch space, so the many helpers don't each need a dozen parameters.</summary>
        private sealed class ImportContext
        {
            public LocProject       Project = null!;
            public IXLWorksheet     Sheet   = null!;
            public LocImportOptions Options = null!;
            public Guid             AuthorId;
            public string           MainLang = string.Empty;

            public int KeyIdCol;
            public int KeyNameCol;
            public int DescCol;
            public int TagsCol;
            public int MaxLenCol;
            public int SourceCol;

            public Dictionary<int, string> LangCols = null!;
            public Dictionary<string, int> HashCols = null!;
            public List<ImportedChange>    Changes  = null!;
            public ImportResult            Result   = null!;
        }
    }
}