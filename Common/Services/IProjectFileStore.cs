using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// A minimal "folder of files" abstraction: a flat key→content map where keys are '/'-separated
    /// paths relative to some root the store encapsulates. <see cref="ProjectFileService"/> is written
    /// entirely against this, so the same folder/ordering/zero-padding logic runs on a disc folder
    /// (<see cref="DiscProjectFileStore"/>) or an in-browser IndexedDB store without change.
    ///
    /// Paths always use '/' as the separator and never a leading slash (e.g. <c>"Keys/{guid}.json"</c>,
    /// <c>"metadata.json"</c>). Implementations translate to their own native form.
    /// </summary>
    [PublicAPI]
    public interface IProjectFileStore
    {
        /// <summary>True if a file exists at <paramref name="path"/>.</summary>
        Task<bool> FileExistsAsync(string path);

        /// <summary>Reads the file's text, or null when it does not exist.</summary>
        Task<string?> ReadTextAsync(string path);

        /// <summary>
        /// Writes <paramref name="content"/> to <paramref name="path"/> atomically, creating any parent
        /// folders. Replacing an existing file must not leave a half-written file if interrupted.
        /// </summary>
        Task WriteTextAsync(string path, string content);

        /// <summary>Deletes the file at <paramref name="path"/>; a no-op when it does not exist.</summary>
        Task DeleteFileAsync(string path);

        /// <summary>
        /// Returns the leaf file names (e.g. <c>"{guid}.json"</c>, not the full path) of every
        /// <c>*.json</c> file directly inside <paramref name="folder"/>. A missing folder yields an
        /// empty list. Order is unspecified — callers that need ordering sort the result themselves.
        /// </summary>
        Task<IReadOnlyList<string>> ListJsonFilesAsync(string folder);
    }
}
