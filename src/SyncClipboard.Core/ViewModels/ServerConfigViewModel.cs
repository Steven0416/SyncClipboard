using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Core.UserServices.ServerService;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using SyncClipboard.Core.Clipboard;

namespace SyncClipboard.Core.ViewModels;

public partial class ServerConfigViewModel : ObservableObject
{
    #region server properties
    [ObservableProperty]
    private bool serverEnable;
    partial void OnServerEnableChanged(bool value) => ServerConfig = ServerConfig with { SwitchOn = value };

    [ObservableProperty]
    private bool enableHttps;
    partial void OnEnableHttpsChanged(bool value) => ServerConfig = ServerConfig with { EnableHttps = value };

    public static readonly IEnumerable<string> CertificatePemFileTypes = [".pem"];
    [ObservableProperty]
    private string certificatePemPath = string.Empty;
    partial void OnCertificatePemPathChanged(string value) => ServerConfig = ServerConfig with { CertificatePemPath = value };

    public static readonly IEnumerable<string> CertificatePemKeyFileTypes = [".pem"];
    [ObservableProperty]
    private string certificatePemKeyPath = string.Empty;
    partial void OnCertificatePemKeyPathChanged(string value) => ServerConfig = ServerConfig with { CertificatePemKeyPath = value };

    [ObservableProperty]
    private bool enableCustomConfigurationFile;
    partial void OnEnableCustomConfigurationFileChanged(bool value) => ServerConfig = ServerConfig with { EnableCustomConfigurationFile = value };

    public static readonly IEnumerable<string> CustomConfigurationFileTypes = [".json"];
    [ObservableProperty]
    private string customConfigurationFilePath = string.Empty;
    partial void OnCustomConfigurationFilePathChanged(string value) => ServerConfig = ServerConfig with { CustomConfigurationFilePath = value };

    [ObservableProperty]
    private uint maxHistoryCount;
    partial void OnMaxHistoryCountChanged(uint value) => ServerConfig = ServerConfig with { MaxHistoryCount = value };

    [ObservableProperty]
    private uint historyRetentionMinutes;
    partial void OnHistoryRetentionMinutesChanged(uint value) => ServerConfig = ServerConfig with { HistoryRetentionMinutes = value };

    [RelayCommand]
    private static void OpenCustomConfigDescLink()
    {
        Sys.OpenWithDefaultApp(I18n.Strings.CustomConfigFileLink);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerConfigDescription))]
    private ServerConfig serverConfig = new();
    partial void OnServerConfigChanged(ServerConfig value)
    {
        ServerEnable = value.SwitchOn;
        EnableHttps = value.EnableHttps;
        CertificatePemPath = value.CertificatePemPath;
        CertificatePemKeyPath = value.CertificatePemKeyPath;
        EnableCustomConfigurationFile = value.EnableCustomConfigurationFile;
        CustomConfigurationFilePath = value.CustomConfigurationFilePath;
        MaxHistoryCount = value.MaxHistoryCount;
        HistoryRetentionMinutes = value.HistoryRetentionMinutes;
        _configManager.SetConfig(value);

        OnPropertyChanged(nameof(ShowHttpsConfig));
        OnPropertyChanged(nameof(ShowHttpsCertConfig));
    }

    #endregion

    #region view properties
    public bool ShowHttpsConfig => !EnableCustomConfigurationFile;
    public bool ShowHttpsCertConfig => EnableHttps && !EnableCustomConfigurationFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerConfigDescription))]
    public bool showServerPassword = false;

    public string ServerConfigDescription =>
