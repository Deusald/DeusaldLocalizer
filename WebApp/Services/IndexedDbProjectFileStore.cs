using System.Collections.Generic;
using System.Threading.Tasks;
using DeusaldLocalizerCommon;

namespace DeusaldLocalizerWeb;

/// <summary>
/// <see cref="IProjectFileStore"/> backed by IndexedDB, rooted at a single project <em>location</em>
/// handle. This is the browser analogue of <see cref="DiscProjectFileStore"/>: the same "folder of files"
/// layout, but records in IndexedDB instead of files on disk. Writes are atomic (one transaction each),
/// so no temp-file dance is needed and <see cref="ProjectFileService"/>'s logic runs unchanged.
/// </summary>
public sealed class IndexedDbProjectFileStore : IProjectFileStore
{
    private readonly IndexedDbInterop _Idb;
    private readonly string           _Location;

    public IndexedDbProjectFileStore(IndexedDbInterop idb, string location)
    {
        _Idb      = idb;
        _Location = location;
    }

    public Task<bool> FileExistsAsync(string path) => _Idb.ExistsAsync(_Location, path);

    public Task<string?> ReadTextAsync(string path) => _Idb.GetAsync(_Location, path);

    public Task WriteTextAsync(string path, string content) => _Idb.PutAsync(_Location, path, content);

    public Task DeleteFileAsync(string path) => _Idb.DeleteAsync(_Location, path);

    public async Task<IReadOnlyList<string>> ListJsonFilesAsync(string folder) =>
        await _Idb.ListJsonAsync(_Location, folder);
}
