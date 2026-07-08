using DeusaldLocalizerCommon;

namespace DeusaldLocalizerWeb;

/// <summary>Web <see cref="IProjectStoreFactory"/>: a location handle maps to an IndexedDB-rooted store.</summary>
public sealed class IndexedDbProjectStoreFactory(IndexedDbInterop idb) : IProjectStoreFactory
{
    public IProjectFileStore Create(string location) => new IndexedDbProjectFileStore(idb, location);
}
