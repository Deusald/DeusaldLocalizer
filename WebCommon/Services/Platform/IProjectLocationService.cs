namespace DeusaldLocalizerWeb
{
    /// <summary>
    /// Provides a save <em>location</em> for a project that does not have one yet (a brand-new offline
    /// project). On desktop this shows the native folder picker and nests a subfolder named
    /// <paramref name="preferredFolderName"/> (the project slug) inside the chosen folder, so the project
    /// lives in its own folder; on the web it mints an IndexedDB namespace and the name is ignored.
    /// Returns null when the user cancels.
    /// </summary>
    public interface IProjectLocationService
    {
        Task<string?> PickSaveLocationAsync(string preferredFolderName);
    }
}