@$"{I18n.Strings.Port}{new string('\t', int.Parse(I18n.Strings.PortTabRepeat))}: {ServerConfig.Port}
{I18n.Strings.UserName}{new string('\t', int.Parse(I18n.Strings.UserNameTabRepeat))}: {ServerConfig.UserName}
{I18n.Strings.Password}{new string('\t', int.Parse(I18n.Strings.PasswordTabRepeat))}: {GetPasswordString(ServerConfig.Password, ShowServerPassword)}";

    private static string GetPasswordString(string origin, bool? show)
    {
        return show ?? false ? origin : "*********";
    }

    #endregion

    private readonly ConfigManager _configManager;
    private readonly LanFirstService? _lanService;
    private readonly RemoteClipboardServerFactory? _remoteFactory;
    private readonly LanClipboardBridge? _lanClipboard;
    private int _nasModeChange;

    public bool LanEnabled
    {
        get => _configManager.GetConfig<LanFirstConfig>().Enabled;
        set
        {
            _configManager.SetConfig(_configManager.GetConfig<LanFirstConfig>() with { Enabled = value });
            OnPropertyChanged();
        }
    }

    public bool LanAllowPublic
    {
        get => _configManager.GetConfig<LanFirstConfig>().AllowPublicNetworks;
        set
        {
            _configManager.SetConfig(_configManager.GetConfig<LanFirstConfig>() with { AllowPublicNetworks = value });
            OnPropertyChanged();
        }
    }

    public bool LanOnDemandNas
    {
        get => _configManager.GetConfig<LanFirstConfig>().OnDemandNas;
        set
        {
            var revision = Interlocked.Increment(ref _nasModeChange);
            if (value)
            {
                _ = EnableOnDemandNasAsync(revision);
            }
            else
            {
                _configManager.SetConfig(_configManager.GetConfig<LanFirstConfig>() with { OnDemandNas = false });
                _remoteFactory?.ResetCurrentServer();
                OnPropertyChanged();
            }
        }
    }

    private async Task EnableOnDemandNasAsync(int revision)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (_remoteFactory?.Current is not OfficialEventDrivenServer server
                || !await server.SupportsLanFirstAsync(timeout.Token))
            {
                LanStatus = "NAS must support v1 text relay over HTTPS. Upgrade and verify before enabling.";
                return;
            }
            if (revision != Volatile.Read(ref _nasModeChange))
            {
                return;
            }
            _configManager.SetConfig(_configManager.GetConfig<LanFirstConfig>() with { OnDemandNas = true });
            _remoteFactory.ResetCurrentServer();
            RefreshLanStatus();
        }
        catch (Exception ex)
        {
            LanStatus = $"NAS capability check failed: {ex.GetType().Name}; original sync retained.";
        }
        finally
        {
            OnPropertyChanged(nameof(LanOnDemandNas));
        }
    }

    public double LanPort
    {
        get => _configManager.GetConfig<LanFirstConfig>().Port;
        set
        {
            if (value is >= 1 and <= 65535)
            {
                _configManager.SetConfig(_configManager.GetConfig<LanFirstConfig>() with { Port = (ushort)value });
                OnPropertyChanged();
            }
        }
    }

    public string LanHostName => _configManager.GetConfig<LanFirstConfig>().HostName;
    public string LanDeviceId => _configManager.GetConfig<LanFirstConfig>().DeviceId;
    public string LanPairingToken => _configManager.GetConfig<LanFirstConfig>().Token;

    [ObservableProperty]
    private string lanStatus = "Refresh status / 刷新状态";

    [RelayCommand]
    private void RefreshLanStatus()
    {
        var nas = _remoteFactory?.Current as OfficialEventDrivenServer;
        LanStatus = $"{_lanService?.Status ?? "Unavailable"}\n{_lanService?.Addresses}\n"
            + (nas?.OnDemandStatus ?? "NAS: no official server selected")
            + $"\nLast successful transport / 最近成功通道: {_lanClipboard?.LastTransport}";
    }

    [RelayCommand]
    private void RegenerateLanToken()
    {
        _configManager.SetConfig(_configManager.GetConfig<LanFirstConfig>() with
        {
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
        });
        OnPropertyChanged(nameof(LanPairingToken));
        LanStatus = "Token regenerated. Update the paired shortcuts. / 请更新已配对快捷指令。";
    }

    [RelayCommand]
    private static void OpenLanLogs() => Sys.OpenWithDefaultApp(Env.LogFolder);

    [RelayCommand]
    private async Task TestLanConnection()
    {
        var config = _configManager.GetConfig<LanFirstConfig>();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
            {
                Timeout = TimeSpan.FromSeconds(4)
            };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.Token);
            using var response = await client.GetAsync($"http://{config.HostName}:{config.Port}/api/lan-first/v1/capabilities");
            response.EnsureSuccessStatusCode();
            LanStatus = "Windows hostname + HTTP test passed; iPhone access still requires a device test.";
        }
        catch (Exception ex)
        {
            LanStatus = $"LAN test failed: {ex.GetType().Name}. Check network category, mDNS and firewall.";
        }
    }

    public ServerConfigViewModel(ConfigManager configManager, LanFirstService? lanService = null,
        RemoteClipboardServerFactory? remoteFactory = null, LanClipboardBridge? lanClipboard = null)
    {
        _configManager = configManager;
        _lanService = lanService;
        _remoteFactory = remoteFactory;
        _lanClipboard = lanClipboard;
        _configManager.SetConfig(_configManager.GetConfig<LanFirstConfig>());
        _configManager.ListenConfig<ServerConfig>(config => ServerConfig = config);
        serverConfig = _configManager.GetConfig<ServerConfig>();
        serverEnable = serverConfig.SwitchOn;
        enableHttps = serverConfig.EnableHttps;
        certificatePemPath = serverConfig.CertificatePemPath;
        certificatePemKeyPath = serverConfig.CertificatePemKeyPath;
        enableCustomConfigurationFile = serverConfig.EnableCustomConfigurationFile;
        customConfigurationFilePath = serverConfig.CustomConfigurationFilePath;
        maxHistoryCount = serverConfig.MaxHistoryCount;
        historyRetentionMinutes = serverConfig.HistoryRetentionMinutes;
    }

    public string? SetServerConfig(string portString, string username, string password)
    {
        if (!ushort.TryParse(portString, out var port))
        {
            return I18n.Strings.PortRangeIs;
        }
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            return I18n.Strings.UsernameOrPasswordBlank;
        }

        ServerConfig = ServerConfig with { Password = password, Port = port, UserName = username };

        return null;
    }
}
