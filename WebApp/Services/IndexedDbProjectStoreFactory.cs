using DeusaldLocalizerCommon;

namespace DeusaldLocalizerWeb;

/// <summary>Web <see cref="IProjectStoreFactory"/>: a location handle maps to an IndexedDB-rooted store.</summary>
public sealed class IndexedDbProjectStoreFactory : IProjectStoreFactory
{
    private readonly IndexedDbInterop _Idb;

    public IndexedDbProjectStoreFactory(IndexedDbInterop idb) => _Idb = idb;

    public IProjectFileStore Create(string location) => new IndexedDbProjectFileStore(_Idb, location);
}
