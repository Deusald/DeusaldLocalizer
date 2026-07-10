using System.IO.Compression;
using System.Text;
using DeusaldLocalizerCommon;

namespace DeusaldLocalizerWeb;

/// <summary>
/// Exports an IndexedDB-stored project to a downloadable <c>.zip</c> and imports such a zip back into a
/// fresh IndexedDB location. The zip <em>is</em> a project folder — the same JSON files at the same
/// relative paths — so it round-trips with the desktop app (unzip and open, or zip and import).
/// This is the web client's "save a local copy" and the only durable backup for offline projects.
/// </summary>
public sealed class WebProjectArchive(IndexedDbInterop idb, WebFileDownloadInterop files)
{
    private const string _ZIP_MIME = "application/zip";

    /// <summary>Zips every file at <paramref name="location"/> and triggers a browser download.</summary>
    public async Task ExportAsync(string location, string suggestedFileName)
    {
        byte[] zipBytes;
        using (MemoryStream buffer = new MemoryStream())
        {
            await using (ZipArchive zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (string path in await idb.ListAllAsync(location))
                {
                    string? content = await idb.GetAsync(location, path);
                    if (content is null) continue;

                    ZipArchiveEntry    entry       = zip.CreateEntry(path, CompressionLevel.Optimal);
                    await using Stream entryStream = await entry.OpenAsync();
                    byte[]             data        = Encoding.UTF8.GetBytes(content);
                    await entryStream.WriteAsync(data, 0, data.Length);
                }
            }
            zipBytes = buffer.ToArray();
        }

        await files.SaveBytesAsync(suggestedFileName, zipBytes, _ZIP_MIME);
    }

    /// <summary>
    /// Prompts the user to pick a project <c>.zip</c>, extracts it into a brand-new IndexedDB location and
    /// returns that location, or null if the user cancelled or the zip held no metadata.
    /// </summary>
    public async Task<string?> ImportAsync()
    {
        byte[]? zipBytes = await files.PickBytesAsync();
        if (zipBytes is null) return null;

        // Prefix so the imported project is scoped to the Localizer in the shared origin store (see WebProjectLocationService).
        string location  = WebProjectLocationService.LocationPrefix + Guid.NewGuid().ToString("N");
        bool   hasMeta   = false;

        using (MemoryStream buffer = new MemoryStream(zipBytes))
        await using (ZipArchive zip = new ZipArchive(buffer, ZipArchiveMode.Read))
        {
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                if (entry.FullName.EndsWith("/")) continue; // directory entry

                string             path    = entry.FullName.Replace('\\', '/');
                using StreamReader reader  = new StreamReader(await entry.OpenAsync(), Encoding.UTF8);
                string             content = await reader.ReadToEndAsync();

                await idb.PutAsync(location, path, content);
                if (path == ProjectFileService.METADATA_FILE_NAME) hasMeta = true;
            }
        }

        if (!hasMeta)
        {
            // Not a project zip — clean up the partial import so it never shows in the project list.
            await idb.DeleteLocationAsync(location);
            return null;
        }

        return location;
    }
}
