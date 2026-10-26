using System.Text;
using System.Text.Json;
using ClipBridge.Setup;

namespace SyncClipboard.Test;

[TestClass]
public sealed class ClipBridgeSetupTests
{
    [TestMethod]
    [DataRow("http://relay.example/")]
    [DataRow("https://user:password@relay.example/")]
    [DataRow("https://relay.example/?password=secret")]
    [DataRow("https://relay.example/#secret")]
    public void PairingRejectsUnsafeNasAddresses(string nas)
    {
        Assert.Throws<ArgumentException>(() => PairingFiles.CreatePayload(Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N"), new string('a', 64), nas, "test", "password"));
    }

    [TestMethod]
    public void PairingUsesSeparateChannelCredentialsAndRoutesToTheNewComputer()
    {
        var device = Guid.NewGuid().ToString("N");
        var phone = Guid.NewGuid().ToString("N");
        var token = new string('b', 64);
        using var json = JsonDocument.Parse(PairingFiles.CreatePayload(device, phone, token,
            "https://relay.example:8788/", "test", "中文-password"));
        var pair = json.RootElement;
        Assert.AreEqual(phone, pair.GetProperty("phoneDeviceId").GetString());
        Assert.AreEqual($"http://syncclip-{device}.local:5034/api/lan-first/v1/clipboard", pair.GetProperty("lanUrl").GetString());
        Assert.AreEqual($"https://relay.example:8788/api/lan-first/v1/devices/{device}/clipboard", pair.GetProperty("nasUrl").GetString());
        Assert.AreEqual("Bearer " + token, pair.GetProperty("lanAuthorization").GetString());
        Assert.AreEqual("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("test:中文-password")),
            pair.GetProperty("nasAuthorization").GetString());
    }

    [TestMethod]
    public void NewComputerConfigurationKeepsTheChosenInterfaceAndStableIdentity()
    {
        var device = Guid.NewGuid().ToString("N");
        var adapter = Guid.NewGuid();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(PairingFiles.NewConfiguration(adapter,
            "https://relay.example/", "test", "password", true, device, new string('c', 64))));
        var config = json.RootElement;
        Assert.AreEqual(device, config.GetProperty("LanFirst").GetProperty("DeviceId").GetString());
        Assert.AreEqual(adapter.ToString(), config.GetProperty("LanFirst").GetProperty("InterfaceId").GetString());
        Assert.IsTrue(config.GetProperty("LanFirst").GetProperty("OnDemandNas").GetBoolean());
        Assert.IsFalse(config.GetProperty("ServerService").GetProperty("SwitchOn").GetBoolean());
        Assert.IsFalse(config.GetProperty("Program").GetProperty("CheckUpdateOnStartUp").GetBoolean());
    }
}
