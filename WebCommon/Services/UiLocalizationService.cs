using System;
using System.Collections.Generic;
using System.Linq;

namespace DeusaldLocalizerWeb
{
    /// <summary>
    /// Runtime access to the app's own UI translations (the strings generated into
    /// <see cref="Localization"/> from the <c>deusald-localizer-ui</c> project). Holds the currently
    /// selected UI language — a per-user preference, independent of any open project — and raises
    /// <see cref="CultureChanged"/> when it changes so the layouts can re-render the tree.
    ///
    /// Lookups fall back to the source language (<c>en-US</c>) when a string is missing in the current
    /// language, and finally to an empty string, so the UI never shows a blank because of a gap.
    /// </summary>
    public sealed class UiLocalizationService
    {
        public const string FallbackLanguage = "en-US";

        private const string _PREF_KEY = "ui.language";

        private readonly IPreferencesStore _Prefs;

        public UiLocalizationService(IPreferencesStore prefs)
        {
            _Prefs          = prefs;
            CurrentLanguage = prefs.Get(_PREF_KEY, FallbackLanguage);
        }

        /// <summary>BCP-47 code of the language the UI is currently rendered in.</summary>
        public string CurrentLanguage { get; private set; }

        /// <summary>Raised after <see cref="CurrentLanguage"/> changes.</summary>
        public event Action? CultureChanged;

        /// <summary>Languages that actually carry translations in the generated data, source first.</summary>
        public IReadOnlyList<string> AvailableLanguages =>
            Localization.Translations.Keys
                        .OrderBy(l => l == FallbackLanguage ? 0 : 1)
                        .ThenBy(l => l, StringComparer.Ordinal)
                        .ToList();

        public void SetLanguage(string language)
        {
            if (string.IsNullOrEmpty(language) || language == CurrentLanguage) return;
            CurrentLanguage = language;
            _Prefs.Set(_PREF_KEY, language);
            CultureChanged?.Invoke();
        }

        /// <summary>
        /// Returns the UI string for <paramref name="keyId"/> in the current language, falling back to the
        /// source language and then to an empty string. When <paramref name="values"/> is supplied the text
        /// is rendered through SmartFormat.
        /// </summary>
        public string T(Guid keyId, IDictionary<string, object>? values = null)
        {
            string text = Localization.Get(CurrentLanguage, keyId, values);
            if (!string.IsNullOrEmpty(text)) return text;

            if (CurrentLanguage != FallbackLanguage)
            {
                text = Localization.Get(FallbackLanguage, keyId, values);
                if (!string.IsNullOrEmpty(text)) return text;
            }
            return string.Empty;
        }
    }
}
