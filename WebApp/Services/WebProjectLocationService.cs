namespace DeusaldLocalizerWeb;

/// <summary>
/// Web <see cref="IProjectLocationService"/>: a brand-new offline project needs a location, which on the
/// web is simply a fresh IndexedDB namespace handle. No dialog — the project just gets its own bucket in
/// the browser database.
/// </summary>
public sealed class WebProjectLocationService : IProjectLocationService
{
    /// <summary>
    /// Prefix stamped on every location handle the Localizer mints. The IndexedDB store is shared across the
    /// whole <c>deusald.github.io</c> origin (the Story web app lives in the same database), so this scopes
    /// enumeration: the Localizer only lists its own projects, while a linked <c>story:</c> project stays
    /// reachable in the same store. Keep in sync with the filter passed to <c>listLocations</c>.
    /// </summary>
    public const string LocationPrefix = "loc:";

    public Task<string?> PickSaveLocationAsync(string preferredFolderName) =>
        Task.FromResult<string?>(LocationPrefix + Guid.NewGuid().ToString("N"));
}