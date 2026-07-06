using DeusaldLocalizerCommon;
using Microsoft.AspNetCore.Mvc;

namespace DeusaldLocalizerBackend;

/// <summary>
/// The bot's public surface. Auth travels in headers: <c>Authorization: Bearer &lt;raw-token&gt;</c>
/// and <c>X-User-Id: &lt;member guid&gt;</c>.
/// </summary>
[ApiController]
[Route("projects/{projectId:guid}")]
public sealed class ProjectsController : ControllerBase
{
    private readonly SyncService _Sync;
    private readonly PushService _Push;

    public ProjectsController(SyncService sync, PushService push)
    {
        _Sync = sync;
        _Push = push;
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync(Guid projectId, [FromBody] SyncRequest request, CancellationToken ct)
    {
        if (!TryGetAuth(out Guid userId, out string token)) return Unauthorized();
        ServiceResult<SyncResponse> result = await _Sync.SyncAsync(projectId, userId, token, request.SyncId, ct);
        return Map(result);
    }

    [HttpPost("push")]
    public async Task<IActionResult> Push(Guid projectId, [FromBody] PushRequest request, CancellationToken ct)
    {
        if (!TryGetAuth(out Guid userId, out string token)) return Unauthorized();
        ServiceResult<PushResponse> result = await _Push.PushAsync(projectId, userId, token, request.Changes, ct);
        return Map(result);
    }

    private bool TryGetAuth(out Guid userId, out string token)
    {
        userId = Guid.Empty;
        token  = string.Empty;

        string? auth = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        token = auth["Bearer ".Length..].Trim();
        string? uid = Request.Headers["X-User-Id"].FirstOrDefault();
        return Guid.TryParse(uid, out userId) && token.Length > 0;
    }

    private IActionResult Map<T>(ServiceResult<T> result) where T : class => result.Outcome switch
    {
        RequestOutcome.ProjectNotFound => NotFound(),
        RequestOutcome.Unauthorized    => Unauthorized(),
        _                              => Ok(result.Value),
    };
}
