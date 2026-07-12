using System.IO;
using System.Linq;
using CommunityToolkit.Maui.Storage;
using DeusaldLocalizerWeb;
using JetBrains.Annotations;

namespace App;

/// <summary>
/// Desktop <see cref="IProjectLocationService"/>: picking a save location shows the native folder picker,
/// then nests a subfolder named from the project slug inside the chosen folder so the project lives in its
/// own folder. Returns that subfolder path (the location handle for a disc-backed project).
/// </summary>
[UsedImplicitly]
public sealed class MauiProjectLocationService : IProjectLocationService
{
    public async Task<string?> PickSaveLocationAsync(string preferredFolderName)
    {
        FolderPickerResult result = await FolderPicker.Default.PickAsync();
        if (!result.IsSuccessful) return null;

        string parent    = result.Folder.Path;
        string subFolder = SanitizeFolderName(preferredFolderName);
        return string.IsNullOrEmpty(subFolder) ? parent : Path.Combine(parent, subFolder);
    }

    /// <summary>Strips characters that are illegal in a folder name, so any slug maps to a valid folder.</summary>
    private static string SanitizeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
    }
}
