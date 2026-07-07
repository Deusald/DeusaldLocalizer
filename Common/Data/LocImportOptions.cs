namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// Toggles for <see cref="LocalizationImportService"/>. Everything except the
    /// translation cells themselves is opt-in — an import only touches keys/metadata
    /// the user explicitly asks it to.
    /// </summary>
    public class LocImportOptions
    {
        /// <summary>Create keys that are present in the sheet but missing from the project.</summary>
        public bool CreateNewKeys { get; set; }

        /// <summary>Move keys to the category encoded in their KeyName column, creating categories as needed.</summary>
        public bool UpdateCategories { get; set; }

        /// <summary>Overwrite key descriptions from the KeyDescription column.</summary>
        public bool UpdateDescriptions { get; set; }

        /// <summary>Overwrite key tags from the Tags column (requires the sheet to carry that column).</summary>
        public bool UpdateTags { get; set; }

        /// <summary>Overwrite key max length from the MaxLength column.</summary>
        public bool UpdateMaxLength { get; set; }

        /// <summary>Import the main/source-language column as well (off by default so the source stays a reference).</summary>
        public bool UpdateSourceText { get; set; }

        /// <summary>
        /// When true (default) changed cells are added as suggestions; when false the text is
        /// written directly onto the approved translation.
        /// </summary>
        public bool ImportAsSuggestions { get; set; } = true;
    }
}
