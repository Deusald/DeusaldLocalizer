using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// Reads and writes a localization project stored as a folder of JSON files.
    ///
    /// Expected folder structure:
    ///   metadata.json
    ///   Members/              {guid}.json  per LocProjectMember
    ///   Categories/           {guid}.json  per LocCategory
    ///   Enums/                {guid}.json  per LocEnum
    ///   UncommittedChanges/   0000.json, 0001.json … (ordered, zero-padded)
    ///   Keys/                 {guid}.json  per LocLocalizationKey
    /// </summary>
    [PublicAPI]
    public static class ProjectFileService
    {
        // ── Constants ─────────────────────────────────────────────────────────

        public const string METADATA_FILE_NAME         = "metadata.json";
        public const string MEMBERS_FOLDER             = "Members";
        public const string CATEGORIES_FOLDER          = "Categories";
        public const string ENUMS_FOLDER               = "Enums";
        public const string UNCOMMITTED_CHANGES_FOLDER = "UncommittedChanges";
        public const string KEYS_FOLDER                = "Keys";
        public const int    CURRENT_FORMAT_VERSION     = 1;

        private static readonly JsonSerializerSettings _JsonSettings = new()
        {
            Formatting        = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include,
            Converters        = { new StringEnumConverter() },
        };

        // ── Open ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Opens and validates a project folder, returning a fully hydrated LocProject.
        /// Throws <see cref="ProjectFolderException"/> on any structural or version error.
        /// </summary>
        public static async Task<LocProject> OpenAsync(string folderPath)
        {
            // ── Validate folder ────────────────────────────────────────────────
            if (!Directory.Exists(folderPath))
                throw new ProjectFolderException($"Folder does not exist: {folderPath}");

            string metadataPath = Path.Combine(folderPath, METADATA_FILE_NAME);
            if (!File.Exists(metadataPath))
                throw new ProjectFolderException(
                    $"'{METADATA_FILE_NAME}' not found — this does not appear to be a valid project folder.");

            // ── Read metadata ──────────────────────────────────────────────────
            LocProjectMetadata metadata = await ReadJsonAsync<LocProjectMetadata>(metadataPath)
                                       ?? throw new ProjectFolderException($"'{METADATA_FILE_NAME}' is empty or malformed.");

            if (metadata.FormatVersion > CURRENT_FORMAT_VERSION)
                throw new ProjectFolderException(
                    $"Project uses format version {metadata.FormatVersion} but this application " +
                    $"only supports up to version {CURRENT_FORMAT_VERSION}. Please update the application.");

            if (metadata.Id == Guid.Empty)
                throw new ProjectFolderException($"'{METADATA_FILE_NAME}' contains an invalid project Id.");

            if (string.IsNullOrWhiteSpace(metadata.MainLanguageId))
                throw new ProjectFolderException($"'{METADATA_FILE_NAME}' is missing MainLanguageId.");

            // ── Read sub-folders ───────────────────────────────────────────────
            List<LocProjectMember>   members    = await ReadFolderAsync<LocProjectMember>(folderPath, MEMBERS_FOLDER);
            List<LocCategory>        categories = await ReadFolderAsync<LocCategory>(folderPath, CATEGORIES_FOLDER);
            List<LocEnum>            enums      = await ReadFolderAsync<LocEnum>(folderPath, ENUMS_FOLDER);
            List<LocLocalizationKey> keys       = await ReadFolderAsync<LocLocalizationKey>(folderPath, KEYS_FOLDER);

            // Uncommitted changes must be read in order (0000, 0001, …)
            List<LocEntryChange> uncommitted = await ReadUncommittedChangesAsync(folderPath);

            return new LocProject
            {
                Metadata          = metadata,
                ProjectMembers    = members,
                Categories        = categories,
                Enums             = enums,
                UncommitedChanges = uncommitted,
                Keys              = keys,
            };
        }

        // ── Full Save ─────────────────────────────────────────────────────────

        /// <summary>
        /// Saves the entire project to the folder. Deletes files for entities
        /// that no longer exist (removed members, keys, etc.).
        /// </summary>
        public static async Task SaveAsync(LocProject project, string folderPath)
        {
            project.Metadata.SyncId        = Guid.NewGuid();
            project.Metadata.UpdatedAt     = DateTime.UtcNow;
            project.Metadata.FormatVersion = CURRENT_FORMAT_VERSION;

            EnsureFolderStructure(folderPath);

            await WriteJsonAsync(Path.Combine(folderPath, METADATA_FILE_NAME), project.Metadata);

            await SaveFolderAsync(folderPath, MEMBERS_FOLDER,    project.ProjectMembers, m => m.UserId.ToString());
            await SaveFolderAsync(folderPath, CATEGORIES_FOLDER, project.Categories,     c => c.Id.ToString());
            await SaveFolderAsync(folderPath, ENUMS_FOLDER,      project.Enums,          e => e.Id.ToString());
            await SaveFolderAsync(folderPath, KEYS_FOLDER,       project.Keys,           k => k.Id.ToString());

            await SaveUncommittedChangesAsync(folderPath, project.UncommitedChanges);
        }

        // ── Incremental Save (offline — changed keys only) ────────────────────

        /// <summary>
        /// Saves only the metadata/members/categories/enums and the keys whose Ids are in <paramref name="dirtyKeyIds"/>.
        /// Deleted keys (present on disk but not in the project) are also removed.
        /// Use this in offline mode after the user edits translations locally.
        /// </summary>
        public static async Task SaveIncrementalAsync(
            LocProject project,
            string folderPath,
            HashSet<Guid> dirtyKeyIds)
        {
            project.Metadata.SyncId    = Guid.NewGuid();
            project.Metadata.UpdatedAt = DateTime.UtcNow;

            EnsureFolderStructure(folderPath);

            // Always rewrite metadata (cheap, contains SyncId/UpdatedAt)
            await WriteJsonAsync(Path.Combine(folderPath, METADATA_FILE_NAME), project.Metadata);

            await SaveFolderAsync(folderPath, MEMBERS_FOLDER,    project.ProjectMembers, m => m.UserId.ToString());
            await SaveFolderAsync(folderPath, CATEGORIES_FOLDER, project.Categories,     c => c.Id.ToString());
            await SaveFolderAsync(folderPath, ENUMS_FOLDER,      project.Enums,          e => e.Id.ToString());
            await SaveFolderAsync(folderPath, KEYS_FOLDER,       project.Keys,           k => k.Id.ToString());

            string keysPath = Path.Combine(folderPath, KEYS_FOLDER);

            // Write only dirty keys
            foreach (LocLocalizationKey key in project.Keys)
            {
                if (!dirtyKeyIds.Contains(key.Id)) continue;
                await WriteJsonAsync(Path.Combine(keysPath, $"{key.Id}.json"), key);
            }

            // Delete key files that no longer exist in the project
            HashSet<string> validFileNames = project.Keys
                                                    .Select(k => $"{k.Id}.json")
                                                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (string file in Directory.GetFiles(keysPath, "*.json"))
            {
                if (!validFileNames.Contains(Path.GetFileName(file)))
                    File.Delete(file);
            }
        }

        // ── Save Uncommitted Changes Only (remote/bot mode) ───────────────────

        /// <summary>
        /// Saves only the UncommittedChanges folder. Use this in remote/bot mode
        /// to persist pending changes locally without touching any key files —
        /// those will only be written once the bot confirms the commit.
        /// </summary>
        public static async Task SaveUncommittedOnlyAsync(
            LocProject project,
            string folderPath)
        {
            EnsureFolderStructure(folderPath);
            await SaveUncommittedChangesAsync(folderPath, project.UncommitedChanges);
        }

        /// <summary>
        /// Clears all uncommitted change files from disk after a successful bot commit.
        /// </summary>
        public static void ClearUncommittedChanges(string folderPath)
        {
            string changesPath = Path.Combine(folderPath, UNCOMMITTED_CHANGES_FOLDER);
            if (!Directory.Exists(changesPath)) return;

            foreach (string file in Directory.GetFiles(changesPath, "*.json"))
                File.Delete(file);
        }

        // ── Folder Helpers ────────────────────────────────────────────────────

        private static void EnsureFolderStructure(string folderPath)
        {
            Directory.CreateDirectory(folderPath);
            Directory.CreateDirectory(Path.Combine(folderPath, MEMBERS_FOLDER));
            Directory.CreateDirectory(Path.Combine(folderPath, CATEGORIES_FOLDER));
            Directory.CreateDirectory(Path.Combine(folderPath, ENUMS_FOLDER));
            Directory.CreateDirectory(Path.Combine(folderPath, UNCOMMITTED_CHANGES_FOLDER));
            Directory.CreateDirectory(Path.Combine(folderPath, KEYS_FOLDER));
        }

        /// <summary>
        /// Reads all *.json files from a sub-folder. Missing folders are treated
        /// as empty (project may not have any members/enums yet).
        /// </summary>
        private static async Task<List<T>> ReadFolderAsync<T>(string rootPath, string subFolder)
            where T : class
        {
            string  folderPath = Path.Combine(rootPath, subFolder);
            List<T> result     = new List<T>();

            if (!Directory.Exists(folderPath)) return result;

            foreach (string file in Directory.GetFiles(folderPath, "*.json").OrderBy(f => f))
            {
                T? item = await ReadJsonAsync<T>(file);
                if (item != null) result.Add(item);
            }

            return result;
        }

        /// <summary>
        /// Writes all items to a sub-folder, one file per item, and deletes files
        /// for items that are no longer in the list.
        /// </summary>
        private static async Task SaveFolderAsync<T>(
            string rootPath,
            string subFolder,
            List<T> items,
            Func<T, string> getFileName)
        {
            string folderPath = Path.Combine(rootPath, subFolder);
            Directory.CreateDirectory(folderPath);

            HashSet<string> validFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (T item in items)
            {
                string fileName = $"{getFileName(item)}.json";
                validFiles.Add(fileName);
                await WriteJsonAsync(Path.Combine(folderPath, fileName), item);
            }

            // Remove files for deleted items
            foreach (string file in Directory.GetFiles(folderPath, "*.json"))
            {
                if (!validFiles.Contains(Path.GetFileName(file)))
                    File.Delete(file);
            }
        }

        /// <summary>
        /// Reads uncommitted changes in order (0000.json, 0001.json, …).
        /// </summary>
        private static async Task<List<LocEntryChange>> ReadUncommittedChangesAsync(string rootPath)
        {
            string               folderPath = Path.Combine(rootPath, UNCOMMITTED_CHANGES_FOLDER);
            List<LocEntryChange> result     = new List<LocEntryChange>();

            if (!Directory.Exists(folderPath)) return result;

            // Sort numerically by filename stem so 0009 comes before 0010
            IEnumerable<string> files = Directory.GetFiles(folderPath, "*.json")
                                                 .OrderBy(f =>
                                                  {
                                                      string stem = Path.GetFileNameWithoutExtension(f);
                                                      return int.TryParse(stem, out int n) ? n : int.MaxValue;
                                                  });

            foreach (string file in files)
            {
                LocEntryChange? change = await ReadJsonAsync<LocEntryChange>(file);
                if (change != null) result.Add(change);
            }

            return result;
        }

        /// <summary>
        /// Rewrites the UncommittedChanges folder from scratch, using zero-padded
        /// filenames to preserve order. Supports up to 10 000 pending changes
        /// before the padding overflows (at which point it still sorts correctly
        /// due to the numeric sort in ReadUncommittedChangesAsync).
        /// </summary>
        private static async Task SaveUncommittedChangesAsync(
            string rootPath,
            List<LocEntryChange> changes)
        {
            string folderPath = Path.Combine(rootPath, UNCOMMITTED_CHANGES_FOLDER);
            Directory.CreateDirectory(folderPath);

            // Remove all existing change files before rewriting
            foreach (string file in Directory.GetFiles(folderPath, "*.json"))
                File.Delete(file);

            for (int i = 0; i < changes.Count; i++)
            {
                string fileName = $"{i:D4}.json"; // 0000.json … 9999.json
                await WriteJsonAsync(Path.Combine(folderPath, fileName), changes[i]);
            }
        }

        // ── JSON helpers ──────────────────────────────────────────────────────

        private static async Task<T?> ReadJsonAsync<T>(string filePath) where T : class
        {
            string json = await File.ReadAllTextAsync(filePath);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonConvert.DeserializeObject<T>(json, _JsonSettings);
        }

        private static async Task WriteJsonAsync<T>(string filePath, T value)
        {
            string json = JsonConvert.SerializeObject(value, _JsonSettings);

            // Write to a temp sibling then rename — prevents corruption if
            // the process is killed mid-write
            string tmp = filePath + ".tmp";
            await File.WriteAllTextAsync(tmp, json);
            if (File.Exists(filePath)) File.Delete(filePath);
            File.Move(tmp, filePath);
        }
    }

    // ── Exception ─────────────────────────────────────────────────────────────

    /// <summary>Thrown when a project folder fails structural or version validation.</summary>
    public class ProjectFolderException : Exception
    {
        public ProjectFolderException(string message) : base(message) { }
    }
}