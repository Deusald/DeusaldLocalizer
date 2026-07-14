using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// Single source of truth for resolving which comment list a <see cref="LocCommentRef"/> targets on a key.
    /// Shared by the change executor, the authority/permission checks and the undo/redo inverse builder so all
    /// of them agree on where a comment lives (key, translation, or suggestion).
    /// </summary>
    [PublicAPI]
    public static class CommentLocator
    {
        /// <summary>
        /// Returns the comment list <paramref name="commentRef"/> points at, or null when the sub-entity does
        /// not exist. When <paramref name="create"/> is true a missing translation row (for the Translation
        /// scope) is created — used on add so a comment can be left on an as-yet untranslated language.
        /// </summary>
        public static List<LocComment>? ResolveList(LocLocalizationKey key, LocCommentRef commentRef, bool create)
        {
            switch (commentRef.Scope)
            {
                case CommentScope.Key:
                    return key.Comments;
                case CommentScope.Translation:
                {
                    LocKeyTranslation? translation = key.Translations.Find(t => t.LanguageId == commentRef.LanguageId);
                    if (translation == null)
                    {
                        if (!create) return null;
                        translation = new LocKeyTranslation { LanguageId = commentRef.LanguageId };
                        key.Translations.Add(translation);
                    }
                    return translation.Comments;
                }
                case CommentScope.Suggestion:
                {
                    LocKeyTranslation?        translation = key.Translations.Find(t => t.LanguageId == commentRef.LanguageId);
                    LocTranslationSuggestion? suggestion  = translation?.Suggestions.Find(s => s.Id == commentRef.SuggestionId);
                    return suggestion?.Comments;
                }
                default:
                    return null;
            }
        }

        /// <summary>Finds a single comment on <paramref name="key"/> by ref + id, or null when absent.</summary>
        public static LocComment? Find(LocLocalizationKey key, LocCommentRef commentRef, Guid commentId)
        {
            List<LocComment>? list = ResolveList(key, commentRef, create: false);
            return list?.Find(c => c.Id == commentId);
        }

        /// <summary>Human-readable description of a comment's target, for commit messages.</summary>
        public static string TargetLabel(LocCommentRef commentRef, LocLocalizationKey key)
        {
            switch (commentRef.Scope)
            {
                case CommentScope.Translation:
                    return $"translation {commentRef.LanguageId} of key {key.KeyName}";
                case CommentScope.Suggestion:
                    return $"a suggestion for {commentRef.LanguageId} on key {key.KeyName}";
                default:
                    return $"key {key.KeyName}";
            }
        }
    }
}
