using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace UByMoen.Core.Client;

public class MoenPusherClient : IMoenPusherClient
{
    private readonly ILogger<MoenPusherClient> _logger;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public bool IsConnected => _ws?.State == WebSocketState.Open;
    public string? SocketId { get; private set; }

    public Func<object, Task>? OnDisconnected { get; set; }

    public event PusherEventHandler? OnEvent;

    public MoenPusherClient(ILogger<MoenPusherClient> logger)
    {
        _logger = logger;
    }

    public async Task<bool> ConnectAsync(string appKey, string cluster, CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            _logger.LogDebug("Pusher already connected");
            return true;
        }

        var wsUrl = string.Format(MoenConstants.PusherWsUrlTemplate, cluster, appKey);

        try
        {
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri(wsUrl), cancellationToken);

            _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), _receiveCts.Token);

            // Wait for the connection_established event to populate SocketId
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (SocketId is null && DateTime.UtcNow < deadline)
                await Task.Delay(100, cancellationToken);

            if (SocketId is null)
            {
                _logger.LogWarning("Connected to Pusher but did not receive socket_id within timeout");
                return false;
            }

            _logger.LogInformation("Connected to Pusher WebSocket with socket_id={SocketId}", SocketId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Pusher");
            return false;
        }
    }

    public async Task<bool> SubscribeAsync(string channelName, string auth, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            _logger.LogError("Pusher not connected, cannot subscribe to {Channel}", channelName);
            return false;
        }

        var message = new
        {
            @event = "pusher:subscribe",
            data = new { channel = channelName, auth }
        };

        return await SendJsonAsync(message, cancellationToken);
    }

    public async Task<bool> SendControlEventAsync(string channelName, string action, object parameters,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            _logger.LogError("Pusher not connected, cannot send event to {Channel}", channelName);
            return false;
        }

        var message = new
        {
            @event = "client-state-desired",
            channel = channelName,
            data = new
            {
                type = "control",
                data = new { action, @params = parameters }
            }
        };

        _logger.LogDebug("Sending control event '{Action}' to '{Channel}'", action, channelName);
        return await SendJsonAsync(message, cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_receiveCts is not null)
        {
            await _receiveCts.CancelAsync();
            if (_receiveTask is not null)
            {
                try { await _receiveTask; }
                catch (OperationCanceledException) { }
            }
        }

        if (_ws?.State == WebSocketState.Open)
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing WebSocket");
            }
        }

        _ws?.Dispose();
        _ws = null;
        SocketId = null;
        _logger.LogInformation("Disconnected from Pusher WebSocket");
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _sendLock.Dispose();
        _receiveCts?.Dispose();
    }

    // -------------------------------------------------------------------------
    // Private receive loop
    // -------------------------------------------------------------------------

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];

        bool unexpectedDisconnect = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsConnected)
            {
                var messageBuilder = new StringBuilder();
                WebSocketReceiveResult result;

                do
                {
                    result = await _ws!.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation("Pusher WebSocket closed by server");
                        unexpectedDisconnect = true;
                        return;
                    }

                    messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                await ProcessMessageAsync(messageBuilder.ToString());
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Pusher receive loop cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Pusher receive loop");
            unexpectedDisconnect = true;
        }
        finally
        {
            if (unexpectedDisconnect && OnDisconnected is not null)
            {
                _logger.LogInformation("Pusher disconnected unexpectedly — invoking reconnect handler");
                _ = Task.Run(() => OnDisconnected(this));
            }
        }
    }

    private async Task ProcessMessageAsync(string raw)
    {
        try
        {
            var node = JsonNode.Parse(raw);
            if (node is null) return;

            var eventName = node["event"]?.GetValue<string>();
            var channel = node["channel"]?.GetValue<string>() ?? string.Empty;
            var dataRaw = node["data"];

            // The Pusher protocol double-encodes the data field as a JSON string
            object? eventData = null;
            if (dataRaw is JsonValue dataValue && dataValue.TryGetValue<string>(out var dataStr))
            {
                try { eventData = JsonNode.Parse(dataStr); }
                catch { eventData = dataStr; }
            }
            else
            {
                eventData = dataRaw;
            }

            switch (eventName)
            {
                case "pusher:connection_established":
                    if (eventData is JsonNode connNode)
                        SocketId = connNode["socket_id"]?.GetValue<string>();
                    _logger.LogInformation("Pusher connection established, socket_id={SocketId}", SocketId);
                    break;

                case "pusher:error":
                    _logger.LogError("Pusher error: {Data}", eventData);
                    break;

                case "pusher_internal:subscription_succeeded":
                    _logger.LogInformation("Subscribed to Pusher channel: {Channel}", channel);
                    break;

                default:
                    _logger.LogDebug("Pusher event '{Event}' on '{Channel}': {Data}", eventName, channel, eventData);
                    if (OnEvent is not null && eventName is not null)
                        await OnEvent(channel, eventName, eventData);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Pusher message: {Raw}", raw);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Pusher message");
        }
    }

    private async Task<bool> SendJsonAsync(object payload, CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws!.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Pusher message");
            return false;
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
