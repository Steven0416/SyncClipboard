using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SyncClipboard.Core.RemoteServer;

public enum ClipboardTransport
{
    Lan,
    Nas,
}

public sealed record RoutedClipboardResponse(ClipboardTransport Transport, LanResponse Response);

/// <summary>A paired endpoint. Credentials are never included in diagnostic string representations.</summary>
public sealed class ClipboardRouteEndpoint
{
    public Uri Address { get; }
    public bool IsNas { get; }
    private readonly AuthenticationHeaderValue _authorization;

    public ClipboardRouteEndpoint(Uri address, AuthenticationHeaderValue authorization, bool isNas)
    {
        if (!address.IsAbsoluteUri || !string.IsNullOrEmpty(address.UserInfo)
            || !string.IsNullOrEmpty(address.Fragment) || !string.IsNullOrEmpty(address.Query)
            || (isNas ? address.Scheme != "https" : address.Scheme is not ("http" or "https")))
        {
            throw new ArgumentException("Use a credential-free HTTP endpoint and HTTPS for NAS.", nameof(address));
        }
        if (authorization.Scheme != (isNas ? "Basic" : "Bearer")
            || string.IsNullOrWhiteSpace(authorization.Parameter))
        {
            throw new ArgumentException("Use a separate paired credential for each transport.", nameof(authorization));
        }
        Address = address;
        IsNas = isNas;
        _authorization = authorization;
    }

    internal void Authenticate(HttpRequestMessage request) => request.Headers.Authorization = _authorization;
}

/// <summary>
/// Sequential LAN-first routing for the v1 live clipboard protocol. Both endpoints must reach the same bridge.
/// </summary>
public sealed class LanFirstRequestRouter : IDisposable
{
    private readonly HttpClient lanClient;
    private readonly HttpClient nasClient;
    private readonly ClipboardRouteEndpoint lan;
    private readonly ClipboardRouteEndpoint nas;
    private readonly string deviceId;
    private readonly TimeSpan lanTimeout;
    private readonly TimeSpan nasTimeout;

    internal LanFirstRequestRouter(HttpClient lanClient, HttpClient nasClient,
        ClipboardRouteEndpoint lan, ClipboardRouteEndpoint nas, string deviceId,
        TimeSpan lanTimeout, TimeSpan nasTimeout)
    {
        if (lan.IsNas || !nas.IsNas)
        {
            throw new ArgumentException("LAN and NAS endpoints must have their corresponding credential types.");
        }
        this.lanClient = lanClient;
        this.nasClient = nasClient;
        this.lan = lan;
        this.nas = nas;
        this.deviceId = deviceId;
        this.lanTimeout = lanTimeout;
        this.nasTimeout = nasTimeout;
    }

    public static LanFirstRequestRouter Create(ClipboardRouteEndpoint lan, ClipboardRouteEndpoint nas, string deviceId)
    {
        if (lan.IsNas || !nas.IsNas)
        {
            throw new ArgumentException("Invalid transport endpoint order.");
        }
        return new(
            new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }),
            new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }),
            lan, nas, deviceId, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(15));
    }

    public void Dispose()
    {
        lanClient.Dispose();
        nasClient.Dispose();
    }

    public async Task<RoutedClipboardResponse> ExecuteAsync(LanRequest request, CancellationToken token = default)
    {
        if (!LanProtocol.IsValid(request) || !Guid.TryParse(deviceId, out _))
        {
            throw new ArgumentException("A valid request and paired device ID are required.", nameof(request));
        }
        if (lanTimeout <= TimeSpan.Zero || nasTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Both transport timeouts must be positive.");
        }
        token.ThrowIfCancellationRequested();
        try
        {
            var direct = await SendAsync(lanClient, lan, request, lanTimeout, token);
            return new(ClipboardTransport.Lan, direct);
        }
        catch (HttpRequestException ex) when (CanFallBack(ex) && !token.IsCancellationRequested)
        {
            // Reuse the exact request, including the write precondition and deduplication ID.
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            // Only our transport deadline expired. User cancellation never starts a second request.
        }
        token.ThrowIfCancellationRequested();
        return new(ClipboardTransport.Nas, await SendAsync(nasClient, nas, request, nasTimeout, token));
    }

    private async Task<LanResponse> SendAsync(HttpClient client, ClipboardRouteEndpoint endpoint,
        LanRequest request, TimeSpan timeout, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint.Address)
        {
            Content = JsonContent.Create(request),
        };
        endpoint.Authenticate(message);
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<LanResponse>(deadline.Token)
            ?? throw new JsonException("Empty clipboard response.");
        if (result.RequestId != request.RequestId
            || !Guid.TryParse(result.DeviceId, out var actualDevice)
            || actualDevice != Guid.Parse(deviceId))
        {
            throw new InvalidDataException("Clipboard response does not match the paired request and device.");
        }
        if (result.Status == "ok" && (result.Profile is null || string.IsNullOrWhiteSpace(result.Profile.Hash)
            || result.Profile.Type != ProfileType.Text || result.Profile.HasData
            || result.Profile.Text.Length > LanProtocol.MaxTextLength))
        {
            throw new InvalidDataException("Invalid text clipboard response.");
        }
        return result;
    }

    private static bool CanFallBack(HttpRequestException error) => error.StatusCode is null
        or HttpStatusCode.RequestTimeout or HttpStatusCode.BadGateway
        or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
}
