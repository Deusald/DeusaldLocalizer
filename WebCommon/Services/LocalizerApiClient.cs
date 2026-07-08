using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeusaldLocalizerCommon;
using JetBrains.Annotations;

namespace DeusaldLocalizerWeb;

/// <summary>
/// Talks to the backend bot over HTTP. Auth travels in headers
/// (<c>Authorization: Bearer &lt;token&gt;</c> + <c>X-User-Id</c>); the base address is the
/// project's <see cref="LocProjectMetadata.ApiUrl"/>.
/// </summary>
[PublicAPI]
public sealed class LocalizerApiClient
{
    private readonly HttpClient _Http;

    private static readonly JsonSerializerOptions _Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public LocalizerApiClient(HttpClient http) => _Http = http;

    public async Task<SyncResponse?> SyncAsync(
        string apiUrl, Guid projectId, Guid userId, string token, Guid syncId, CancellationToken ct = default)
    {
        using HttpRequestMessage request = Build(apiUrl, projectId, "sync", userId, token);
        request.Content = JsonContent.Create(new SyncRequest { SyncId = syncId }, options: _Json);

        using HttpResponseMessage response = await _Http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SyncResponse>(_Json, ct);
    }

    public async Task<PushResponse?> PushAsync(
        string apiUrl, Guid projectId, Guid userId, string token, Guid syncId,
        List<LocEntryChange> changes, CancellationToken ct = default)
    {
        using HttpRequestMessage request = Build(apiUrl, projectId, "push", userId, token);
        request.Content = JsonContent.Create(new PushRequest { SyncId = syncId, Changes = changes }, options: _Json);

        using HttpResponseMessage response = await _Http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PushResponse>(_Json, ct);
    }

    private static HttpRequestMessage Build(string apiUrl, Guid projectId, string action, Guid userId, string token)
    {
        string baseUrl = apiUrl.TrimEnd('/');
        HttpRequestMessage request = new(HttpMethod.Post, $"{baseUrl}/projects/{projectId}/{action}");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        request.Headers.TryAddWithoutValidation("X-User-Id", userId.ToString());
        return request;
    }
}
