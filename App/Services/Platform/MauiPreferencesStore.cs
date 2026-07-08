using DeusaldLocalizerWeb;
using JetBrains.Annotations;

namespace App;

/// <summary>MAUI <see cref="IPreferencesStore"/> backed by <see cref="Preferences"/>.</summary>
[UsedImplicitly]
public sealed class MauiPreferencesStore : IPreferencesStore
{
    public string Get(string key, string defaultValue) => Preferences.Default.Get(key, defaultValue);

    public void Set(string key, string value) => Preferences.Default.Set(key, value);

    public void Remove(string key) => Preferences.Default.Remove(key);
}
