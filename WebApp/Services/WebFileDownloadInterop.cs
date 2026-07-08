using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace DeusaldLocalizerWeb;

/// <summary>
/// Shared bridge to <c>wwwroot/js/excel.js</c>: pick a file as bytes, or trigger a browser download.
/// Reused by the Excel interop and the project zip export/import.
/// </summary>
public sealed class WebFileDownloadInterop : IAsyncDisposable
{
    private readonly IJSRuntime          _Js;
    private          IJSObjectReference? _Module;

    public WebFileDownloadInterop(IJSRuntime js) => _Js = js;

    private async ValueTask<IJSObjectReference> ModuleAsync() =>
        _Module ??= await _Js.InvokeAsync<IJSObjectReference>("import", "./js/excel.js");

    /// <summary>Prompts the user to pick a file and returns its bytes, or null if cancelled.</summary>
    public async Task<byte[]?> PickBytesAsync()
    {
        string? base64 = await (await ModuleAsync()).InvokeAsync<string?>("pickXlsx");
        return base64 is null ? null : Convert.FromBase64String(base64);
    }

    /// <summary>Triggers a browser download of <paramref name="bytes"/> as <paramref name="fileName"/>.</summary>
    public async Task SaveBytesAsync(string fileName, byte[] bytes, string mimeType)
    {
        string base64 = Convert.ToBase64String(bytes);
        await (await ModuleAsync()).InvokeVoidAsync("saveBytes", fileName, base64, mimeType);
    }

    public async ValueTask DisposeAsync()
    {
        if (_Module is not null)
            await _Module.DisposeAsync();
    }
}
