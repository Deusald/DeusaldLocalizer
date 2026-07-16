using DeusaldLocalizerCommon;
using JetBrains.Annotations;

namespace DeusaldLocalizerWeb;

public enum PullChangeScope
{
    Key,         // key added / removed
    Translation, // translation text / status changed
    Suggestion,  // suggestion added / removed / voted
    Comment,     // comment added on key / translation / suggestion
    Other,       // non-key change (category, enum, member, language) — not navigable
}

/// <summary>
/// One human-readable entry describing something the last server pull changed. Navigable entries
/// (<see cref="KeyId"/> set) can jump straight to the affected key; <see cref="PullChangeScope.Other"/>
/// entries are informational only.
/// </summary>
[PublicAPI]
public sealed class PullChange
{
    /// <summary>The affected key, or null for a non-key (Other) change.</summary>
    public Guid? KeyId { get; set; }

    /// <summary>The affected language, when the change is language-scoped ("" otherwise).</summary>
    public string LanguageId { get; set; } = "";

    public PullChangeScope Scope { get; set; }

    /// <summary>Short label of what changed, e.g. "Translation updated" or "Comment added".</summary>
    public string Description { get; set; } = "";
}
