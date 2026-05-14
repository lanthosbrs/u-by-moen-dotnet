using System.Text.Json.Serialization;

namespace UByMoen.Core.Models;

public class ShowerDevice
{
    [JsonPropertyName("serial_number")]
    public string SerialNumber { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = MoenConstants.ModeOff;

    [JsonPropertyName("current_temperature")]
    public double? CurrentTemperature { get; set; }

    [JsonPropertyName("target_temperature")]
    public double? TargetTemperature { get; set; }

    [JsonPropertyName("max_temp")]
    public double? MaxTemperature { get; set; }

    [JsonPropertyName("active_preset")]
    public int? ActivePreset { get; set; }

    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("current_firmware_version")]
    public string? FirmwareVersion { get; set; }

    [JsonPropertyName("battery_level")]
    public double? BatteryLevel { get; set; }

    [JsonPropertyName("outlets")]
    public List<Outlet> Outlets { get; set; } = [];

    [JsonPropertyName("presets")]
    public List<Preset> Presets { get; set; } = [];

    [JsonPropertyName("timer_enabled")]
    public bool? TimerEnabled { get; set; }

    [JsonPropertyName("timer_remaining")]
    public int? TimerRemaining { get; set; }

    public string DisplayName => Name ?? $"Moen Shower {SerialNumber}";

    public bool IsOn => Mode is not (MoenConstants.ModeOff or MoenConstants.ModePausedByPreset);
}
