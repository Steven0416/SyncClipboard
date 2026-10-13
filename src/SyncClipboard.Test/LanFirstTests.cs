using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Server.Core.Services;
using SyncClipboard.Shared;
using SyncClipboard.Shared.Profiles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using SyncClipboard.Core.UserServices.ServerService;
using SyncClipboard.Core.ViewModels;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SyncClipboard.Test;

[TestClass]
public sealed class LanFirstTests
{
    private static LanRequest Get() => new(Guid.NewGuid(), Guid.NewGuid().ToString("N"), "get");

    [TestMethod]
    public async Task GetReadsCurrentClipboardWithoutRemoteStorage()
    {
        using var fixture = new Fixture();
        fixture.Text = "中文 English 😀\nsecond line";
        var first = await fixture.Bridge.ExecuteAsync(Get(), CancellationToken.None);
        Assert.AreEqual(fixture.Text, first.Profile!.Text);
        Assert.IsFalse(first.IsCache);
        fixture.Text = "copied after first request";
        var second = await fixture.Bridge.ExecuteAsync(Get(), CancellationToken.None);
        Assert.AreEqual(fixture.Text, second.Profile!.Text);
        Assert.AreEqual(0, fixture.Writes);
    }

    [TestMethod]
    public async Task SetUsesExistingSetterAndDuplicateCannotOverwriteLaterCopy()
    {
        using var fixture = new Fixture();
        var current = await fixture.Bridge.ExecuteAsync(Get(), CancellationToken.None);
        var request = new LanRequest(Guid.NewGuid(), Guid.NewGuid().ToString("N"), "set", "from iPhone", current.Profile!.Hash);
        Assert.AreEqual("ok", (await fixture.Bridge.ExecuteAsync(request, CancellationToken.None)).Status);
        Assert.AreEqual("from iPhone", fixture.Text);
        fixture.Text = "new local copy";
        var duplicate = await fixture.Bridge.ExecuteAsync(request, CancellationToken.None);
        Assert.IsTrue(duplicate.IsCache);
        Assert.AreEqual("new local copy", fixture.Text);
        Assert.AreEqual(1, fixture.Writes);
        Assert.AreEqual(1, fixture.PullStoppedMessages);
    }

    [TestMethod]
    public async Task StaleNasHashCannotReplaceNewLocalClipboard()
    {
        using var fixture = new Fixture();
        var current = await fixture.Bridge.ExecuteAsync(Get(), CancellationToken.None);
        fixture.Text = "new local copy";
        var request = new LanRequest(Guid.NewGuid(), Guid.NewGuid().ToString("N"), "set", "old remote", current.Profile!.Hash);
        Assert.AreEqual("conflict", (await fixture.Bridge.ExecuteAsync(request, CancellationToken.None)).Status);
        Assert.AreEqual("new local copy", fixture.Text);
        Assert.AreEqual(0, fixture.Writes);
    }

