using DeusaldLocalizerCommon;

namespace DeusaldLocalizerWeb
{
    /// <summary>
    /// Turns a project <em>location</em> handle into an <see cref="IProjectFileStore"/>. The location is
    /// opaque to the shared code: a disc folder path on desktop (<see cref="DiscProjectFileStore"/>) or an
    /// IndexedDB namespace on the web. <see cref="ProjectStateService"/> holds a location string and asks
    /// the factory for a store whenever it needs to read or write project files.
    /// </summary>
    public interface IProjectStoreFactory
    {
        IProjectFileStore Create(string location);
    }
}
