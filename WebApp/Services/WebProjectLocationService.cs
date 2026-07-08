namespace DeusaldLocalizerWeb;

/// <summary>
/// Web <see cref="IProjectLocationService"/>: a brand-new offline project needs a location, which on the
/// web is simply a fresh IndexedDB namespace handle (a GUID). No dialog — the project just gets its own
/// bucket in the browser database.
/// </summary>
public sealed class WebProjectLocationService : IProjectLocationService
{
    public Task<string?> PickSaveLocationAsync() =>
        Task.FromResult<string?>(Guid.NewGuid().ToString("N"));
}
