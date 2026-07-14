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
    /// Reads and writes a localization project stored as a "folder of files". The concrete backing store
    /// is an <see cref="IProjectFileStore"/> — a real disc folder on desktop/Backend, or an in-browser
    /// IndexedDB store on the web — so the exact same layout and ordering rules apply everywhere.
    ///
    /// Expected layout (paths are '/'-separated, relative to the store root):
    ///   metadata.json
    ///   Members/              {guid}.json  per LocProjectMember
    ///   Categories/           {guid}.json  per LocCategory
    ///   Enums/                {guid}.json  per LocEnum
    ///   UncommittedChanges/   0000.json, 0001.json … (ordered, zero-padded)
    ///   Keys/                 {guid}.json  per LocLocalizationKey
    ///
    /// Every method has a <c>string folderPath</c> overload that operates on a <see cref="DiscProjectFileStore"/>,
    /// so existing disc callers (App, Backend) are unchanged.
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

        /// <summary>Opens and validates a project from a disc folder.</summary>
        public static Task<LocProject> OpenAsync(string folderPath) =>
            OpenAsync(new DiscProjectFileStore(folderPath));

        /// <summary>
        /// Opens and validates a project from <paramref name="store"/>, returning a fully hydrated LocProject.
        /// Throws <see cref="ProjectFolderException"/> on any structural or version error.
        /// </summary>
        public static async Task<LocProject> OpenAsync(IProjectFileStore store)
        {
            // ── Read metadata ──────────────────────────────────────────────────
            if (!await store.FileExistsAsync(METADATA_FILE_NAME))
                throw new ProjectFolderException(
                    $"'{METADATA_FILE_NAME}' not found — this does not appear to be a valid project.");

            LocProjectMetadata metadata = await ReadJsonAsync<LocProjectMetadata>(store, METADATA_FILE_NAME)
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
            List<LocProjectMember>   members    = await ReadFolderAsync<LocProjectMember>(store, MEMBERS_FOLDER);
            List<LocCategory>        categories = await ReadFolderAsync<LocCategory>(store, CATEGORIES_FOLDER);
            List<LocEnum>            enums      = await ReadFolderAsync<LocEnum>(store, ENUMS_FOLDER);
            List<LocLocalizationKey> keys       = await ReadFolderAsync<LocLocalizationKey>(store, KEYS_FOLDER);

            // Uncommitted changes must be read in order (0000, 0001, …)
            List<LocEntryChange> uncommitted = await ReadUncommittedChangesAsync(store);

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

        public static Task SaveAsync(LocProject project, string folderPath) =>
            SaveAsync(project, new DiscProjectFileStore(folderPath));

        /// <summary>
        /// Saves the entire project. Deletes files for entities that no longer exist
        /// (removed members, keys, etc.).
        /// </summary>
        public static async Task SaveAsync(LocProject project, IProjectFileStore store)
        {
            project.Metadata.SyncId        = Guid.NewGuid();
            project.Metadata.UpdatedAt     = DateTime.UtcNow;
            project.Metadata.FormatVersion = CURRENT_FORMAT_VERSION;

            await WriteJsonAsync(store, METADATA_FILE_NAME, project.Metadata);

            await SaveFolderAsync(store, MEMBERS_FOLDER,    project.ProjectMembers, m => m.UserId.ToString());
            await SaveFolderAsync(store, CATEGORIES_FOLDER, project.Categories,     c => c.Id.ToString());
            await SaveFolderAsync(store, ENUMS_FOLDER,      project.Enums,          e => e.Id.ToString());
            await SaveFolderAsync(store, KEYS_FOLDER,       project.Keys,           k => k.Id.ToString());

            await SaveUncommittedChangesAsync(store, project.UncommitedChanges);
        }

        // ── Incremental Save (offline — changed keys only) ────────────────────

        public static Task SaveIncrementalAsync(LocProject project, string folderPath, HashSet<Guid> dirtyKeyIds) =>
            SaveIncrementalAsync(project, new DiscProjectFileStore(folderPath), dirtyKeyIds);

        /// <summary>
        /// Saves the metadata/members/categories/enums and only the keys whose Ids are in <paramref name="dirtyKeyIds"/>.
        /// Deleted keys (present in the store but not in the project) are also removed.
        /// Use this in offline mode after the user edits translations locally.
        /// </summary>
        public static async Task SaveIncrementalAsync(LocProject project, IProjectFileStore store, HashSet<Guid> dirtyKeyIds)
        {
            project.Metadata.SyncId    = Guid.NewGuid();
            project.Metadata.UpdatedAt = DateTime.UtcNow;

            // Always rewrite metadata (cheap, contains SyncId/UpdatedAt)
            await WriteJsonAsync(store, METADATA_FILE_NAME, project.Metadata);

            await SaveFolderAsync(store, MEMBERS_FOLDER,    project.ProjectMembers, m => m.UserId.ToString());
            await SaveFolderAsync(store, CATEGORIES_FOLDER, project.Categories,     c => c.Id.ToString());
            await SaveFolderAsync(store, ENUMS_FOLDER,      project.Enums,          e => e.Id.ToString());

            // Write only dirty keys
            foreach (LocLocalizationKey key in project.Keys)
            {
                if (!dirtyKeyIds.Contains(key.Id)) continue;
                await WriteJsonAsync(store, $"{KEYS_FOLDER}/{key.Id}.json", key);
            }

            // Delete key files that no longer exist in the project
            HashSet<string> validFileNames = project.Keys
                                                    .Select(k => $"{k.Id}.json")
                                                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (string file in await store.ListJsonFilesAsync(KEYS_FOLDER))
            {
                if (!validFileNames.Contains(file))
                    await store.DeleteFileAsync($"{KEYS_FOLDER}/{file}");
            }
        }

        // ── Save Uncommitted Changes Only (remote/bot mode) ───────────────────

        public static Task SaveUncommittedOnlyAsync(LocProject project, string folderPath) =>
            SaveUncommittedOnlyAsync(project, new DiscProjectFileStore(folderPath));

        /// <summary>
        /// Saves only the UncommittedChanges folder. Use this in remote/bot mode to persist pending
        /// changes without touching any key files — those are only written once the bot confirms the commit.
        /// </summary>
        public static Task SaveUncommittedOnlyAsync(LocProject project, IProjectFileStore store) =>
            SaveUncommittedChangesAsync(store, project.UncommitedChanges);

        public static Task SaveMetadataOnlyAsync(LocProject project, string folderPath) =>
            SaveMetadataOnlyAsync(project, new DiscProjectFileStore(folderPath));

        /// <summary>
        /// Writes only <c>metadata.json</c> (does not mint a new SyncId — the caller is expected
        /// to set <see cref="LocProjectMetadata.SyncId"/>/<see cref="LocProjectMetadata.UpdatedAt"/>
        /// explicitly). Used by the bot to stamp a new sync id in its own commit.
        /// </summary>
        public static Task SaveMetadataOnlyAsync(LocProject project, IProjectFileStore store) =>
            WriteJsonAsync(store, METADATA_FILE_NAME, project.Metadata);

        public static Task WriteEntityForChangeAsync(LocProject project, string folderPath, LocEntryChange change) =>
            WriteEntityForChangeAsync(project, new DiscProjectFileStore(folderPath), change);

        /// <summary>
        /// Writes (or, for whole-entity removals, deletes) exactly the single file affected by
        /// <paramref name="change"/>, after that change has been applied in memory. Used by the bot so it
        /// can stage and commit one change at a time. Sub-entity changes (translations, suggestions, flags,
        /// tags, variables) live inside their key's file and rewrite that key.
        /// </summary>
        public static async Task WriteEntityForChangeAsync(LocProject project, IProjectFileStore store, LocEntryChange change)
        {
            switch (change.Type)
            {
                case EntryChangeType.MemberAdded:
                case EntryChangeType.MemberUpdated:
                {
                    LocProjectMember? member = project.ProjectMembers.Find(m => m.UserId == change.EntryId);
                    if (member != null)
                        await WriteJsonAsync(store, EntityPath(MEMBERS_FOLDER, change.EntryId), member);
                    break;
                }
                case EntryChangeType.MemberRemoved:
                {
                    // Delete the member file, then rewrite every key file: removing a member reassigns any
                    // suggestion authors, votes and flag creators it owned to the offline user (see
                    // EntryChangeExeService.ReassignMemberReferences), so those key files may have changed too.
                    await store.DeleteFileAsync(EntityPath(MEMBERS_FOLDER, change.EntryId));
                    foreach (LocLocalizationKey key in project.Keys)
                        await WriteJsonAsync(store, EntityPath(KEYS_FOLDER, key.Id), key);
                    break;
                }
                case EntryChangeType.LanguageAdded:
                case EntryChangeType.LanguageRemoved:
                    await WriteJsonAsync(store, METADATA_FILE_NAME, project.Metadata);
                    break;
                case EntryChangeType.CategoryAdded:
                case EntryChangeType.CategoryUpdated:
                {
                    LocCategory? category = project.Categories.Find(c => c.Id == change.EntryId);
                    if (category != null)
                        await WriteJsonAsync(store, EntityPath(CATEGORIES_FOLDER, change.EntryId), category);
                    break;
                }
                case EntryChangeType.CategoryRemoved:
                    await store.DeleteFileAsync(EntityPath(CATEGORIES_FOLDER, change.EntryId));
                    break;
                case EntryChangeType.EnumAdded:
                case EntryChangeType.EnumUpdated:
                {
                    LocEnum? locEnum = project.Enums.Find(e => e.Id == change.EntryId);
                    if (locEnum != null)
                        await WriteJsonAsync(store, EntityPath(ENUMS_FOLDER, change.EntryId), locEnum);
                    break;
                }
                case EntryChangeType.EnumRemoved:
                    await store.DeleteFileAsync(EntityPath(ENUMS_FOLDER, change.EntryId));
                    break;
                case EntryChangeType.KeyRemoved:
                    await store.DeleteFileAsync(EntityPath(KEYS_FOLDER, change.EntryId));
                    break;
                default:
                {
                    // Every remaining change type is key-scoped: EntryId is the key id.
                    LocLocalizationKey? key = project.Keys.Find(k => k.Id == change.EntryId);
                    if (key != null)
                        await WriteJsonAsync(store, EntityPath(KEYS_FOLDER, change.EntryId), key);
                    break;
                }
            }
        }

        private static string EntityPath(string subFolder, Guid id) => $"{subFolder}/{id}.json";

        /// <summary>Clears all uncommitted change files after a successful bot commit (disc overload).</summary>
        public static void ClearUncommittedChanges(string folderPath) =>
            ClearUncommittedChangesAsync(new DiscProjectFileStore(folderPath)).GetAwaiter().GetResult();

        /// <summary>Clears all uncommitted change files after a successful bot commit.</summary>
        public static async Task ClearUncommittedChangesAsync(IProjectFileStore store)
        {
            foreach (string file in await store.ListJsonFilesAsync(UNCOMMITTED_CHANGES_FOLDER))
                await store.DeleteFileAsync($"{UNCOMMITTED_CHANGES_FOLDER}/{file}");
        }

        // ── Folder Helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Reads all *.json files from a sub-folder. Missing folders are treated
        /// as empty (project may not have any members/enums yet).
        /// </summary>
        private static async Task<List<T>> ReadFolderAsync<T>(IProjectFileStore store, string subFolder)
            where T : class
        {
            List<T> result = new List<T>();

            foreach (string file in (await store.ListJsonFilesAsync(subFolder)).OrderBy(f => f, StringComparer.Ordinal))
            {
                T? item = await ReadJsonAsync<T>(store, $"{subFolder}/{file}");
                if (item != null) result.Add(item);
            }

            return result;
        }

        /// <summary>
        /// Writes all items to a sub-folder, one file per item, and deletes files
        /// for items that are no longer in the list.
        /// </summary>
        private static async Task SaveFolderAsync<T>(
            IProjectFileStore store,
            string subFolder,
            List<T> items,
            Func<T, string> getFileName)
        {
            HashSet<string> validFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (T item in items)
            {
                string fileName = $"{getFileName(item)}.json";
                validFiles.Add(fileName);
                await WriteJsonAsync(store, $"{subFolder}/{fileName}", item);
            }

            // Remove files for deleted items
            foreach (string file in await store.ListJsonFilesAsync(subFolder))
            {
                if (!validFiles.Contains(file))
                    await store.DeleteFileAsync($"{subFolder}/{file}");
            }
        }

        /// <summary>Reads uncommitted changes in order (0000.json, 0001.json, …).</summary>
        private static async Task<List<LocEntryChange>> ReadUncommittedChangesAsync(IProjectFileStore store)
        {
            List<LocEntryChange> result = new List<LocEntryChange>();

            // Sort numerically by filename stem so 0009 comes before 0010
            IEnumerable<string> files = (await store.ListJsonFilesAsync(UNCOMMITTED_CHANGES_FOLDER))
                                       .OrderBy(f =>
                                        {
                                            string stem = Path.GetFileNameWithoutExtension(f);
                                            return int.TryParse(stem, out int n) ? n : int.MaxValue;
                                        });

            foreach (string file in files)
            {
                LocEntryChange? change = await ReadJsonAsync<LocEntryChange>(store, $"{UNCOMMITTED_CHANGES_FOLDER}/{file}");
                if (change != null) result.Add(change);
            }

            return result;
        }

        /// <summary>
        /// Rewrites the UncommittedChanges folder from scratch, using zero-padded filenames to preserve
        /// order. Supports up to 10 000 pending changes before the padding overflows (at which point it
        /// still sorts correctly due to the numeric sort in <see cref="ReadUncommittedChangesAsync"/>).
        /// </summary>
        private static async Task SaveUncommittedChangesAsync(IProjectFileStore store, List<LocEntryChange> changes)
        {
            // Remove all existing change files before rewriting
            foreach (string file in await store.ListJsonFilesAsync(UNCOMMITTED_CHANGES_FOLDER))
                await store.DeleteFileAsync($"{UNCOMMITTED_CHANGES_FOLDER}/{file}");

            for (int i = 0; i < changes.Count; i++)
            {
                string fileName = $"{i:D4}.json"; // 0000.json … 9999.json
                await WriteJsonAsync(store, $"{UNCOMMITTED_CHANGES_FOLDER}/{fileName}", changes[i]);
            }
        }

        // ── JSON helpers ──────────────────────────────────────────────────────

        private static async Task<T?> ReadJsonAsync<T>(IProjectFileStore store, string path) where T : class
        {
            string? json = await store.ReadTextAsync(path);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonConvert.DeserializeObject<T>(json, _JsonSettings);
        }

        private static Task WriteJsonAsync<T>(IProjectFileStore store, string path, T value)
        {
            string json = JsonConvert.SerializeObject(value, _JsonSettings);
            return store.WriteTextAsync(path, json);
        }
    }

    // ── Exception ─────────────────────────────────────────────────────────────

    /// <summary>Thrown when a project fails structural or version validation.</summary>
    public class ProjectFolderException : Exception
    {
        public ProjectFolderException(string message) : base(message) { }
    }
}
