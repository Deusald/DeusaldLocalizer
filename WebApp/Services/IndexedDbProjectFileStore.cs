using DeusaldLocalizerCommon;

namespace DeusaldLocalizerWeb;

/// <summary>
/// <see cref="IProjectFileStore"/> backed by IndexedDB, rooted at a single project <em>location</em>
/// handle. This is the browser analogue of <see cref="DiscProjectFileStore"/>: the same "folder of files"
/// layout, but records in IndexedDB instead of files on disk. Writes are atomic (one transaction each),
/// so no temp-file dance is needed and <see cref="ProjectFileService"/>'s logic runs unchanged.
/// </summary>
public sealed class IndexedDbProjectFileStore(IndexedDbInterop idb, string location) : IProjectFileStore
{
    public Task<bool> FileExistsAsync(string path) => idb.ExistsAsync(location, path);

    public Task<string?> ReadTextAsync(string path) => idb.GetAsync(location, path);

    public Task WriteTextAsync(string path, string content) => idb.PutAsync(location, path, content);

    public Task DeleteFileAsync(string path) => idb.DeleteAsync(location, path);

    public async Task<IReadOnlyList<string>> ListJsonFilesAsync(string folder) => await idb.ListJsonAsync(location, folder);
}