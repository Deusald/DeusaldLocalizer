using System.Threading.Tasks;
using CommunityToolkit.Maui.Storage;
using DeusaldLocalizerWeb;
using JetBrains.Annotations;

namespace App;

/// <summary>
/// Desktop <see cref="IProjectLocationService"/>: picking a save location shows the native folder picker
/// and returns the chosen folder path (the location handle for a disc-backed project).
/// </summary>
[UsedImplicitly]
public sealed class MauiProjectLocationService : IProjectLocationService
{
    public async Task<string?> PickSaveLocationAsync()
    {
        FolderPickerResult result = await FolderPicker.Default.PickAsync();
        return result.IsSuccessful ? result.Folder.Path : null;
    }
}
