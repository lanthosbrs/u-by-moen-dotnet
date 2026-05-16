using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UByMoen.Core.Exceptions;
using UByMoen.Core.Models;

namespace UByMoen.Core.Client;

public class MoenApiClient : IMoenApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MoenApiClient> _logger;
    private readonly string _email;
    private readonly string _password;
    private readonly IMoenPusherClient _pusherClient;

    private string? _token;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public MoenApiClient(
        HttpClient httpClient,
        IMoenPusherClient pusherClient,
        string email,
        string password,
        ILogger<MoenApiClient> logger)
    {
        _httpClient = httpClient;
        _pusherClient = pusherClient;
        _email = email;
        _password = password;
        _logger = logger;
    }

    public async Task<string> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        var url = $"{MoenConstants.ApiBaseUrl}{MoenConstants.ApiAuthenticate}"
                + $"?email={Uri.EscapeDataString(_email)}&password={Uri.EscapeDataString(_password)}";

        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var data = JsonSerializer.Deserialize<JsonElement>(body, JsonOptions);

            _token = data.GetProperty("token").GetString()
                ?? throw new MoenAuthException("No token received from authentication");

            _logger.LogDebug("Successfully authenticated with Moen API");
            return _token;
        }
        catch (HttpRequestException ex)
        {
            throw new MoenAuthException($"Authentication failed: {ex.Message}", ex);
        }
    }

    public async Task<PusherCredentials> GetPusherCredentialsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        using var request = BuildRequest(HttpMethod.Get, MoenConstants.ApiCredentials);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var creds = JsonSerializer.Deserialize<PusherCredentials>(body, JsonOptions)
            ?? throw new MoenApiException("Failed to deserialize Pusher credentials");

        _logger.LogDebug("Got Pusher credentials: key={Key}, cluster={Cluster}", creds.AppKey, creds.Cluster);
        return creds;
    }

    public async Task<List<ShowerDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        using var request = BuildRequest(HttpMethod.Get, MoenConstants.ApiShowers);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var devices = JsonSerializer.Deserialize<List<ShowerDevice>>(body, JsonOptions)
            ?? [];

        _logger.LogDebug("Found {Count} device(s)", devices.Count);
        return devices;
    }

    public async Task<ShowerDevice> GetDeviceDetailsAsync(string serialNumber, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var path = string.Format(MoenConstants.ApiShowerDetail, serialNumber);
        using var request = BuildRequest(HttpMethod.Get, path);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var device = JsonSerializer.Deserialize<ShowerDevice>(body, JsonOptions)
            ?? throw new MoenApiException($"Failed to deserialize device {serialNumber}");

        _logger.LogDebug("Got device details for {SerialNumber}", serialNumber);
        return device;
    }

    public async Task<string> GetPusherAuthAsync(string socketId, string channelName, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        if (string.IsNullOrEmpty(socketId))
        {
            _logger.LogError("No socket_id available for Pusher auth");
            return string.Empty;
        }

        using var request = BuildRequest(HttpMethod.Post, MoenConstants.ApiPusherAuth);
        request.Content = new StringContent(
            $"socket_id={Uri.EscapeDataString(socketId)}&channel_name={Uri.EscapeDataString(channelName)}",
            Encoding.UTF8,
            "application/x-www-form-urlencoded");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var data = JsonSerializer.Deserialize<JsonElement>(body, JsonOptions);
        return data.TryGetProperty("auth", out var authProp) ? authProp.GetString() ?? string.Empty : string.Empty;
    }

    public async Task SetShowerModeAsync(string serialNumber, string mode, CancellationToken cancellationToken = default)
    {
        if (await GetDeviceWithChannelAsync(serialNumber, cancellationToken) is not { } r)
            return;
        var (_, channelId) = r;

        if (mode == "on")
            await SendControlEventAsync(channelId, MoenConstants.ActionShowerOn,
                new { preset = "0" }, cancellationToken);
        else
            await SendControlEventAsync(channelId, MoenConstants.ActionShowerOff,
                new { }, cancellationToken);
    }

    public async Task ResumeShowerAsync(string serialNumber, int? preset = null, CancellationToken cancellationToken = default)
    {
        if (await GetDeviceWithChannelAsync(serialNumber, cancellationToken) is not { } r)
            return;
        var (device, channelId) = r;

        var activePreset = preset ?? device.ActivePreset;
        if (activePreset is null)
        {
            _logger.LogError("Cannot resume shower {SerialNumber}: no active preset reported", serialNumber);
            return;
        }

        _logger.LogInformation("Resuming shower {SerialNumber} from paused-by-preset state using preset {Preset}",
            serialNumber, activePreset);
        await SendControlEventAsync(channelId, MoenConstants.ActionShowerOn,
            new { preset = activePreset.ToString() }, cancellationToken);
    }

    public async Task ActivatePresetAsync(string serialNumber, int presetPosition, CancellationToken cancellationToken = default)
    {
        if (await GetDeviceWithChannelAsync(serialNumber, cancellationToken) is not { } r)
            return;
        var (device, channelId) = r;

        var preset = device.Presets.FirstOrDefault(p => p.Position == presetPosition);
        if (preset is null)
        {
            _logger.LogError("Preset {Position} not found on device {SerialNumber}", presetPosition, serialNumber);
            return;
        }

        // shower_set alone activates the preset with all settings (ready_pauses_water works correctly)
        var parameters = new
        {
            active_preset = presetPosition,
            title = preset.Title,
            greeting = preset.Greeting,
            target_temperature = preset.TargetTemperature,
            outlets = preset.Outlets.Select(o => new { position = o.Position, active = o.Active }),
            timer_enabled = preset.TimerEnabled,
            timer_length = preset.TimerLength,
            timer_ends_shower = preset.TimerEndsShower,
            timer_sounds_alert = preset.TimerSoundsAlert,
            ready_pauses_water = preset.ReadyPausesWater,
            ready_pushes_notification = preset.ReadyPushesNotification,
            ready_sounds_alert = preset.ReadySoundsAlert,
        };

        await SendControlEventAsync(channelId, MoenConstants.ActionShowerSet, parameters, cancellationToken);
    }

    public async Task SetTargetTemperatureAsync(string serialNumber, double temperature, CancellationToken cancellationToken = default)
    {
        if (await GetDeviceWithChannelAsync(serialNumber, cancellationToken) is not { } r)
            return;
        var (_, channelId) = r;

        await SendControlEventAsync(channelId, MoenConstants.ActionTemperatureSet,
            new { target_temperature = (int)temperature }, cancellationToken);
    }

    public async Task SetOutletStateAsync(string serialNumber, int outletPosition, bool active, CancellationToken cancellationToken = default)
    {
        if (await GetDeviceWithChannelAsync(serialNumber, cancellationToken) is not { } r)
            return;
        var (device, channelId) = r;

        // Build the full outlet states list, changing only the target outlet
        var outlets = device.Outlets
            .Select(o => new
            {
                position = o.Position,
                active = o.Position == outletPosition ? active : o.Active
            })
            .ToList();

        await SendControlEventAsync(channelId, MoenConstants.ActionOutletsSet,
            new { outlets }, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    private async Task<(ShowerDevice Device, string ChannelId)?> GetDeviceWithChannelAsync(
        string serialNumber, CancellationToken cancellationToken)
    {
        var device = await GetDeviceDetailsAsync(serialNumber, cancellationToken);
        if (string.IsNullOrEmpty(device.Channel))
        {
            _logger.LogError("No channel ID found for device {SerialNumber}", serialNumber);
            return null;
        }
        return (device, device.Channel);
    }

    private async Task SendControlEventAsync(string channelId, string action, object parameters,
        CancellationToken cancellationToken)
    {
        var channelName = $"{MoenConstants.PusherChannelPrefix}{channelId}";

        if (!_pusherClient.IsConnected)
        {
         
            _logger.LogError("Pusher not connected, cannot send control event");
            return;
        }

        _logger.LogInformation("Sending control event '{Action}' to channel '{Channel}': {@Params}",
            action, channelName, parameters);

        await _pusherClient.SendControlEventAsync(channelName, action, parameters, cancellationToken);
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_token))
            await AuthenticateAsync(cancellationToken);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, $"{MoenConstants.ApiBaseUrl}{path}");
        request.Headers.Add("User-Token", _token);
        return request;
    }
}
