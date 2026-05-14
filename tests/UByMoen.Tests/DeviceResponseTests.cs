using UByMoen.Api.Models.Responses;
using UByMoen.Core.Models;

namespace UByMoen.Tests;

public class DeviceResponseTests
{
    private static ShowerDevice MakeDevice() => new()
    {
        SerialNumber = "SN001",
        Name = "Guest Bath",
        Mode = "ready",
        CurrentTemperature = 98.6,
        TargetTemperature = 100.0,
        MaxTemperature = 115.0,
        ActivePreset = 2,
        FirmwareVersion = "1.2.3",
        BatteryLevel = 80.0,
        TimerEnabled = true,
        TimerRemaining = 300,
        Outlets =
        [
            new Outlet { Position = 1, Active = true, IconIndex = 3, Name = "Overhead" },
            new Outlet { Position = 2, Active = false, IconIndex = 0, Name = null },
        ],
        Presets =
        [
            new Preset { Position = 1, Title = "Morning", TargetTemperature = 102.0 },
            new Preset { Position = 2, Title = "Evening", TargetTemperature = 98.0 },
        ],
    };

    [Fact]
    public void FromDevice_MapsScalarFields()
    {
        var device = MakeDevice();
        var response = DeviceResponse.FromDevice(device);

        Assert.Equal("SN001", response.SerialNumber);
        Assert.Equal("Guest Bath", response.DisplayName);
        Assert.Equal("ready", response.Mode);
        Assert.Equal(98.6, response.CurrentTemperature);
        Assert.Equal(100.0, response.TargetTemperature);
        Assert.Equal(115.0, response.MaxTemperature);
        Assert.Equal(2, response.ActivePreset);
        Assert.Equal("1.2.3", response.FirmwareVersion);
        Assert.Equal(80.0, response.BatteryLevel);
        Assert.True(response.TimerEnabled);
        Assert.Equal(300, response.TimerRemaining);
    }

    [Fact]
    public void FromDevice_MapsOutlets()
    {
        var response = DeviceResponse.FromDevice(MakeDevice());

        Assert.Equal(2, response.Outlets.Count);
        Assert.Equal(new OutletResponse(1, true, 3, "Overhead"), response.Outlets[0]);
        Assert.Equal(new OutletResponse(2, false, 0, null), response.Outlets[1]);
    }

    [Fact]
    public void FromDevice_MapsPresets()
    {
        var response = DeviceResponse.FromDevice(MakeDevice());

        Assert.Equal(2, response.Presets.Count);
        Assert.Equal(new PresetResponse(1, "Morning", 102.0), response.Presets[0]);
        Assert.Equal(new PresetResponse(2, "Evening", 98.0), response.Presets[1]);
    }

    [Fact]
    public void FromDevice_UsesSerialFallback_WhenNameIsNull()
    {
        var device = MakeDevice();
        device.Name = null;

        var response = DeviceResponse.FromDevice(device);

        Assert.Equal("Moen Shower SN001", response.DisplayName);
    }
}
