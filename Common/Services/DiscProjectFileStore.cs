using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// <see cref="IProjectFileStore"/> backed by a real folder on disk. Used by the MAUI desktop app,
    /// the Backend bot, and any external consumer (e.g. a Unity reader) that works with project folders.
    /// Writes go to a <c>.tmp</c> sibling then rename, so a mid-write kill can never leave a corrupt file.
    /// </summary>
    [PublicAPI]
    public sealed class DiscProjectFileStore : IProjectFileStore
    {
        private readonly string _Root;

        public DiscProjectFileStore(string rootFolderPath) => _Root = rootFolderPath;

        /// <summary>The disc folder this store is rooted at.</summary>
        public string RootFolderPath => _Root;

        public Task<bool> FileExistsAsync(string path) => Task.FromResult(File.Exists(FullPath(path)));

        public async Task<string?> ReadTextAsync(string path)
        {
            string full = FullPath(path);
            if (!File.Exists(full)) return null;
            return await File.ReadAllTextAsync(full);
        }

        public async Task WriteTextAsync(string path, string content)
        {
            string full = FullPath(path);
            string? dir  = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Write to a temp sibling then rename — prevents corruption if the process is killed mid-write.
            string tmp = full + ".tmp";
            await File.WriteAllTextAsync(tmp, content);
            if (File.Exists(full)) File.Delete(full);
            File.Move(tmp, full);
        }

        public Task DeleteFileAsync(string path)
        {
            string full = FullPath(path);
            if (File.Exists(full)) File.Delete(full);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListJsonFilesAsync(string folder)
        {
            string full = FullPath(folder);
            if (!Directory.Exists(full))
                return Task.FromResult<IReadOnlyList<string>>(new List<string>());

            IReadOnlyList<string> names = Directory.GetFiles(full, "*.json")
                                                   .Select(Path.GetFileName)
                                                    // ReSharper disable once RedundantSuppressNullableWarningExpression
                                                   .ToList()!;
            return Task.FromResult(names);
        }

        private string FullPath(string relative)
        {
            string native = relative.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(_Root, native);
        }
    }
}
