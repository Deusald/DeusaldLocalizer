using DeusaldLocalizerCommon;
using Newtonsoft.Json;

namespace App;

public static class RecentProjectsService
{
    private const string _RECENT_PROJECTS_KEY = "RecentProjects";
    private const int    _MAX_RECENT_PROJECTS = 10;

    public static List<RecentProjectEntry> LoadRecentProjects()
    {
        try
        {
            string json = Preferences.Default.Get<string>(_RECENT_PROJECTS_KEY, "[]");
            return JsonConvert.DeserializeObject<List<RecentProjectEntry>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void ClearRecentProjects()
    {
        Preferences.Default.Remove(_RECENT_PROJECTS_KEY);
    }

    public static List<RecentProjectEntry> UpdateRecentProjects(ProjectDto project, string path, bool isRemote)
    {
        List<RecentProjectEntry> projects = LoadRecentProjects();

        // Calculate translation % across all non-main languages
        int pct = 0;
        if (project is { Languages.Count: > 1, Keys.Count: > 0 })
        {
            int totalSlots = project.Keys.Count * project.Languages.Count;
            int translated = project.TotalNumberOfApprovedKeys;
            pct = totalSlots > 0 ? (int)Math.Round(translated * 100.0 / totalSlots) : 0;
        }

        projects.RemoveAll(r => r.Path == path);
        projects.Insert(0, new RecentProjectEntry
        {
            ProjectName    = project.Name,
            Path           = path,
            KeyCount       = project.Keys.Count,
            LangCount      = project.Languages.Count,
            TranslationPct = pct,
            LastEdited     = project.UpdatedAt,
            IsRemote       = isRemote
        });

        if (projects.Count > _MAX_RECENT_PROJECTS)
            projects = projects.GetRange(0, _MAX_RECENT_PROJECTS);

        Preferences.Default.Set(_RECENT_PROJECTS_KEY, JsonConvert.SerializeObject(projects));
        return projects;
    }
}

public record RecentProjectEntry
{
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