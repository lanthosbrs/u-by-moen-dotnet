using System.Collections.Concurrent;
using UByMoen.Core.Models;

namespace UByMoen.Api.Services;

/// <summary>
/// In-memory cache of device states, updated by the Pusher background service and periodic polling.
/// </summary>
public class DeviceStateService
{
    private readonly ConcurrentDictionary<string, ShowerDevice> _devices = new();

    public IReadOnlyDictionary<string, ShowerDevice> Devices => _devices;

    public ShowerDevice? GetDevice(string serialNumber) =>
        _devices.TryGetValue(serialNumber, out var d) ? d : null;

    public void SetDevices(IEnumerable<ShowerDevice> devices)
    {
        foreach (var device in devices)
            _devices[device.SerialNumber] = device;
    }

    public void ApplyPusherUpdate(string serialNumber, PusherDeviceUpdate update)
    {
        if (!_devices.TryGetValue(serialNumber, out var device))
            return;

        if (update.Mode is not null) device.Mode = update.Mode;
        if (update.CurrentTemperature.HasValue) device.CurrentTemperature = update.CurrentTemperature;
        if (update.TargetTemperature.HasValue) device.TargetTemperature = update.TargetTemperature;
        if (update.ActivePreset.HasValue) device.ActivePreset = update.ActivePreset;
        if (update.TimerEnabled.HasValue) device.TimerEnabled = update.TimerEnabled;
        if (update.TimerRemaining.HasValue) device.TimerRemaining = update.TimerRemaining;
        if (update.Outlets is not null) device.Outlets = update.Outlets;
        if (update.Presets is not null) device.Presets = update.Presets;

        _devices[serialNumber] = device;
    }
}

public class PusherDeviceUpdate
{
    public string? Mode { get; set; }
    public double? CurrentTemperature { get; set; }
    public double? TargetTemperature { get; set; }
    public int? ActivePreset { get; set; }
    public bool? TimerEnabled { get; set; }
    public int? TimerRemaining { get; set; }
    public List<Outlet>? Outlets { get; set; }
    public List<Preset>? Presets { get; set; }
}
