using System.IO;
using System.Threading.Tasks;

namespace DeusaldLocalizerWeb;

/// <summary>
/// Web <see cref="IExcelInterop"/>: picks an <c>.xlsx</c> via a hidden file input and saves one via a
/// browser download, delegating the byte transfer to <see cref="WebFileDownloadInterop"/>.
/// </summary>
public sealed class WebExcelInterop : IExcelInterop
{
    private const string _XLSX_MIME = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly WebFileDownloadInterop _Files;

    public WebExcelInterop(WebFileDownloadInterop files) => _Files = files;

    public async Task<Stream?> PickXlsxForReadAsync()
    {
        byte[]? bytes = await _Files.PickBytesAsync();
        return bytes is null ? null : new MemoryStream(bytes);
    }

    public async Task SaveXlsxAsync(string suggestedFileName, Stream content)
    {
        using MemoryStream ms = new MemoryStream();
        await content.CopyToAsync(ms);
        await _Files.SaveBytesAsync(suggestedFileName, ms.ToArray(), _XLSX_MIME);
    }
}
