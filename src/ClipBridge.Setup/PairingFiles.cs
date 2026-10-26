using System.Text;
using System.Text.Json;
using QRCoder;

namespace ClipBridge.Setup;

public static class PairingFiles
{
    public static string CreatePayload(string device, string phone, string token, string nas, string user, string password)
    {
        _ = Guid.Parse(device);
        _ = Guid.Parse(phone);
        var uri = new Uri(nas, UriKind.Absolute);
        if (uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0
            || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(user) || user.Contains(':') || password.Length == 0)
        {
            throw new ArgumentException("Use an HTTPS NAS base URL and valid credentials.");
        }
        return JsonSerializer.Serialize(new
        {
            version = 1, deviceId = device, phoneDeviceId = phone,
            lanUrl = $"http://syncclip-{device}.local:5034/api/lan-first/v1/clipboard",
            lanAuthorization = "Bearer " + token,
            nasUrl = nas.TrimEnd('/') + $"/api/lan-first/v1/devices/{device}/clipboard",
            nasAuthorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password))
        });
    }

    public static byte[] CreateQr(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var image = new PngByteQRCode(data);
        return image.GetGraphic(8);
    }

    public static void Write(string client, string payload)
    {
        var directory = Path.Combine(client, "pairing-private");
        Directory.CreateDirectory(directory);
        // The same phone identity survives QR regeneration and application upgrades.
        File.WriteAllText(Path.Combine(directory, "pairing.json"), payload, new UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(directory, "pairing.png"), CreateQr(payload));
    }

    public static object NewConfiguration(Guid adapter, string nas, string user, string password, bool allowPublic,
        string device, string token)
    {
        _ = CreatePayload(device, Guid.NewGuid().ToString("N"), token, nas, user, password);
        return new
        {
            ConfigVersion = 3,
            Account = new { AccountId = "1", AccountType = "SyncClipboard" },
            SavedAccounts = new Dictionary<string, object>
            {
                ["SyncClipboard"] = new Dictionary<string, object>
                {
                    ["1"] = new { RemoteURL = nas.TrimEnd('/') + "/", UserName = user, Password = password,
                        CustomName = "ClipBridge relay", DeletePreviousFilesOnPush = true }
                }
            },
            SyncService = new { SyncSwitchOn = true, PullSwitchOn = true, PushSwitchOn = true },
            ServerService = new { SwitchOn = false },
            History = new { EnableHistory = false, EnableSyncHistory = false },
            Program = new { CheckUpdateOnStartUp = false, AutoDownloadUpdate = false, HideWindowOnStartup = true,
                DiagnoseMode = false, Language = "zh-CN" },
            LanFirst = new { Enabled = true, Port = 5034, InterfaceId = adapter.ToString(),
                AllowPublicNetworks = allowPublic, OnDemandNas = true, DeviceId = device, Token = token }
        };
    }
}
