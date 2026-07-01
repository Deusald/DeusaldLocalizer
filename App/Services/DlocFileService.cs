using System.IO.Compression;
using DeusaldLocalizerCommon;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace App;

/// <summary>
/// Handles reading and writing the .dloc project file format.
///
/// A .dloc file is a ZIP archive with a changed extension containing:
///   manifest.json   — file format version and project metadata summary
///   project.json    — ProjectDto (members, languages, categories)
///   keys/           — one JSON file per LocalizationKeyDto, named by GUID
///                     e.g.  keys/3fa85f64-5717-4562-b3fc-2c963f66afa6.json
///
/// Splitting keys into individual files keeps diffs readable in version control
/// and avoids loading everything into memory at once for large projects.
/// </summary>
[PublicAPI]
public static class DlocFileService
{
    // ── Constants ────────────────────────────────────────────────────────────

    public const string FILE_EXTENSION         = ".dloc";
    public const int    CURRENT_FORMAT_VERSION = 1;

    private static readonly JsonSerializerSettings _JsonSettings = new()
    {
        Formatting        = Formatting.Indented,
        NullValueHandling = NullValueHandling.Include,
        Converters        = { new StringEnumConverter() },
    };

    // ── Load ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads a .dloc file from disk and returns the fully hydrated ProjectDto.
    /// Throws <see cref="DlocFormatException"/> on structural errors.
    /// </summary>
    public static async Task<ProjectDto> LoadAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Project file not found.", filePath);

        await using FileStream fileStream = File.OpenRead(filePath);
        return await LoadFromStreamAsync(fileStream);
    }

    /// <summary>Loads a .dloc from any readable stream (useful for testing).</summary>
    public static async Task<ProjectDto> LoadFromStreamAsync(Stream stream)
    {
        await using ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        // 1. Read and validate manifest
        DlocManifest? manifest = await ReadEntryAsync<DlocManifest>(archive, "manifest.json");
        if (manifest == null)
            throw new DlocFormatException("manifest.json is missing or empty.");

        if (manifest.FormatVersion > CURRENT_FORMAT_VERSION)
            throw new DlocFormatException(
                $"File was created with format version {manifest.FormatVersion}, " +
                $"but this application supports up to version {CURRENT_FORMAT_VERSION}.");

        // 2. Read project (without keys — keys are stored separately)
        ProjectDto? project = await ReadEntryAsync<ProjectDto>(archive, "project.json");
        if (project == null)
            throw new DlocFormatException("project.json is missing or empty.");

        // 3. Read individual key files
        project.Keys = new List<LocalizationKeyDto>();
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith("keys/", StringComparison.OrdinalIgnoreCase)
             || !entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;

            LocalizationKeyDto? key = await ReadEntryAsync<LocalizationKeyDto>(archive, entry.FullName);
            if (key != null) project.Keys.Add(key);
        }

        return project;
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    /// <summary>Saves the project to a .dloc file at the given path.</summary>
    public static async Task SaveAsync(ProjectDto project, string filePath)
    {
        project.UpdatedAt     = DateTime.UtcNow;
        project.FormatVersion = CURRENT_FORMAT_VERSION;

        // Write to a temp file first so a failed save doesn't corrupt an existing file
        string tempPath = filePath + ".tmp";
        try
        {
            await using (FileStream fileStream = File.Create(tempPath))
                await SaveToStreamAsync(project, fileStream);

            if (File.Exists(filePath))
                File.Delete(filePath);
            File.Move(tempPath, filePath);
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }
    }

    /// <summary>Saves the project to any writable stream.</summary>
    public static async Task SaveToStreamAsync(ProjectDto project, Stream stream)
    {
        await using ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        // 1. Manifest
        DlocManifest manifest = new DlocManifest
        {
            FormatVersion = CURRENT_FORMAT_VERSION,
            ProjectId     = project.Id,
            ProjectName   = project.Name,
            KeyCount      = project.Keys.Count,
            SavedAt       = DateTime.UtcNow,
        };
        await WriteEntryAsync(archive, "manifest.json", manifest);

        // 2. Project without keys (keys are stored as individual files)
        List<LocalizationKeyDto> keysBackup = project.Keys;
        project.Keys = new List<LocalizationKeyDto>();
        await WriteEntryAsync(archive, "project.json", project);
        project.Keys = keysBackup;

        // 3. Individual key files
        foreach (LocalizationKeyDto key in project.Keys)
            await WriteEntryAsync(archive, $"keys/{key.Id}.json", key);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<T?> ReadEntryAsync<T>(ZipArchive archive, string entryName) where T : class
    {
        ZipArchiveEntry? entry = archive.GetEntry(entryName);
        if (entry == null) return null;

        await using Stream entryStream = await entry.OpenAsync();
        using StreamReader reader      = new StreamReader(entryStream);
        string             json        = await reader.ReadToEndAsync();
        return JsonConvert.DeserializeObject<T>(json, _JsonSettings);
    }

    private static async Task WriteEntryAsync<T>(ZipArchive archive, string entryName, T value)
    {
        ZipArchiveEntry          entry       = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using Stream       entryStream = await entry.OpenAsync();
        await using StreamWriter writer      = new StreamWriter(entryStream);
        string                   json        = JsonConvert.SerializeObject(value, _JsonSettings);
        await writer.WriteAsync(json);
    }
}

// ── Supporting types ─────────────────────────────────────────────────────────

/// <summary>Stored as manifest.json inside every .dloc archive.</summary>
[PublicAPI]
public class DlocManifest
{
    public int      FormatVersion { get; set; }
    public Guid     ProjectId     { get; set; }
    public string   ProjectName   { get; set; } = string.Empty;
    public int      KeyCount      { get; set; }
    public DateTime SavedAt       { get; set; }
}

/// <summary>Thrown when a .dloc file has an unrecognised or invalid structure.</summary>
public class DlocFormatException(string message) : Exception(message);