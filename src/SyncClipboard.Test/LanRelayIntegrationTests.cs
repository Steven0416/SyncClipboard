using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SyncClipboard.Server.Core;
using SyncClipboard.Server.Core.Controllers;
using SyncClipboard.Server.Core.CredentialChecker;
using SyncClipboard.Server.Core.Hubs;
using SyncClipboard.Server.Core.Services;
using SyncClipboard.Shared;

namespace SyncClipboard.Test;

[TestClass]
public sealed class LanRelayIntegrationTests
{
    [TestMethod]
    public async Task AuthenticatedHttpRequestTraversesSignalRAndOfflineNeverReturnsOldData()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var password = Guid.NewGuid().ToString("N");
        builder.Services.AddSingleton<ICredentialChecker>(new StaticCredentialChecker("test", password));
        builder.Services.AddAuthentication("BasicAuthentication")
            .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>("BasicAuthentication", null);
        builder.Services.AddAuthorization();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<LanRelay>();
        builder.Services.AddControllers().AddApplicationPart(typeof(LanRelayController).Assembly);
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHub<SyncClipboardHub>("/SyncClipboardHub");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await app.StartAsync(timeout.Token);
        var baseUrl = app.Urls.Single();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(baseUrl) };
        var deviceId = Guid.NewGuid().ToString("N");
        var endpoint = $"/api/lan-first/v1/devices/{deviceId}/clipboard";
        var request = new LanRequest(Guid.NewGuid(), Guid.NewGuid().ToString("N"), "get");
        using var unauthorized = await client.PostAsJsonAsync(endpoint, request, timeout.Token);
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        var auth = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"test:{password}"));
        client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(auth);
        await using var connection = new HubConnectionBuilder().WithUrl(baseUrl + "/SyncClipboardHub", options =>
        {
            options.Headers["Authorization"] = auth;
            options.Proxy = null;
        }).Build();
        connection.On<LanRequest>(LanProtocol.RequestEvent, async incoming =>
        {
            var response = new LanResponse(incoming.RequestId, deviceId, "ok",
                new ProfileDto { Text = "live 中文 😀\ntext" }, DateTimeOffset.UtcNow);
            await connection.InvokeAsync("CompleteLanRequestV1", response, timeout.Token);
        });
        await connection.StartAsync(timeout.Token);
        await connection.InvokeAsync("RegisterLanDeviceV1", deviceId, timeout.Token);
        using var reply = await client.PostAsJsonAsync(endpoint, request, timeout.Token);
        Assert.AreEqual(HttpStatusCode.OK, reply.StatusCode);
        var result = await reply.Content.ReadFromJsonAsync<LanResponse>(timeout.Token);
        Assert.AreEqual(request.RequestId, result!.RequestId);
        Assert.AreEqual("live 中文 😀\ntext", result.Profile!.Text);
        Assert.IsFalse(result.IsCache);
        await connection.StopAsync(timeout.Token);
        // StopAsync can finish before the server's disconnect callback; wait for that bounded transition.
        HttpResponseMessage? offline = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            offline?.Dispose();
            offline = await client.PostAsJsonAsync(endpoint, request with { RequestId = Guid.NewGuid() }, timeout.Token);
            if (offline.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                break;
            }
            await Task.Delay(50, timeout.Token);
        }
        using (offline)
        {
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, offline!.StatusCode);
            var response = await offline.Content.ReadFromJsonAsync<LanResponse>(timeout.Token);
            Assert.AreEqual("offline", response!.Status);
            Assert.IsNull(response.Profile);
        }
        await app.StopAsync(timeout.Token);
    }
}
