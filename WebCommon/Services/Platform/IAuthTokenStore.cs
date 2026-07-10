namespace DeusaldLocalizerWeb
{
    /// <summary>
    /// Per-project sign-in credential storage, abstracted over the host. On desktop this is MAUI
    /// <c>SecureStorage</c> keyed by a hash of the project folder path; on the web it is browser storage
    /// keyed by the project id and its location handle. The <c>location</c> is the same opaque
    /// handle <see cref="ProjectStateService"/> uses to identify a project copy (a disc path on desktop,
    /// an IndexedDB namespace on the web).
    /// </summary>
    public interface IAuthTokenStore
    {
        /// <summary>Returns the cached (userId, rawToken) for a project copy, or null if none/invalid.</summary>
        Task<(Guid UserId, string Token)?> GetAsync(Guid projectId, string location);

        Task SaveAsync(Guid projectId, string location, Guid userId, string rawToken);

        void Remove(Guid projectId, string location);

        /// <summary>Wipes every cached sign-in credential (used when the recent-projects list is cleared).</summary>
        void RemoveAll();
    }
}