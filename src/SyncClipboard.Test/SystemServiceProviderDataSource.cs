using NativeNotification.Interface;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Core.UserServices.ServerService;
using System.Reflection;

namespace SyncClipboard.Test;

[AttributeUsage(AttributeTargets.Method)]
public class SystemServiceProviderDataSource : ServiceProviderDataSourceBase
{
    public override IEnumerable<object[]> GetData(MethodInfo methodInfo)
    {
        Type[] requiredService = [
            typeof(IServiceProvider),
            typeof(ConfigManager),
            typeof(IAppConfig),
            typeof(IUpdateInstallerFactory),
            typeof(ILogger),
            typeof(RemoteClipboardServerFactory),
            typeof(LanClipboardBridge),
            typeof(LanFirstService),
            typeof(IHttp),
            typeof(INotificationManager)
        ];

        List<object[]> res = [];

        foreach (Type serviceType in requiredService)
        {
            res.Add([serviceType]);
        }

        return res;
    }
}
