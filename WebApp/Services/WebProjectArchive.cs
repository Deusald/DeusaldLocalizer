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
    /// returns that location, or null if the user cancelled. Throws <see cref="ProjectFolderException"/>
    /// when the picked file is not a valid project export (so the caller can surface the reason).
    /// </summary>
    public async Task<string?> ImportAsync()
    {
        byte[]? zipBytes = await files.PickBytesAsync(".zip");
        if (zipBytes is null) return null; // cancelled — not an error

        // Read every file into memory first so we can locate metadata.json and normalise the layout before
        // writing anything. Rejecting an invalid zip then never leaves a half-written location behind.
        Dictionary<string, string> entries = new Dictionary<string, string>();
        using (MemoryStream buffer = new MemoryStream(zipBytes))
        await using (ZipArchive zip = new ZipArchive(buffer, ZipArchiveMode.Read))
        {
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                if (entry.FullName.EndsWith("/")) continue; // directory entry

                string path = entry.FullName.Replace('\\', '/');
                if (path.StartsWith("__MACOSX/", StringComparison.Ordinal)) continue; // macOS zip cruft

                using StreamReader reader = new StreamReader(await entry.OpenAsync(), Encoding.UTF8);
                entries[path] = await reader.ReadToEndAsync();
            }
        }

        // Accept metadata.json at the root, or one folder deep — some users zip the project *folder*
        // itself (e.g. "MyProject/metadata.json") rather than its contents. The common prefix is stripped
        // so every file lands at the root, which is where ProjectFileService.OpenAsync expects it.
        string? prefix = FindProjectPrefix(entries.Keys);
        if (prefix is null)
            throw new ProjectFolderException(
                $"No '{ProjectFileService.METADATA_FILE_NAME}' found in the zip — this does not look like a project export.");

        // Prefix so the imported project is scoped to the Localizer in the shared origin store (see WebProjectLocationService).
        string location = WebProjectLocationService.LocationPrefix + Guid.NewGuid().ToString("N");
        foreach (KeyValuePair<string, string> file in entries)
        {
            if (!file.Key.StartsWith(prefix, StringComparison.Ordinal)) continue; // outside the project folder
            string relative = file.Key.Substring(prefix.Length);
            if (relative.Length == 0) continue;
            await idb.PutAsync(location, relative, file.Value);
        }

        return location;
    }

    /// <summary>
    /// Returns the leading path to strip so <c>metadata.json</c> lands at the root, or null if none is present.
    /// Empty string means metadata.json is already at the root; otherwise it is the wrapping folder
    /// (e.g. <c>"MyProject/"</c>). The shallowest metadata.json wins, so a root file beats a nested one.
    /// </summary>
    private static string? FindProjectPrefix(IEnumerable<string> paths)
    {
        string  meta = ProjectFileService.METADATA_FILE_NAME;
        string? best = null;
        foreach (string path in paths)
        {
            string prefix;
            if (path == meta)
                prefix = string.Empty;
            else if (path.EndsWith("/" + meta, StringComparison.Ordinal))
                prefix = path.Substring(0, path.Length - meta.Length); // keeps the trailing '/'
            else
                continue;

            if (best is null || prefix.Length < best.Length) best = prefix;
        }
        return best;
    }
}
