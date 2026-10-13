using System.Security.Cryptography;
using SyncClipboard.Shared.Attributes;

namespace SyncClipboard.Core.Models.UserConfigs;

[ConfigKey("LanFirst", ConfigStorage.SyncClipboard)]
public sealed record LanFirstConfig
{
    public bool Enabled { get; set; }
    public bool AllowPublicNetworks { get; set; }
    public bool OnDemandNas { get; set; }
    public ushort Port { get; set; } = 5034;
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public string Token { get; set; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public string HostName => $"syncclip-{DeviceId}.local";
}
