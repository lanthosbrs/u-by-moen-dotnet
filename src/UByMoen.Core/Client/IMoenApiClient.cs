using UByMoen.Core.Models;

namespace UByMoen.Core.Client;

public interface IMoenApiClient
{
    Task<string> AuthenticateAsync(CancellationToken cancellationToken = default);
    Task<PusherCredentials> GetPusherCredentialsAsync(CancellationToken cancellationToken = default);
    Task<List<ShowerDevice>> GetDevicesAsync(CancellationToken cancellationToken = default);
    Task<ShowerDevice> GetDeviceDetailsAsync(string serialNumber, CancellationToken cancellationToken = default);
    Task<string> GetPusherAuthAsync(string socketId, string channelName, CancellationToken cancellationToken = default);
    Task SetShowerModeAsync(string serialNumber, string mode, string? preset = null, CancellationToken cancellationToken = default);
    Task ResumeShowerAsync(string serialNumber, int? preset = null, CancellationToken cancellationToken = default);
    Task ActivatePresetAsync(string serialNumber, int presetPosition, CancellationToken cancellationToken = default);
    Task SetTargetTemperatureAsync(string serialNumber, double temperature, CancellationToken cancellationToken = default);
    Task SetOutletStateAsync(string serialNumber, int outletPosition, bool active, CancellationToken cancellationToken = default);
}
