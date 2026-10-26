namespace SyncClipboard.Shared;

/// <summary>Versioned, text-only direct/relay protocol. Hashes are preconditions, never device clocks.</summary>
public sealed record LanRequest(Guid RequestId, string SourceDeviceId, string Operation,
    string? Text = null, string? ExpectedHash = null);

public sealed record LanResponse(Guid RequestId, string DeviceId, string Status,
    ProfileDto? Profile = null, DateTimeOffset? UpdatedAt = null, bool IsCache = false);

public static class LanProtocol
{
    public const string RequestEvent = "LanClipboardRequestedV1";
    public const int MaxTextLength = 262144;

    public static bool IsValid(LanRequest request) => request.RequestId != Guid.Empty
        && Guid.TryParse(request.SourceDeviceId, out _)
        && (request.Operation == "get" || request.Operation == "set"
            && request.Text is { Length: <= MaxTextLength } && !string.IsNullOrWhiteSpace(request.ExpectedHash));
}
