using System.Text;

namespace DeusaldLocalizerWeb;

/// <summary>
/// Web <see cref="ITextFileExporter"/>: saves generated text via a browser download, delegating the
/// byte transfer to <see cref="WebFileDownloadInterop"/>.
/// </summary>
public sealed class WebTextFileExporter(WebFileDownloadInterop files) : ITextFileExporter
{
    private const string _TEXT_MIME = "text/plain;charset=utf-8";

    public Task SaveTextAsync(string suggestedFileName, string content) =>
        files.SaveBytesAsync(suggestedFileName, Encoding.UTF8.GetBytes(content), _TEXT_MIME);
}
