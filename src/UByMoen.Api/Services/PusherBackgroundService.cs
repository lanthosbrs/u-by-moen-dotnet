using System.Text.Json.Nodes;
using UByMoen.Core;
using UByMoen.Core.Client;
using UByMoen.Core.Models;

namespace UByMoen.Api.Services;

/// <summary>
/// Background service that:
///  1. Connects to the Pusher WebSocket on startup
///  2. Subscribes to each device channel for real-time state updates
///  3. Falls back to polling every <see cref="MoenConstants.UpdateIntervalSeconds"/> seconds
/// </summary>
public class PusherBackgroundService : BackgroundService
{
    private readonly IMoenApiClient _api;
    private readonly IMoenPusherClient _pusher;
    private readonly DeviceStateService _state;
    private readonly ILogger<PusherBackgroundService> _logger;

    // Map of Pusher channel-id -> serial_number
    private readonly Dictionary<string, string> _channelToSerial = [];

    public PusherBackgroundService(
        IMoenApiClient api,
        IMoenPusherClient pusher,
        DeviceStateService state,
        ILogger<PusherBackgroundService> logger)
    {
        _api = api;
        _pusher = pusher;
        _state = state;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wire up the event handler before connecting
        _pusher.OnEvent += HandlePusherEventAsync;
        _pusher.OnDisconnected = async _ =>
        {
            _logger.LogWarning("Pusher disconnected — reinitialising connection");
            _channelToSerial.Clear();
            await InitialiseAsync(stoppingToken);
        };

        await InitialiseAsync(stoppingToken);

        // Polling loop as a fallback
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(MoenConstants.UpdateIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshDevicesAsync(stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _pusher.OnEvent -= HandlePusherEventAsync;
        await _pusher.DisconnectAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    // -------------------------------------------------------------------------

    private async Task InitialiseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _api.AuthenticateAsync(cancellationToken);
            var creds = await _api.GetPusherCredentialsAsync(cancellationToken);

            // Load initial device list and detail
            await RefreshDevicesAsync(cancellationToken);

            // Connect Pusher WebSocket
            var connected = await _pusher.ConnectAsync(creds.AppKey, creds.Cluster, cancellationToken);
            if (!connected)
            {
                _logger.LogWarning("Could not connect to Pusher — real-time updates disabled, polling only");
                return;
            }

            // Subscribe to every device channel
            foreach (var (serial, device) in _state.Devices)
            {
                if (string.IsNullOrEmpty(device.Channel))
                    continue;

                var channelName = $"{MoenConstants.PusherChannelPrefix}{device.Channel}";
                var auth = await _api.GetPusherAuthAsync(_pusher.SocketId!, channelName, cancellationToken);

                if (string.IsNullOrEmpty(auth))
                {
                    _logger.LogWarning("Failed to get Pusher auth for channel {Channel}", channelName);
                    continue;
                }

                await _pusher.SubscribeAsync(channelName, auth, cancellationToken);
                _channelToSerial[channelName] = serial;
                _logger.LogInformation("Subscribed to Pusher channel {Channel} for device {Serial}", channelName, serial);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialise Pusher background service");
        }
    }

    private async Task RefreshDevicesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var devices = await _api.GetDevicesAsync(cancellationToken);
            var detailed = new List<ShowerDevice>();

            foreach (var d in devices)
            {
                try
                {
                    var detail = await _api.GetDeviceDetailsAsync(d.SerialNumber, cancellationToken);
                    detailed.Add(detail);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get details for device {SerialNumber}", d.SerialNumber);
                    // Keep stale data if available
                    var stale = _state.GetDevice(d.SerialNumber);
                    if (stale is not null) detailed.Add(stale);
                }
            }

            _state.SetDevices(detailed);
            _logger.LogDebug("Refreshed {Count} device(s) from API", detailed.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing devices from API");
        }
    }

    private async Task HandlePusherEventAsync(string channelName, string eventName, object? eventData)
    {
        if (eventName != "client-state-reported")
            return;

        if (!_channelToSerial.TryGetValue(channelName, out var serialNumber))
        {
            _logger.LogDebug("Received event for unknown channel {Channel}", channelName);
            return;
        }

        if (eventData is not JsonNode node) return;

        var eventType = node["type"]?.GetValue<string>();
        if (eventType is not ("state_change" or "shower_report"))
            return;

        var data = node["data"];
        if (data is null) return;

        var update = new PusherDeviceUpdate
        {
            Mode = data["current_mode"]?.GetValue<string>(),
            CurrentTemperature = TryGetDouble(data["current_temperature"]),
            TargetTemperature = TryGetDouble(data["target_temperature"]),
            ActivePreset = TryGetInt(data["active_preset"]),
            
            TimerRemaining = TryGetInt(data["time_remaining"]),
        };

        if (data["timer_enabled"]?.GetType() == typeof(int))
        {
            //parse
            update.TimerEnabled =  data["timer_enabled"]?.GetValue<int>() == 0 ? false : true;
        }
        else
        {
            //otherwise bool
            update.TimerEnabled = data["timer_enabled"]?.GetValue<bool>();
        }


        // Deserialise outlets if present
        if (data["outlets"] is JsonArray outletsArray)
        {
            update.Outlets = outletsArray
                .Where(o => o is not null)
                .Select(o => new Outlet
                {
                    Position = o!["position"]?.GetValue<int>() ?? 0,
                    Active = o["active"]?.GetValue<bool>() ?? false,
                    IconIndex = o["icon_index"]?.GetValue<int>() ?? 0,
                })
                .ToList();
        }

        _state.ApplyPusherUpdate(serialNumber, update);
        _logger.LogInformation("Applied Pusher update for device {Serial}: mode={Mode}", serialNumber, update.Mode ?? "(unchanged)");
    }

    private static double? TryGetDouble(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out double d) ? d : null;

    private static int? TryGetInt(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out int i) ? i : null;
}
