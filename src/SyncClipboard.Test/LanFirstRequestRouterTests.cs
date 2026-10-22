using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Shared;

namespace SyncClipboard.Test;

[TestClass]
public sealed class LanFirstRequestRouterTests
{
    private static readonly string DeviceId = Guid.NewGuid().ToString("N");
    private static LanRequest Request() => new(Guid.NewGuid(), Guid.NewGuid().ToString("N"), "get");
    private static HttpResponseMessage Reply(LanRequest request, string? deviceId = null) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new LanResponse(request.RequestId, deviceId ?? DeviceId, "ok",
            new ProfileDto { Text = "live text", Hash = "test-hash" })),
    };

    [TestMethod]
    public async Task HealthyLanNeverContactsNas()
    {
        var request = Request();
        using var fixture = new Fixture((_, _) => Task.FromResult(Reply(request)));
        var result = await fixture.Router.ExecuteAsync(request, TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(ClipboardTransport.Lan, result.Transport);
        Assert.AreEqual(0, fixture.NasCalls);
    }

    [TestMethod]
    public async Task LostWriteReplyReusesExactRequestAndSeparatesCredentials()
    {
        var request = Request() with { Operation = "set", Text = "from phone", ExpectedHash = "old-hash" };
        LanRequest? first = null;
        using var fixture = new Fixture(async (message, _) =>
        {
            Assert.AreEqual("Bearer", message.Headers.Authorization!.Scheme);
            Assert.AreEqual("lan-only-secret", message.Headers.Authorization.Parameter);
            first = await message.Content!.ReadFromJsonAsync<LanRequest>(TestContext.CancellationTokenSource.Token);
            throw new HttpRequestException("reply lost after write");
        }, async (message, _) =>
        {
            Assert.AreEqual("Basic", message.Headers.Authorization!.Scheme);
            Assert.AreEqual("nas-only-secret", message.Headers.Authorization.Parameter);
            Assert.AreEqual(first, await message.Content!.ReadFromJsonAsync<LanRequest>(TestContext.CancellationTokenSource.Token));
            return Reply(request);
        });
        Assert.AreEqual(ClipboardTransport.Nas, (await fixture.Router.ExecuteAsync(request, TestContext.CancellationTokenSource.Token)).Transport);
        Assert.AreEqual(1, fixture.NasCalls);
    }

    [TestMethod]
    public async Task LanDeadlineFallsBackButNextOperationTriesLanAgain()
    {
        var request = Request();
        var calls = 0;
        using var fixture = new Fixture(async (_, token) =>
        {
            if (++calls == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            return Reply(request);
        }, (_, _) => Task.FromResult(Reply(request)));
        Assert.AreEqual(ClipboardTransport.Nas, (await fixture.Router.ExecuteAsync(request, TestContext.CancellationTokenSource.Token)).Transport);
        Assert.AreEqual(ClipboardTransport.Lan, (await fixture.Router.ExecuteAsync(request, TestContext.CancellationTokenSource.Token)).Transport);
        Assert.AreEqual(1, fixture.NasCalls);
    }

    [TestMethod]
    [DataRow(408)]
    [DataRow(502)]
    [DataRow(503)]
    [DataRow(504)]
    public async Task AvailabilityFailuresFallBack(int status)
    {
        var request = Request();
        using var fixture = new Fixture((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)),
            (_, _) => Task.FromResult(Reply(request)));
        Assert.AreEqual(ClipboardTransport.Nas, (await fixture.Router.ExecuteAsync(request, TestContext.CancellationTokenSource.Token)).Transport);
    }

    [TestMethod]
    [DataRow(301)]
    [DataRow(400)]
    [DataRow(401)]
    [DataRow(403)]
    [DataRow(409)]
    [DataRow(422)]
    [DataRow(429)]
    [DataRow(500)]
    public async Task DefinitiveFailuresDoNotSwitchTransport(int status)
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Router.ExecuteAsync(Request(), TestContext.CancellationTokenSource.Token));
        Assert.AreEqual(0, fixture.NasCalls);
    }

    [TestMethod]
    public async Task UserCancellationNeverContactsNas()
    {
        using var cancellation = new CancellationTokenSource();
        using var fixture = new Fixture(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Reply(Request());
        });
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Router.ExecuteAsync(Request(), cancellation.Token));
        Assert.AreEqual(0, fixture.NasCalls);
    }

    [TestMethod]
    public async Task WrongDeviceNeverFallsBackOrReturnsClipboard()
    {
        var request = Request();
        using var fixture = new Fixture((_, _) => Task.FromResult(Reply(request, Guid.NewGuid().ToString("N"))));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Router.ExecuteAsync(request, TestContext.CancellationTokenSource.Token));
        Assert.AreEqual(0, fixture.NasCalls);
    }

    [TestMethod]
    public async Task InvalidJsonDoesNotHideProtocolFailureWithNas()
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json"),
        }));
        await Assert.ThrowsAsync<JsonException>(() => fixture.Router.ExecuteAsync(Request(), TestContext.CancellationTokenSource.Token));
        Assert.AreEqual(0, fixture.NasCalls);
    }

    [TestMethod]
    public async Task BothTransportsOfflineReportFailure()
    {
        using var fixture = new Fixture((_, _) => throw new HttpRequestException("LAN offline"),
            (_, _) => throw new HttpRequestException("NAS offline"));
        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Router.ExecuteAsync(Request(), TestContext.CancellationTokenSource.Token));
        Assert.AreEqual(1, fixture.NasCalls);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => send(request, token);
    }

    private sealed class Fixture : IDisposable
    {
        public int NasCalls { get; private set; }
        public LanFirstRequestRouter Router { get; }

        public Fixture(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> direct,
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? relay = null)
        {
            Router = new(new HttpClient(new Handler(direct)), new HttpClient(new Handler((message, token) =>
            {
                NasCalls++;
                return relay is null ? throw new InvalidOperationException("NAS must not be called.") : relay(message, token);
            })), new(new Uri("http://paired.local:5034/api/lan-first/v1/clipboard"),
                new AuthenticationHeaderValue("Bearer", "lan-only-secret"), false),
                new(new Uri("https://nas.example.test/api/lan-first/v1/devices/" + DeviceId + "/clipboard"),
                    new AuthenticationHeaderValue("Basic", "nas-only-secret"), true),
                DeviceId, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(2));
        }

        public void Dispose() => Router.Dispose();
    }

    public TestContext TestContext { get; set; }
}
