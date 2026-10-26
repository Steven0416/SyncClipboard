using System.Net;
using SyncClipboard.Core.Utilities.Network;

namespace SyncClipboard.Test;

[TestClass]
public sealed class MdnsRecoveryTests
{
    private static readonly LanAddress Address = new(IPAddress.Parse("192.0.2.10"), 6);
    private static readonly string Device = Guid.NewGuid().ToString("N");

    [TestMethod]
    public async Task MissingRecordRecoversEvenWhenRegistrationThreadIsAlive()
    {
        var clock = new Clock();
        var first = new Publisher();
        var second = new Publisher();
        var pending = new Queue<Publisher>([first, second]);
        await using var supervisor = new MdnsRegistrationSupervisor(() => pending.Dequeue(), clock);
        await supervisor.EnsureAsync([Address], "recovery.local", 5034, Device, false, CancellationToken.None);
        first.RecordExists = false;
        clock.Advance();
        await supervisor.EnsureAsync([Address], "recovery.local", 5034, Device, false, CancellationToken.None);
        Assert.IsTrue(first.Disposed);
        Assert.AreEqual((Address.Address, Address.InterfaceIndex, "recovery.local", (ushort)5034, Device), second.Registration);
        StringAssert.Contains(supervisor.Status, "verified");
    }

    [TestMethod]
    public async Task DaemonFailureRetriesWithoutChangingIdentity()
    {
        var clock = new Clock();
        var unavailable = new Publisher { FailRegistration = true };
        var recovered = new Publisher();
        var pending = new Queue<Publisher>([unavailable, recovered]);
        await using var supervisor = new MdnsRegistrationSupervisor(() => pending.Dequeue(), clock);
        await supervisor.EnsureAsync([Address], "recovery.local", 5034, Device, false, CancellationToken.None);
        StringAssert.Contains(supervisor.Status, "retrying");
        Assert.IsTrue(unavailable.Disposed);
        await supervisor.EnsureAsync([Address], "recovery.local", 5034, Device, false, CancellationToken.None);
        Assert.AreEqual(Device, recovered.Registration.Device);
        StringAssert.Contains(supervisor.Status, "verified");
    }

    [TestMethod]
    public async Task SameIpNetworkReconnectRepublishesAndDisconnectWithdraws()
    {
        var publishers = new List<Publisher>();
        await using var supervisor = new MdnsRegistrationSupervisor(() =>
        {
            var publisher = new Publisher();
            publishers.Add(publisher);
            return publisher;
        }, new Clock());
        await supervisor.EnsureAsync([Address], "recovery.local", 5034, Device, false, CancellationToken.None);
        await supervisor.EnsureAsync([Address], "recovery.local", 5034, Device, true, CancellationToken.None);
        Assert.AreEqual(2, publishers.Count);
        Assert.IsTrue(publishers[0].Disposed);
        await supervisor.EnsureAsync([], "recovery.local", 5034, Device, false, CancellationToken.None);
        Assert.IsTrue(publishers[1].Disposed);
        StringAssert.Contains(supervisor.Status, "offline");
    }

    [TestMethod]
    public async Task AddressChangeWithdrawsTheOldAddress()
    {
        var first = new Publisher();
        var second = new Publisher();
        var pending = new Queue<Publisher>([first, second]);
        await using var supervisor = new MdnsRegistrationSupervisor(() => pending.Dequeue(), new Clock());
        await supervisor.EnsureAsync([Address], "recovery.local", 5034, Device, false, CancellationToken.None);
        var changed = Address with { Address = IPAddress.Parse("192.0.2.20") };
        await supervisor.EnsureAsync([changed], "recovery.local", 5034, Device, false, CancellationToken.None);
        Assert.IsTrue(first.Disposed);
        Assert.AreEqual(changed.Address, second.Registration.Address);
    }

    [TestMethod]
    public async Task HealthyRecordsAreNotContinuouslyWithdrawn()
    {
        var publisher = new Publisher();
        var clock = new Clock();
        var creates = 0;
        await using var supervisor = new MdnsRegistrationSupervisor(() => { creates++; return publisher; }, clock);
        await supervisor.EnsureAsync([Address], "recovery.local", 5034, Device, false, CancellationToken.None);
        clock.Advance();
        await supervisor.EnsureAsync([Address], "recovery.local", 5034, Device, false, CancellationToken.None);
        Assert.AreEqual(1, creates);
        Assert.IsFalse(publisher.Disposed);
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance() => _now = _now.AddSeconds(31);
    }

    private sealed class Publisher : IBonjourPublisher
    {
        public bool IsRunning => !Disposed;
        public bool Disposed { get; private set; }
        public bool RecordExists { get; set; } = true;
        public bool FailRegistration { get; init; }
        public (IPAddress Address, uint Interface, string Host, ushort Port, string Device) Registration { get; private set; }
        public Task RegisterAsync(IPAddress address, uint interfaceIndex, string hostName, ushort port,
            string deviceId, CancellationToken token = default)
        {
            if (FailRegistration)
            {
                throw new IOException("Simulated daemon unavailable.");
            }
            Registration = (address, interfaceIndex, hostName, port, deviceId);
            return Task.CompletedTask;
        }
        public Task<bool> VerifyAsync(IPAddress address, uint interfaceIndex, string hostName, CancellationToken token)
            => Task.FromResult(RecordExists);
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
