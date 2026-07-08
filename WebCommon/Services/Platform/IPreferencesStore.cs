namespace DeusaldLocalizerWeb
{
    /// <summary>
    /// A tiny string key/value settings store, abstracted over the host: MAUI <c>Preferences</c> on
    /// desktop, browser <c>localStorage</c> on the web. Used for small, non-secret app settings such as
    /// the recent-projects list.
    /// </summary>
    public interface IPreferencesStore
    {
        string Get(string key, string defaultValue);

        void Set(string key, string value);

        void Remove(string key);
    }
}
