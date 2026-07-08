using Microsoft.JSInterop;

namespace DeusaldLocalizerWeb;

/// <summary>Web <see cref="IPreferencesStore"/> backed by the browser's <c>localStorage</c>.</summary>
public sealed class LocalStoragePreferencesStore : IPreferencesStore
{
    private readonly IJSInProcessRuntime _Js;

    public LocalStoragePreferencesStore(IJSRuntime js) => _Js = (IJSInProcessRuntime)js;

    public string Get(string key, string defaultValue) =>
        _Js.Invoke<string?>("localStorage.getItem", key) ?? defaultValue;

    public void Set(string key, string value) => _Js.InvokeVoid("localStorage.setItem", key, value);

    public void Remove(string key) => _Js.InvokeVoid("localStorage.removeItem", key);
}
