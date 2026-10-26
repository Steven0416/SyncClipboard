using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClipBridge.Setup;

Console.OutputEncoding = Encoding.UTF8;
try
{
    if (args.Length != 2 || args[0] is not ("--configure" or "--pair" or "--check"))
    {
        throw new ArgumentException("Run Setup.cmd from the extracted ClipBridge folder.");
    }
    var client = Path.GetFullPath(args[1]);
    if (!File.Exists(Path.Combine(client, "SyncClipboard.exe"))
        || !File.Exists(Path.Combine(client, "SyncClipboard.pri"))
        || !File.Exists(Path.Combine(client, "Assets", "icon.ico")))
    {
        throw new IOException("完整解压 ZIP 后再运行，客户端文件不完整。");
    }
    if (args[0] == "--check")
    {
        Console.WriteLine("Windows client files verified.");
        return 0;
    }
    if (!OperatingSystem.IsWindows())
    {
        throw new PlatformNotSupportedException();
    }
    var configPath = Path.Combine(client, "SyncClipboard.json");
    if (args[0] == "--configure" && !File.Exists(configPath))
    {
        if (!File.Exists(Path.Combine(Environment.SystemDirectory, "dnssd.dll")))
        {
            throw new IOException("请先安装 Apple Bonjour，然后重新运行 Setup.cmd。官方链接见 README.md。");
        }
        Console.WriteLine("ClipBridge：仅配置本解压目录，不改其他 SyncClipboard 或 NAS。\n请选择当前可信的物理网卡：");
        var processInfo = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, UseShellExecute = false,
            CreateNoWindow = true };
        processInfo.ArgumentList.Add("-NoProfile");
        processInfo.ArgumentList.Add("-Command");
        processInfo.ArgumentList.Add("[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); @(Get-NetAdapter -Physical | Where-Object Status -eq 'Up' | Select-Object Name,InterfaceGuid) | ConvertTo-Json -Compress");
        using var process = Process.Start(processInfo)!;
        var adapterJson = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        using var adapters = JsonDocument.Parse(adapterJson);
        var list = adapters.RootElement.ValueKind == JsonValueKind.Array ? adapters.RootElement.EnumerateArray().ToArray()
            : [adapters.RootElement];
        if (list.Length == 0)
        {
            throw new IOException("请连接 Wi-Fi 或有线网络。");
        }
        for (var index = 0; index < list.Length; index++)
        {
            Console.WriteLine($"{index + 1}. {list[index].GetProperty("Name").GetString()}");
        }
        var choice = int.Parse(Prompt("网卡编号")) - 1;
        if (choice < 0 || choice >= list.Length)
        {
            throw new ArgumentException("网卡编号无效。");
        }
        var adapter = Guid.Parse(list[choice].GetProperty("InterfaceGuid").GetString()!);
        var allowPublic = Prompt("这张网卡是否为你信任的局域网？允许 Windows 公共网络类别使用它？输入 YES 确认") == "YES";
        var nas = Prompt("NAS HTTPS 基地址，或本机导出的 RelayConnection.private.json 路径");
        string user, password;
        if (File.Exists(nas.Trim('"')))
        {
            using var connection = JsonDocument.Parse(File.ReadAllText(nas.Trim('"')));
            nas = connection.RootElement.GetProperty("nasUrl").GetString()!;
            user = connection.RootElement.GetProperty("userName").GetString()!;
            password = connection.RootElement.GetProperty("password").GetString()!;
        }
        else
        {
            user = Prompt("NAS 中继用户名");
            password = Password();
        }
        var device = Guid.NewGuid().ToString("N");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var payload = PairingFiles.CreatePayload(device, Guid.NewGuid().ToString("N"), token, nas, user, password);
        Console.WriteLine("正在验证 HTTPS 中继能力，尚未保存配置……");
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password)));
        using var response = await http.GetAsync(nas.TrimEnd('/') + "/api/lan-first/v1/capabilities");
        response.EnsureSuccessStatusCode();
        using var capabilities = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (capabilities.RootElement.GetProperty("version").GetInt32() != 1)
        {
            throw new IOException("NAS 不支持本版本中继协议，请使用已部署的 0.2 服务。");
        }
        var config = PairingFiles.NewConfiguration(adapter, nas, user, password, allowPublic, device, token);
        // Config is created exclusively, never overwrite an existing identity/account.
        using (var file = new FileStream(configPath, FileMode.CreateNew))
        {
            JsonSerializer.Serialize(file, config, new JsonSerializerOptions { WriteIndented = true });
        }
        File.WriteAllText(Path.Combine(client, "StaticConfig.json"),
            """{"Env":{"PortableUserConfig":true,"PortableAppDataFolder":true}}""");
        PairingFiles.Write(client, payload);
    }
    else
    {
        using var config = JsonDocument.Parse(File.ReadAllText(configPath));
        var root = config.RootElement;
        var lan = root.GetProperty("LanFirst");
        var accountId = root.GetProperty("Account").GetProperty("AccountId").GetString()!;
        var account = root.GetProperty("SavedAccounts").GetProperty("SyncClipboard").GetProperty(accountId);
        var oldPair = Path.Combine(client, "pairing-private", "pairing.json");
        var phone = Guid.NewGuid().ToString("N");
        if (File.Exists(oldPair))
        {
            using var saved = JsonDocument.Parse(File.ReadAllText(oldPair));
            phone = saved.RootElement.GetProperty("phoneDeviceId").GetString()!;
        }
        PairingFiles.Write(client, PairingFiles.CreatePayload(lan.GetProperty("DeviceId").GetString()!, phone,
            lan.GetProperty("Token").GetString()!, account.GetProperty("RemoteURL").GetString()!,
            account.GetProperty("UserName").GetString()!, account.GetProperty("Password").GetString()!));
    }
    var qrPath = Path.Combine(client, "pairing-private", "pairing.png");
    using (var savedConfig = JsonDocument.Parse(File.ReadAllText(configPath)))
    {
        var root = savedConfig.RootElement;
        var id = root.GetProperty("Account").GetProperty("AccountId").GetString()!;
        var account = root.GetProperty("SavedAccounts").GetProperty("SyncClipboard").GetProperty(id);
        File.WriteAllText(Path.Combine(client, "pairing-private", "RelayConnection.private.json"),
            JsonSerializer.Serialize(new { nasUrl = account.GetProperty("RemoteURL").GetString(),
                userName = account.GetProperty("UserName").GetString(), password = account.GetProperty("Password").GetString() }));
    }
    Console.WriteLine("配置与本机配对二维码已准备。请运行手机 ClipBridge → 扫码配对。\n二维码含凭据，请勿分享或上传。");
    Process.Start(new ProcessStartInfo(qrPath) { UseShellExecute = true });
    return 0;
}
catch (Exception ex)
{
    // HTTP exceptions have no auth headers; JSON/key lookup errors never print the source JSON.
    Console.Error.WriteLine($"设置未完成：{ex.GetType().Name}。请检查输入、完整解压目录、Bonjour 和 HTTPS 中继连接。");
    return 1;
}

static string Prompt(string label)
{
    Console.Write(label + ": ");
    return Console.ReadLine()?.Trim() ?? "";
}

static string Password()
{
    Console.Write("NAS 密码（输入不显示）: ");
    var result = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return result.ToString();
        }
        if (key.Key == ConsoleKey.Backspace && result.Length > 0)
        {
            result.Length--;
        }
        else if (!char.IsControl(key.KeyChar))
        {
            result.Append(key.KeyChar);
        }
    }
}
