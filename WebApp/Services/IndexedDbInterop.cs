using Microsoft.JSInterop;

namespace DeusaldLocalizerWeb;

/// <summary>
/// Thin typed wrapper over <c>wwwroot/js/idb.js</c>: the IndexedDB key/value store that backs the web
/// client's projects. Registered as a scoped service; the JS module is imported lazily on first use.
/// </summary>
public sealed class IndexedDbInterop(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _Module;

    private async ValueTask<IJSObjectReference> ModuleAsync() =>
        _Module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/idb.js");

    public async Task<string?> GetAsync(string location, string path) =>
        await (await ModuleAsync()).InvokeAsync<string?>("get", location, path);

    public async Task<bool> ExistsAsync(string location, string path) =>
        await (await ModuleAsync()).InvokeAsync<bool>("exists", location, path);

    public async Task PutAsync(string location, string path, string content) =>
        await (await ModuleAsync()).InvokeVoidAsync("put", location, path, content);

    public async Task DeleteAsync(string location, string path) =>
        await (await ModuleAsync()).InvokeVoidAsync("del", location, path);

    public async Task<string[]> ListJsonAsync(string location, string folder) =>
        await (await ModuleAsync()).InvokeAsync<string[]>("listJson", location, folder);

    public async Task<string[]> ListAllAsync(string location) =>
        await (await ModuleAsync()).InvokeAsync<string[]>("listAll", location);

    public async Task<string[]> ListLocationsAsync() =>
        await (await ModuleAsync()).InvokeAsync<string[]>("listLocations");

    public async Task DeleteLocationAsync(string location) =>
        await (await ModuleAsync()).InvokeVoidAsync("deleteLocation", location);

    /// <summary>Asks the browser to make this origin's storage durable so pending/offline work is not evicted.</summary>
    public async Task<bool> PersistAsync() =>
        await (await ModuleAsync()).InvokeAsync<bool>("persist");

    public async ValueTask DisposeAsync()
    {
        if (_Module is not null)
            await _Module.DisposeAsync();
    }
}