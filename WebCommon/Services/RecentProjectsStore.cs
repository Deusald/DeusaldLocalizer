using DeusaldLocalizerCommon;
using Newtonsoft.Json;

namespace DeusaldLocalizerWeb
{
    /// <summary>
    /// Maintains the "recent projects" list shown on the home screen. Storage is abstracted via
    /// <see cref="IPreferencesStore"/> (MAUI Preferences on desktop, localStorage on the web); clearing
    /// entries also clears their cached sign-in credentials via <see cref="IAuthTokenStore"/>, so no token
    /// lingers for a project the user can no longer see.
    /// </summary>
    public sealed class RecentProjectsStore(IPreferencesStore prefs, IAuthTokenStore authTokens)
    {
        // Namespaced so the web client does not clobber the Story app's recents: both run on the same
        // origin (deusald.github.io) and share localStorage. Harmless on desktop (MAUI Preferences are sandboxed).
        private const string _RECENT_PROJECTS_KEY = "loc:RecentProjects";
        private const int    _MAX_RECENT_PROJECTS = 10;

        public List<RecentProjectEntry> LoadRecentProjects()
        {
            try
            {
                string json = prefs.Get(_RECENT_PROJECTS_KEY, "[]");
                return JsonConvert.DeserializeObject<List<RecentProjectEntry>>(json) ?? new List<RecentProjectEntry>();
            }
            catch
            {
                return new List<RecentProjectEntry>();
            }
        }

        public void ClearRecentProjects()
        {
            prefs.Remove(_RECENT_PROJECTS_KEY);
            // Forgetting the projects should also forget the sign-ins cached for them, so no access
            // token lingers in secure storage for a project the user can no longer see.
            authTokens.RemoveAll();
        }

        /// <summary>
        /// Drops a single project from the recent list and removes its cached sign-in credential. Returns
        /// the updated list. Entries saved before <see cref="RecentProjectEntry.ProjectId"/> existed carry
        /// an empty Id, so their token (if any) cannot be targeted and is left for the next full clear.
        /// </summary>
        public List<RecentProjectEntry> RemoveRecentProject(RecentProjectEntry entry)
        {
            List<RecentProjectEntry> projects = LoadRecentProjects();
            projects.RemoveAll(r => r.Path == entry.Path);
            prefs.Set(_RECENT_PROJECTS_KEY, JsonConvert.SerializeObject(projects));

            if (entry.ProjectId != Guid.Empty)
                authTokens.Remove(entry.ProjectId, entry.Path);

            return projects;
        }

        public List<RecentProjectEntry> UpdateRecentProjects(LocProject project, string location, bool isRemote)
        {
            List<RecentProjectEntry> projects = LoadRecentProjects();

            // Calculate translation % across all non-main languages
            int pct = 0;
            if (project is { Metadata.Languages.Count: > 1, Keys.Count: > 0 })
            {
                int totalSlots = project.Keys.Count * project.Metadata.Languages.Count;
                int translated = project.TotalNumberOfApprovedKeys();
                pct = totalSlots > 0 ? (int)Math.Round(translated * 100.0 / totalSlots) : 0;
            }

            projects.RemoveAll(r => r.Path == location);
            projects.Insert(0, new RecentProjectEntry
            {
                ProjectId      = project.Metadata.Id,
                ProjectName    = project.Metadata.Name,
                Path           = location,
                KeyCount       = project.Keys.Count,
                LangCount      = project.Metadata.Languages.Count,
                TranslationPct = pct,
                LastEdited     = project.Metadata.UpdatedAt,
                IsRemote       = isRemote
            });

            if (projects.Count > _MAX_RECENT_PROJECTS)
                projects = projects.GetRange(0, _MAX_RECENT_PROJECTS);

            prefs.Set(_RECENT_PROJECTS_KEY, JsonConvert.SerializeObject(projects));
            return projects;
        }
    }

    public record RecentProjectEntry
    {
        public Guid     ProjectId      { get; init; }
        public string   ProjectName    { get; init; } = "";
        public string   Path           { get; init; } = "";
        public int      KeyCount       { get; init; }
        public int      LangCount      { get; init; }
        public int      TranslationPct { get; init; }
        public DateTime LastEdited     { get; init; } = DateTime.Now;
        public bool     IsRemote       { get; init; }

        public string LastEditedLabel
        {
            get
            {
                TimeSpan diff = DateTime.Now - LastEdited;
                if (diff.TotalMinutes < 2) return "just now";
                if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}m ago";
                if (diff.TotalDays < 1) return $"{(int)diff.TotalHours}h ago";
                if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
                return LastEdited.ToString("MMM d");
            }
        }
    }
}