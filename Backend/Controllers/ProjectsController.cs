using DeusaldLocalizerCommon;
using Microsoft.AspNetCore.Mvc;

namespace DeusaldLocalizerBackend;

/// <summary>
/// The bot's public surface. Auth travels in headers: <c>Authorization: Bearer &lt;raw-token&gt;</c>
/// and <c>X-User-Id: &lt;member guid&gt;</c>.
/// </summary>
[ApiController]
[Route("projects/{projectId:guid}")]
public sealed class ProjectsController(SyncService sync, PushService push, ILogger<ProjectsController> logger) : ControllerBase
{
    [HttpPost("sync")]
    public async Task<IActionResult> Sync(Guid projectId, [FromBody] SyncRequest request, CancellationToken ct)
    {
        if (!TryGetAuth(out Guid userId, out string token))
        {
            logger.LogInformation("Sync rejected (no auth) for project {ProjectId} from {Ip}", projectId, ClientIp());
            return Unauthorized();
        }
        ServiceResult<SyncResponse> result = await sync.SyncAsync(projectId, userId, token, request.SyncId, ct);
        logger.LogInformation("Sync {Outcome} for project {ProjectId} by user {UserId} from {Ip}; status {Status}",
            result.Outcome, projectId, userId, ClientIp(), result.Value?.Status);
        return Map(result);
    }

    [HttpPost("push")]
    public async Task<IActionResult> Push(Guid projectId, [FromBody] PushRequest request, CancellationToken ct)
    {
        if (!TryGetAuth(out Guid userId, out string token))
        {
            logger.LogInformation("Push rejected (no auth) for project {ProjectId} from {Ip}", projectId, ClientIp());
            return Unauthorized();
        }
        ServiceResult<PushResponse> result = await push.PushAsync(projectId, userId, token, request.Changes, ct);
        logger.LogInformation("Push {Outcome} for project {ProjectId} by user {UserId} from {Ip}; {Count} change(s), status {Status}",
            result.Outcome, projectId, userId, ClientIp(), request.Changes.Count, result.Value?.Status);
        return Map(result);
    }

    [HttpPost("bootstrap")]
    public async Task<IActionResult> Bootstrap(Guid projectId, [FromBody] BootstrapRequest request, CancellationToken ct)
    {
        // First-time full download: the caller has no UserId yet, so auth is by username + token.
        if (!TryGetToken(out string token))
        {
            logger.LogInformation("Bootstrap rejected (no auth) for project {ProjectId} from {Ip}", projectId, ClientIp());
            return Unauthorized();
        }
        ServiceResult<SyncResponse> result = await sync.BootstrapAsync(projectId, request.Username, token, ct);
        logger.LogInformation("Bootstrap {Outcome} for project {ProjectId} by user '{Username}' from {Ip}; status {Status}",
            result.Outcome, projectId, request.Username, ClientIp(), result.Value?.Status);
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

    // Prefer the forwarded client address (behind the reverse proxy / DigitalOcean droplet); fall back
    // to the direct connection when no proxy header is present.
    private string ClientIp()
    {
        string? forwarded = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwarded))
            return forwarded.Split(',')[0].Trim();
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private IActionResult Map<T>(ServiceResult<T> result) where T : class => result.Outcome switch
    {
        RequestOutcome.ProjectNotFound => NotFound(),
        RequestOutcome.Unauthorized    => Unauthorized(),
        _                              => Ok(result.Value),
    };
}
