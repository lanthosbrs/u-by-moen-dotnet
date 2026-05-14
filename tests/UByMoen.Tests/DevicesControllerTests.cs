using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using UByMoen.Api.Controllers;
using UByMoen.Api.Models.Requests;
using UByMoen.Api.Services;
using UByMoen.Core;
using UByMoen.Core.Client;
using UByMoen.Core.Models;

namespace UByMoen.Tests;

public class DevicesControllerTests
{
    private readonly IMoenApiClient _api = Substitute.For<IMoenApiClient>();
    private readonly DeviceStateService _state = new();

    private DevicesController CreateController() => new(_api, _state);

    private void SeedDevice(ShowerDevice device) => _state.SetDevices([device]);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ShowerDevice MakeDevice(string serial = "SN001", string mode = "off", int? activePreset = 1) => new()
    {
        SerialNumber = serial,
        Name = "Test Shower",
        Mode = mode,
        MaxTemperature = 115.0,
        ActivePreset = activePreset,
        Outlets = [new Outlet { Position = 1, Active = false }],
        Presets = [new Preset { Position = 1, Title = "Morning", TargetTemperature = 100.0 }],
    };

    // ── GetDevices ────────────────────────────────────────────────────────────

    [Fact]
    public void GetDevices_ReturnsEmptyList_WhenNoDevicesStored()
    {
        var result = CreateController().GetDevices() as OkObjectResult;

        Assert.NotNull(result);
        Assert.Empty((IEnumerable<object>)result.Value!);
    }

    [Fact]
    public void GetDevices_ReturnsAllStoredDevices()
    {
        SeedDevice(MakeDevice("A"));
        SeedDevice(MakeDevice("B"));

        var result = CreateController().GetDevices() as OkObjectResult;
        var items = (IEnumerable<object>)result!.Value!;

        Assert.Equal(2, items.Count());
    }

    // ── GetDevice ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetDevice_Returns404_WhenNotFound()
    {
        var result = CreateController().GetDevice("MISSING");
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void GetDevice_Returns200WithDevice_WhenFound()
    {
        SeedDevice(MakeDevice("SN001"));

        var result = CreateController().GetDevice("SN001");

        Assert.IsType<OkObjectResult>(result);
    }

    // ── TurnOn ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TurnOn_Returns404_WhenNotFound()
    {
        var result = await CreateController().TurnOn("MISSING", Ct);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task TurnOn_CallsResumeShower_WhenPausedByPreset()
    {
        SeedDevice(MakeDevice("SN001", MoenConstants.ModePausedByPreset));

        var result = await CreateController().TurnOn("SN001", Ct);

        Assert.IsType<NoContentResult>(result);
        await _api.Received(1).ResumeShowerAsync("SN001", 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TurnOn_CallsSetShowerMode_WhenNotPaused()
    {
        SeedDevice(MakeDevice("SN001", MoenConstants.ModeOff));

        var result = await CreateController().TurnOn("SN001", Ct);

        Assert.IsType<NoContentResult>(result);
        await _api.Received(1).SetShowerModeAsync("SN001", "on", Arg.Any<CancellationToken>());
    }

    // ── TurnOff ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task TurnOff_Returns404_WhenNotFound()
    {
        var result = await CreateController().TurnOff("MISSING", Ct);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task TurnOff_CallsSetShowerModeOff()
    {
        SeedDevice(MakeDevice("SN001", "ready"));

        var result = await CreateController().TurnOff("SN001", Ct);

        Assert.IsType<NoContentResult>(result);
        await _api.Received(1).SetShowerModeAsync("SN001", MoenConstants.ModeOff, Arg.Any<CancellationToken>());
    }

    // ── Resume ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resume_Returns404_WhenNotFound()
    {
        var result = await CreateController().Resume("MISSING", Ct);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Resume_CallsResumeShowerWithCachedPreset()
    {
        SeedDevice(MakeDevice("SN001", activePreset: 2));

        var result = await CreateController().Resume("SN001", Ct);

        Assert.IsType<NoContentResult>(result);
        await _api.Received(1).ResumeShowerAsync("SN001", 2, Arg.Any<CancellationToken>());
    }

    // ── SetTemperature ────────────────────────────────────────────────────────

    [Fact]
    public async Task SetTemperature_Returns404_WhenNotFound()
    {
        var result = await CreateController().SetTemperature("MISSING", new SetTemperatureRequest(100), Ct);
        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData(50.0)]  // below MinTemperature (60)
    [InlineData(120.0)] // above MaxTemperature (115)
    public async Task SetTemperature_Returns400_WhenOutOfRange(double temp)
    {
        SeedDevice(MakeDevice("SN001"));

        var result = await CreateController().SetTemperature("SN001", new SetTemperatureRequest(temp), Ct);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SetTemperature_CallsSetTargetTemperature_WhenValid()
    {
        SeedDevice(MakeDevice("SN001"));

        var result = await CreateController().SetTemperature("SN001", new SetTemperatureRequest(100.0), Ct);

        Assert.IsType<NoContentResult>(result);
        await _api.Received(1).SetTargetTemperatureAsync("SN001", 100.0, Arg.Any<CancellationToken>());
    }

    // ── ActivatePreset ────────────────────────────────────────────────────────

    [Fact]
    public async Task ActivatePreset_Returns404_WhenNotFound()
    {
        var result = await CreateController().ActivatePreset("MISSING", 1, Ct);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ActivatePreset_CallsActivatePresetAsync()
    {
        SeedDevice(MakeDevice("SN001"));

        var result = await CreateController().ActivatePreset("SN001", 1, Ct);

        Assert.IsType<NoContentResult>(result);
        await _api.Received(1).ActivatePresetAsync("SN001", 1, Arg.Any<CancellationToken>());
    }

    // ── OutletOn / OutletOff ──────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Outlet_Returns404_WhenNotFound(bool active)
    {
        var result = active
            ? await CreateController().OutletOn("MISSING", 1, Ct)
            : await CreateController().OutletOff("MISSING", 1, Ct);
        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Outlet_CallsSetOutletStateWithCorrectFlag(bool active)
    {
        SeedDevice(MakeDevice("SN001"));

        var result = active
            ? await CreateController().OutletOn("SN001", 1, Ct)
            : await CreateController().OutletOff("SN001", 1, Ct);

        Assert.IsType<NoContentResult>(result);
        await _api.Received(1).SetOutletStateAsync("SN001", 1, active, Arg.Any<CancellationToken>());
    }
}
