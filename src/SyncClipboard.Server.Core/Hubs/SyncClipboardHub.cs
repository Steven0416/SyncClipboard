using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Services;

namespace SyncClipboard.Server.Core.Hubs;

public interface ISyncClipboardClient
{
    Task RemoteProfileChanged(ProfileDto profile);
    Task RemoteHistoryChanged(HistoryRecordDto historyRecordDto);
}

[Authorize]
public class SyncClipboardHub(LanRelay relay) : Hub<ISyncClipboardClient>
{
    public void RegisterLanDeviceV1(string deviceId) => relay.Register(deviceId, Context.ConnectionId);

    public void CompleteLanRequestV1(LanResponse response) => relay.Complete(Context.ConnectionId, response);

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        relay.Disconnect(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
