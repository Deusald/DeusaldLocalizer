using DeusaldLocalizerCommon;
using Microsoft.AspNetCore.Mvc;

namespace DeusaldLocalizerBackend;

/// <summary>
/// The bot's public surface. Auth travels in headers: <c>Authorization: Bearer &lt;raw-token&gt;</c>
/// and <c>X-User-Id: &lt;member guid&gt;</c>.
/// </summary>
[ApiController]
[Route("projects/{projectId:guid}")]
public sealed class ProjectsController(SyncService sync, PushService push) : ControllerBase
{
    [HttpPost("sync")]
    public async Task<IActionResult> Sync(Guid projectId, [FromBody] SyncRequest request, CancellationToken ct)
    {
        if (!TryGetAuth(out Guid userId, out string token)) return Unauthorized();
        ServiceResult<SyncResponse> result = await sync.SyncAsync(projectId, userId, token, request.SyncId, ct);
        return Map(result);
    }

    [HttpPost("push")]
    public async Task<IActionResult> Push(Guid projectId, [FromBody] PushRequest request, CancellationToken ct)
    {
        if (!TryGetAuth(out Guid userId, out string token)) return Unauthorized();
        ServiceResult<PushResponse> result = await push.PushAsync(projectId, userId, token, request.Changes, ct);
        return Map(result);
    }

    [HttpPost("bootstrap")]
    public async Task<IActionResult> Bootstrap(Guid projectId, [FromBody] BootstrapRequest request, CancellationToken ct)
    {
        // First-time full download: the caller has no UserId yet, so auth is by username + token.
        if (!TryGetToken(out string token)) return Unauthorized();
        ServiceResult<SyncResponse> result = await sync.BootstrapAsync(projectId, request.Username, token, ct);
        return Map(result);
    }

    private bool TryGetAuth(out Guid userId, out string token)
    {
        userId = Guid.Empty;
        if (!TryGetToken(out token)) return false;

        string? uid = Request.Headers["X-User-Id"].FirstOrDefault();
        return Guid.TryParse(uid, out userId);
    }

    private bool TryGetToken(out string token)
    {
        token = string.Empty;

        string? auth = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        token = auth["Bearer ".Length..].Trim();
        return token.Length > 0;
    }

    private IActionResult Map<T>(ServiceResult<T> result) where T : class => result.Outcome switch
    {
        RequestOutcome.ProjectNotFound => NotFound(),
        RequestOutcome.Unauthorized    => Unauthorized(),
        _                              => Ok(result.Value),
    };
}
