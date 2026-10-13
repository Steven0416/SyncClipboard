using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncClipboard.Server.Core.Services;

namespace SyncClipboard.Server.Core.Controllers;

[ApiController]
[Authorize]
[Route("api/lan-first/v1")]
public sealed class LanRelayController(LanRelay relay) : ControllerBase
{
    [HttpGet("capabilities")]
    public IActionResult Capabilities() => Ok(new { Version = 1, TextRelay = true, OfflineQueue = false });

    [HttpPost("devices/{deviceId}/clipboard")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> RequestClipboard(string deviceId, LanRequest request, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await relay.RequestAsync(deviceId, request, token);
        return StatusCode(result.Status switch
        {
            "ok" => 200,
            "conflict" or "request-id-conflict" => 409,
            "offline" => 503,
            "timeout" => 504,
            "busy" => 429,
            "unsupported-type" => 422,
            _ => 400
        }, result);
    }
}
