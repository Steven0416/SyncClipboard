using Microsoft.Extensions.DependencyInjection;
using Moq;
using SyncClipboard.Core.Interfaces;
using System.Text.Json;
using SyncClipboard.WinUI3;

namespace SyncClipboard.Test.WinUI3;

[TestClass]
public class ServiceProvider
{
    Microsoft.Extensions.DependencyInjection.ServiceProvider? Services { get; set; }
    private ConfigurationTestServices? _configurationServices;
    private DirectoryInfo? _directory;

    [TestInitialize]
    public void InitializeServices()
    {
        var servicesCollection = AppServices.ConfigureServices(new Mock<IThreadDispatcher>().Object);
        _directory = Directory.CreateTempSubdirectory("SyncClipboard.WinUI.DI-");
        var configPath = Path.Combine(_directory.FullName, "SyncClipboard.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(new
        {
            ConfigVersion = SyncClipboard.Core.Commons.Env.SyncClipboardConfigVersion
        }));
        _configurationServices = new ConfigurationTestServices();
        servicesCollection.AddSingleton(_configurationServices.CreateConfigManager(configPath));
        servicesCollection.AddSingleton<IMainWindow>(new Mock<IMainWindow>().Object);
        servicesCollection.AddSingleton<IContextMenu>(new Mock<IContextMenu>().Object);
        Services = servicesCollection.BuildServiceProvider();
    }

    [TestCleanup]
    public void CleanupServices()
    {
        Services?.Dispose();
        _configurationServices?.Dispose();
        if (_directory is not null
            && _directory.Name.StartsWith("SyncClipboard.WinUI.DI-", StringComparison.Ordinal)
            && Path.GetFullPath(_directory.Parent!.FullName).TrimEnd(Path.DirectorySeparatorChar)
                .Equals(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            _directory.Delete(true);
        }
    }

    [TestMethod]
    [SystemServiceProviderDataSource]
    [PlatformServiceProviderDataSource]
    public void ConfigedServices(Type type)
    {
        Assert.IsNotNull(Services?.GetService(type));
    }
}
