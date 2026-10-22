using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.UserServices.ServerService;

internal static class LanApi
{
    internal static void Map(WebApplication app, ConfigManager configManager, LanClipboardBridge clipboard,
        Func<IPAddress?, bool> isPermitted)
    {
        app.Use(async (context, next) =>
        {
            var current = configManager.GetConfig<LanFirstConfig>();
            context.Response.Headers.CacheControl = "no-store";
            if (!current.Enabled || !isPermitted(context.Connection.LocalIpAddress))
            {
                context.Response.StatusCode = 403;
                return;
            }
            var expected = Encoding.UTF8.GetBytes($"Bearer {current.Token}");
            var actual = Encoding.UTF8.GetBytes(context.Request.Headers.Authorization.ToString());
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                context.Response.StatusCode = 401;
                return;
            }
            await next(context);
        });
        app.MapGet("/api/lan-first/v1/capabilities", () =>
        {
            var config = configManager.GetConfig<LanFirstConfig>();
            return new { Version = 1, config.DeviceId, config.HostName, TextDirect = true };
        });
        app.MapPost("/api/lan-first/v1/clipboard", async (LanRequest request, HttpContext context) =>
        {
            var response = await clipboard.ExecuteAsync(request, context.RequestAborted);
            return Results.Json(response, statusCode: response.Status switch
            {
                "ok" => 200,
                "conflict" or "request-id-conflict" => 409,
                "unsupported-type" => 422,
                _ => 400
            });
        });
    }
}
