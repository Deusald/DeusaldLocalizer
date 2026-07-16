namespace DeusaldLocalizerWeb
{
    /// <summary>
    /// Host-specific bridge for saving a generated text file (e.g. the C# localization script):
    /// a save dialog on desktop, a browser download on the web. Keeps the shared export modal free
    /// of any platform file-dialog API, mirroring <see cref="IExcelInterop"/> for text content.
    /// </summary>
    public interface ITextFileExporter
    {
        /// <summary>
        /// Saves <paramref name="content"/> as a UTF-8 text file, offering <paramref name="suggestedFileName"/>
        /// as the default name (a save dialog on desktop, a download on the web).
        /// </summary>
        Task SaveTextAsync(string suggestedFileName, string content);
    }
}
