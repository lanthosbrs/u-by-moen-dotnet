using Microsoft.AspNetCore.Mvc;
using UByMoen.Api.Models.Requests;
using UByMoen.Api.Models.Responses;
using UByMoen.Api.Services;
using UByMoen.Core.Client;
using UByMoen.Core;

namespace UByMoen.Api.Controllers;

[ApiController]
[Route("api/devices")]
public class DevicesController : ControllerBase
{
    private readonly IMoenApiClient _api;
    private readonly DeviceStateService _state;

    public DevicesController(IMoenApiClient api, DeviceStateService state)
    {
        _api = api;
        _state = state;
    }

    // GET /api/devices
    [HttpGet]
    [ProducesResponseType<IEnumerable<DeviceResponse>>(StatusCodes.Status200OK)]
    public IActionResult GetDevices()
    {
        var devices = _state.Devices.Values.Select(DeviceResponse.FromDevice);
        return Ok(devices);
    }

    // GET /api/devices/{serial}
    [HttpGet("{serial}")]
    [ProducesResponseType<DeviceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetDevice(string serial)
    {
        var device = _state.GetDevice(serial);
        return device is null ? NotFound() : Ok(DeviceResponse.FromDevice(device));
    }

    // POST /api/devices/{serial}/on
    [HttpPost("{serial}/on")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TurnOn(string serial, CancellationToken ct)
    {
        var device = _state.GetDevice(serial);
        if (device is null) return NotFound();

        if (device.Mode == MoenConstants.ModePausedByPreset)
            await _api.ResumeShowerAsync(serial, device.ActivePreset, ct);
        else
            await _api.SetShowerModeAsync(serial, "on", cancellationToken: ct);

        return NoContent();
    }

    // POST /api/devices/{serial}/off
    [HttpPost("{serial}/off")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TurnOff(string serial, CancellationToken ct)
    {
        if (_state.GetDevice(serial) is null) return NotFound();
        await _api.SetShowerModeAsync(serial, MoenConstants.ModeOff, cancellationToken: ct);
        return NoContent();
    }

    // POST /api/devices/{serial}/resume
    [HttpPost("{serial}/resume")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Resume(string serial, CancellationToken ct)
    {
        var device = _state.GetDevice(serial);
        if (device is null) return NotFound();
        await _api.ResumeShowerAsync(serial, device.ActivePreset, ct);
        return NoContent();
    }

    // POST /api/devices/{serial}/temperature
    [HttpPost("{serial}/temperature")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetTemperature(string serial, [FromBody] SetTemperatureRequest body, CancellationToken ct)
    {
        var device = _state.GetDevice(serial);
        if (device is null) return NotFound();

        var max = device.MaxTemperature ?? MoenConstants.DefaultMaxTemperature;

        if (body.Temperature < MoenConstants.MinTemperature || body.Temperature > max)
            return BadRequest($"Temperature must be between {MoenConstants.MinTemperature} and {max}°F");

        await _api.SetTargetTemperatureAsync(serial, body.Temperature, ct);
        return NoContent();
    }

    // POST /api/devices/{serial}/presets/{position}
    [HttpPost("{serial}/presets/{position:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ActivatePreset(string serial, int position, CancellationToken ct)
    {
        if (_state.GetDevice(serial) is null) return NotFound();
        await _api.ActivatePresetAsync(serial, position, ct);
        return NoContent();
    }

    // POST /api/devices/{serial}/outlets/{position}/on
    [HttpPost("{serial}/outlets/{position:int}/on")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> OutletOn(string serial, int position, CancellationToken ct)
    {
        if (_state.GetDevice(serial) is null) return NotFound();
        await _api.SetOutletStateAsync(serial, position, active: true, ct);
        return NoContent();
    }

    // POST /api/devices/{serial}/outlets/{position}/off
    [HttpPost("{serial}/outlets/{position:int}/off")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> OutletOff(string serial, int position, CancellationToken ct)
    {
        if (_state.GetDevice(serial) is null) return NotFound();
        await _api.SetOutletStateAsync(serial, position, active: false, ct);
        return NoContent();
    }
}
