using UByMoen.Api.Services;
using UByMoen.Core.Models;

namespace UByMoen.Tests;

public class DeviceStateServiceTests
{
    private readonly DeviceStateService _svc = new();

    private static ShowerDevice MakeDevice(string serial, string mode = "off") => new()
    {
        SerialNumber = serial,
        Mode = mode,
        Outlets = [new Outlet { Position = 1, Active = false }],
    };

    [Fact]
    public void GetDevice_ReturnsNull_WhenSerialUnknown()
    {
        Assert.Null(_svc.GetDevice("UNKNOWN"));
    }

    [Fact]
    public void SetDevices_StoresBySerialNumber()
    {
        _svc.SetDevices([MakeDevice("A"), MakeDevice("B")]);

        Assert.NotNull(_svc.GetDevice("A"));
        Assert.NotNull(_svc.GetDevice("B"));
        Assert.Null(_svc.GetDevice("C"));
    }

    [Fact]
    public void SetDevices_OverwritesExistingEntry()
    {
        _svc.SetDevices([MakeDevice("A", "off")]);
        _svc.SetDevices([MakeDevice("A", "ready")]);

        Assert.Equal("ready", _svc.GetDevice("A")!.Mode);
    }

    [Fact]
    public void Devices_ReflectsStoredEntries()
    {
        _svc.SetDevices([MakeDevice("X"), MakeDevice("Y")]);

        Assert.Equal(2, _svc.Devices.Count);
        Assert.True(_svc.Devices.ContainsKey("X"));
        Assert.True(_svc.Devices.ContainsKey("Y"));
    }

    [Fact]
    public void ApplyPusherUpdate_IgnoresUnknownSerial()
    {
        // Should not throw
        _svc.ApplyPusherUpdate("GHOST", new PusherDeviceUpdate { Mode = "ready" });
    }

    [Fact]
    public void ApplyPusherUpdate_UpdatesMode_WhenProvided()
    {
        _svc.SetDevices([MakeDevice("A", "off")]);

        _svc.ApplyPusherUpdate("A", new PusherDeviceUpdate { Mode = "ready" });

        Assert.Equal("ready", _svc.GetDevice("A")!.Mode);
    }

    [Fact]
    public void ApplyPusherUpdate_PreservesMode_WhenNull()
    {
        _svc.SetDevices([MakeDevice("A", "adjusting")]);

        _svc.ApplyPusherUpdate("A", new PusherDeviceUpdate { Mode = null });

        Assert.Equal("adjusting", _svc.GetDevice("A")!.Mode);
    }

    [Fact]
    public void ApplyPusherUpdate_UpdatesTemperatures()
    {
        _svc.SetDevices([MakeDevice("A")]);

        _svc.ApplyPusherUpdate("A", new PusherDeviceUpdate
        {
            CurrentTemperature = 98.5,
            TargetTemperature = 102.0,
        });

        var device = _svc.GetDevice("A")!;
        Assert.Equal(98.5, device.CurrentTemperature);
        Assert.Equal(102.0, device.TargetTemperature);
    }

    [Fact]
    public void ApplyPusherUpdate_UpdatesOutlets_WhenProvided()
    {
        _svc.SetDevices([MakeDevice("A")]);

        var newOutlets = new List<Outlet> { new() { Position = 1, Active = true } };
        _svc.ApplyPusherUpdate("A", new PusherDeviceUpdate { Outlets = newOutlets });

        Assert.True(_svc.GetDevice("A")!.Outlets[0].Active);
    }

    [Fact]
    public void ApplyPusherUpdate_PreservesOutlets_WhenNull()
    {
        _svc.SetDevices([MakeDevice("A")]);

        _svc.ApplyPusherUpdate("A", new PusherDeviceUpdate { Outlets = null });

        // Original outlet (Active = false) should be unchanged
        Assert.False(_svc.GetDevice("A")!.Outlets[0].Active);
    }

    [Fact]
    public void ApplyPusherUpdate_UpdatesTimerFields()
    {
        _svc.SetDevices([MakeDevice("A")]);

        _svc.ApplyPusherUpdate("A", new PusherDeviceUpdate
        {
            TimerEnabled = true,
            TimerRemaining = 120,
            ActivePreset = 3,
        });

        var device = _svc.GetDevice("A")!;
        Assert.True(device.TimerEnabled);
        Assert.Equal(120, device.TimerRemaining);
        Assert.Equal(3, device.ActivePreset);
    }
}
