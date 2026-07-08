using System.Threading.Tasks;

namespace DeusaldLocalizerWeb
{
    /// <summary>
    /// Provides a save <em>location</em> for a project that does not have one yet (a brand-new offline
    /// project). On desktop this shows the native folder picker and returns the chosen path; on the web it
    /// mints an IndexedDB namespace. Returns null when the user cancels.
    /// </summary>
    public interface IProjectLocationService
    {
        Task<string?> PickSaveLocationAsync();
    }
}