    [TestMethod]
    public async Task ConcurrentWritesWithSamePreconditionHaveOneWinner()
    {
        using var fixture = new Fixture();
        var current = await fixture.Bridge.ExecuteAsync(Get(), CancellationToken.None);
        var source = Guid.NewGuid().ToString("N");
        var replies = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
            fixture.Bridge.ExecuteAsync(new(Guid.NewGuid(), source, "set", $"copy {index}", current.Profile!.Hash), CancellationToken.None)));
        Assert.AreEqual(1, replies.Count(reply => reply.Status == "ok"));
        Assert.AreEqual(7, replies.Count(reply => reply.Status == "conflict"));
        Assert.AreEqual(1, fixture.Writes);
    }

    [TestMethod]
    public async Task ReusingRequestIdForDifferentPayloadIsRejected()
    {
        using var fixture = new Fixture();
        var request = Get();
        await fixture.Bridge.ExecuteAsync(request, CancellationToken.None);
        var result = await fixture.Bridge.ExecuteAsync(request with { SourceDeviceId = Guid.NewGuid().ToString() }, CancellationToken.None);
        Assert.AreEqual("request-id-conflict", result.Status);
    }

    [TestMethod]
    public async Task SensitiveClipboardIsNotReturned()
    {
        using var fixture = new Fixture();
        fixture.Clipboard.Setup(item => item.GetMetaInfomation(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClipboardMetaInfomation { ExcludeForSync = true });
        var result = await fixture.Bridge.ExecuteAsync(Get(), CancellationToken.None);
        Assert.AreEqual("filtered", result.Status);
        Assert.IsNull(result.Profile);
    }

    [TestMethod]
    public async Task InvalidRequestsDoNotTouchClipboard()
    {
        using var fixture = new Fixture();
        var result = await fixture.Bridge.ExecuteAsync(new(Guid.Empty, "invalid", "set", "secret"), CancellationToken.None);
        Assert.AreEqual("invalid-request", result.Status);
        fixture.Clipboard.Verify(item => item.CreateProfileFromLocal(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DisconnectedRelayNeverPretendsToReturnFreshContent()
    {
        var relay = new LanRelay(Mock.Of<Microsoft.AspNetCore.SignalR.IHubContext<SyncClipboard.Server.Core.Hubs.SyncClipboardHub>>());
        var result = await relay.RequestAsync(Guid.NewGuid().ToString("N"), Get(), CancellationToken.None);
        Assert.AreEqual("offline", result.Status);
        Assert.IsNull(result.Profile);
    }

    [TestMethod]
    public void IdentityIsUniqueAndLanDisabledByDefault()
    {
        var first = new LanFirstConfig();
        var second = new LanFirstConfig();
        Assert.AreNotEqual(first.DeviceId, second.DeviceId);
        Assert.AreNotEqual(first.Token, second.Token);
        Assert.AreEqual(64, first.Token.Length);
        Assert.IsFalse(first.Enabled);
        Assert.IsFalse(first.AllowPublicNetworks);
        Assert.IsFalse(first.OnDemandNas);
    }

    [TestMethod]
    public void OnDemandCannotDisableOriginalSyncWithoutAnUpgradedNas()
    {
        using var fixture = new Fixture();
        var settings = new ServerConfigViewModel(fixture.Config)
        {
            LanOnDemandNas = true
        };
        Assert.IsFalse(fixture.Config.GetConfig<LanFirstConfig>().OnDemandNas);
        Assert.Contains("NAS must support", settings.LanStatus);
    }

    [TestMethod]
    public async Task LanHttpRejectsMissingAndRevokedTokensAndUntrustedNetworks()
    {
        using var fixture = new Fixture();
        var config = fixture.Config.GetConfig<LanFirstConfig>() with { Enabled = true };
        fixture.Config.SetConfig(config);
        var trusted = true;
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        LanApi.Map(app, fixture.Config, fixture.Bridge, _ => trusted);
        await app.StartAsync(TestContext.CancellationTokenSource.Token);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(app.Urls.Single()) };
        const string endpoint = "/api/lan-first/v1/clipboard";
        using var missing = await client.PostAsJsonAsync(endpoint, Get(), TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(HttpStatusCode.Unauthorized, missing.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.Token);
        using var accepted = await client.PostAsJsonAsync(endpoint, Get(), TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
        fixture.Config.SetConfig(config with { Token = new LanFirstConfig().Token });
        using var revoked = await client.PostAsJsonAsync(endpoint, Get(), TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(HttpStatusCode.Unauthorized, revoked.StatusCode);
        trusted = false;
        using var publicNetwork = await client.PostAsJsonAsync(endpoint, Get(), TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(HttpStatusCode.Forbidden, publicNetwork.StatusCode);
        await app.StopAsync(TestContext.CancellationTokenSource.Token);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "SyncClipboard-LanTest-" + Guid.NewGuid());
        private readonly ConfigurationTestServices _migration = new();
        private readonly ServiceProvider _services;
        public Mock<IClipboardFactory> Clipboard { get; } = new();
        public string Text { get; set; } = "initial";
        public int Writes { get; private set; }
        public int PullStoppedMessages { get; private set; }
        public LanClipboardBridge Bridge { get; }
        public ConfigManager Config { get; }

        public Fixture()
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, "config.json");
            File.WriteAllText(path, """{"ConfigVersion":1}""");
            var config = new ConfigManager(path, _migration.Upgrader);
            Config = config;
            config.SetConfig(new LanFirstConfig());
            Clipboard.Setup(item => item.CreateProfileFromLocal(It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult<Profile>(new TextProfile(Text)));
            Clipboard.Setup(item => item.CreateProfileFromMeta(It.IsAny<ClipboardMetaInfomation>(), It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult<Profile>(new TextProfile(Text)));
            Clipboard.Setup(item => item.GetMetaInfomation(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ClipboardMetaInfomation());
            var setter = new Mock<IClipboardSetter<TextProfile>>();
            setter.Setup(item => item.SetLocalClipboard(It.IsAny<ClipboardMetaInfomation>(), It.IsAny<CancellationToken>()))
                .Callback<ClipboardMetaInfomation, CancellationToken>((meta, _) =>
                {
                    Text = meta.Text!;
                    Writes++;
                })
                .Returns(Task.CompletedTask);
            var dispatcher = new Mock<IThreadDispatcher>();
            dispatcher.Setup(item => item.RunOnMainThreadAsync(It.IsAny<Func<Task>>())).Returns<Func<Task>>(action => action());
            var env = new Mock<IProfileEnv>();
            env.Setup(item => item.GetPersistentDir()).Returns(_directory);
            _services = new ServiceCollection().AddSingleton(setter.Object).BuildServiceProvider();
            var messenger = new StrongReferenceMessenger();
            messenger.Register<Profile, string>(this, SyncClipboard.Core.UserServices.SyncService.PULL_STOP_ENENT_NAME,
                (_, _) => PullStoppedMessages++);
            Bridge = new(Clipboard.Object, new LocalClipboardSetter(_services, dispatcher.Object, env.Object), config, messenger);
        }

        public void Dispose()
        {
            _services.Dispose();
            _migration.Dispose();
            Directory.Delete(_directory, true);
        }
    }

    public TestContext TestContext { get; set; }
}
