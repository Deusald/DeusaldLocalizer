using System.IO;
using System.Threading.Tasks;

namespace DeusaldLocalizerWeb
{
    /// <summary>
    /// Host-specific bridge for the Excel import/export modals: on desktop it uses the MAUI file
    /// picker / file saver, on the web an <c>&lt;input type="file"&gt;</c> and a browser download. Keeps
    /// the shared modals free of any platform file-dialog API.
    /// </summary>
    public interface IExcelInterop
    {
        /// <summary>
        /// Prompts the user to choose an <c>.xlsx</c> file and returns a readable stream, or null if the
        /// user cancels. The caller owns the returned stream and disposes it.
        /// </summary>
        Task<Stream?> PickXlsxForReadAsync();

        /// <summary>
        /// Saves <paramref name="content"/> as an <c>.xlsx</c> file, offering <paramref name="suggestedFileName"/>
        /// as the default name (a save dialog on desktop, a download on the web).
        /// </summary>
        Task SaveXlsxAsync(string suggestedFileName, Stream content);
    }
}
