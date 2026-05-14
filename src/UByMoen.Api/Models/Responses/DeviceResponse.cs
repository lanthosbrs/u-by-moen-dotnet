using UByMoen.Core.Models;

namespace UByMoen.Api.Models.Responses;

public record OutletResponse(int Position, bool Active, int IconIndex, string? Name);
public record PresetResponse(int Position, string Title, double TargetTemperature);

public record DeviceResponse(
    string SerialNumber,
    string DisplayName,
    string Mode,
    double? CurrentTemperature,
    double? TargetTemperature,
    double? MaxTemperature,
    int? ActivePreset,
    string? FirmwareVersion,
    double? BatteryLevel,
    bool? TimerEnabled,
    int? TimerRemaining,
    IReadOnlyList<OutletResponse> Outlets,
    IReadOnlyList<PresetResponse> Presets)
{
    public static DeviceResponse FromDevice(ShowerDevice d) => new(
        d.SerialNumber,
        d.DisplayName,
        d.Mode,
        d.CurrentTemperature,
        d.TargetTemperature,
        d.MaxTemperature,
        d.ActivePreset,
        d.FirmwareVersion,
        d.BatteryLevel,
        d.TimerEnabled,
        d.TimerRemaining,
        d.Outlets.Select(o => new OutletResponse(o.Position, o.Active, o.IconIndex, o.Name)).ToList(),
        d.Presets.Select(p => new PresetResponse(p.Position, p.Title, p.TargetTemperature)).ToList()
    );
}
