using UByMoen.Core.Models;

namespace UByMoen.Core.Client;

public delegate Task PusherEventHandler(string channelName, string eventName, object? eventData);

public interface IMoenPusherClient : IAsyncDisposable
{
    bool IsConnected { get; }
    string? SocketId { get; }

    Task<bool> ConnectAsync(string appKey, string cluster, CancellationToken cancellationToken = default);
    Task<bool> SubscribeAsync(string channelName, string auth, CancellationToken cancellationToken = default);
    Task<bool> SendControlEventAsync(string channelName, string action, object parameters, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    event PusherEventHandler? OnEvent;
}
