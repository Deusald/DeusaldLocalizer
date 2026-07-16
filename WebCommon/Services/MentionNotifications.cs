using DeusaldLocalizerCommon;
using JetBrains.Annotations;

namespace DeusaldLocalizerWeb;

public enum MentionSource
{
    KeyComment,
    TranslationComment,
    Suggestion,
    SuggestionComment,
}

/// <summary>One place the current user was @mentioned — enough to render a bell row and jump to the key.</summary>
[PublicAPI]
public sealed class MentionNotification
{
    public Guid          KeyId      { get; set; }
    public string        LanguageId { get; set; } = "";
    public MentionSource Source     { get; set; }
    public Guid          AuthorId   { get; set; }
    public DateTime      CreatedAt  { get; set; }
    public string        Text       { get; set; } = "";
}

/// <summary>
/// Scans a project's comments and suggestions for @mentions of the current user. Derived entirely on the
/// client (no server support needed) — the bell diffs each mention's <see cref="MentionNotification.CreatedAt"/>
/// against a locally-stored "last seen" timestamp to decide what is still unread.
/// </summary>
public static class MentionNotifications
{
    public static List<MentionNotification> Build(LocProject project, LocProjectMember currentUser)
    {
        List<MentionNotification> result = [];

        string username = currentUser.Username;
        Guid   userId   = currentUser.UserId;
        if (string.IsNullOrEmpty(username)) return result;

        List<string> known = project.ProjectMembers.Select(m => m.Username).ToList();

        foreach (LocLocalizationKey key in project.Keys)
        {
            foreach (LocComment comment in key.Comments)
                AddIfMentioned(result, comment, key.Id, "", MentionSource.KeyComment, username, userId, known);

            foreach (LocKeyTranslation translation in key.Translations)
            {
                foreach (LocComment comment in translation.Comments)
                    AddIfMentioned(result, comment, key.Id, translation.LanguageId, MentionSource.TranslationComment, username, userId, known);

                foreach (LocTranslationSuggestion suggestion in translation.Suggestions)
                {
                    if (suggestion.AuthorId != userId && MentionParser.MentionsUser(suggestion.Text, username, known))
                        result.Add(new MentionNotification
                        {
                            KeyId      = key.Id,
                            LanguageId = translation.LanguageId,
                            Source     = MentionSource.Suggestion,
                            AuthorId   = suggestion.AuthorId,
                            CreatedAt  = suggestion.CreatedAt,
                            Text       = suggestion.Text,
                        });

                    foreach (LocComment comment in suggestion.Comments)
                        AddIfMentioned(result, comment, key.Id, translation.LanguageId, MentionSource.SuggestionComment, username, userId, known);
                }
            }
        }

        result.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
        return result;
    }

    private static void AddIfMentioned(
        List<MentionNotification> result, LocComment comment, Guid keyId, string languageId,
        MentionSource source, string username, Guid userId, List<string> known)
    {
        if (comment.AuthorId == userId) return;
        if (!MentionParser.MentionsUser(comment.Text, username, known)) return;

        result.Add(new MentionNotification
        {
            KeyId      = keyId,
            LanguageId = languageId,
            Source     = source,
            AuthorId   = comment.AuthorId,
            CreatedAt  = comment.CreatedAt,
            Text       = comment.Text,
        });
    }
}
