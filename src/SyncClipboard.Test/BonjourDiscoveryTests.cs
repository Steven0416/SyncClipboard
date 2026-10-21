using System.Text;
using SyncClipboard.Core.Utilities.Network;

namespace SyncClipboard.Test;

[TestClass]
public sealed class BonjourDiscoveryTests
{
    [TestMethod]
    [DataRow("clipbridge-test.local", true)]
    [DataRow("ClipBridge-Test.LOCAL", true)]
    [DataRow("clipbridge.local.example.com", false)]
    [DataRow("a.b.local", false)]
    [DataRow("-invalid.local", false)]
    [DataRow("invalid-.local", false)]
    [DataRow("含空格.local", false)]
    [DataRow(".local", false)]
    public void HostNameMustBeOneDnsLabelInLocalDomain(string name, bool expected)
    {
        Assert.AreEqual(expected, BonjourMdnsPublisher.IsValidHostName(name));
    }

    [TestMethod]
    public void TxtRecordsContainOnlyDeviceIdentityVersionAndPort()
    {
        var deviceId = Guid.NewGuid().ToString("N");
        var record = BonjourMdnsPublisher.BuildTxtRecord(deviceId, 5033);
        var values = new List<string>();
        var offset = 0;
        while (offset < record.Length)
        {
            var length = record[offset++];
            Assert.IsTrue(offset + length <= record.Length);
            values.Add(Encoding.UTF8.GetString(record.AsSpan(offset, length)));
            offset += length;
        }
        CollectionAssert.AreEquivalent(new[] { "version=1", "deviceId=" + deviceId, "port=5033" }, values);
    }
}
