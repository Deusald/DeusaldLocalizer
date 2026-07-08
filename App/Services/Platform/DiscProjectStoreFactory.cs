using DeusaldLocalizerCommon;
using DeusaldLocalizerWeb;
using JetBrains.Annotations;

namespace App;

/// <summary>
/// Desktop <see cref="IProjectStoreFactory"/>: the location handle is a folder path, so the store is a
/// <see cref="DiscProjectFileStore"/> rooted at it.
/// </summary>
[UsedImplicitly]
public sealed class DiscProjectStoreFactory : IProjectStoreFactory
{
    public IProjectFileStore Create(string location) => new DiscProjectFileStore(location);
}
