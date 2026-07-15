namespace DeusaldLocalizerWeb;

/// <summary>
/// Web <see cref="IExcelInterop"/>: picks an <c>.xlsx</c> via a hidden file input and saves one via a
/// browser download, delegating the byte transfer to <see cref="WebFileDownloadInterop"/>.
/// </summary>
public sealed class WebExcelInterop(WebFileDownloadInterop files) : IExcelInterop
{
    private const string _XLSX_MIME = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public async Task<Stream?> PickXlsxForReadAsync()
    {
        byte[]? bytes = await files.PickBytesAsync(".xlsx");
        return bytes is null ? null : new MemoryStream(bytes);
    }

    public async Task SaveXlsxAsync(string suggestedFileName, Stream content)
    {
        using MemoryStream ms = new MemoryStream();
        await content.CopyToAsync(ms);
        await files.SaveBytesAsync(suggestedFileName, ms.ToArray(), _XLSX_MIME);
    }
}
