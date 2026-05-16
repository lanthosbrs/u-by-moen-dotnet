using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using UByMoen.Api.Services;
using UByMoen.Core;
using UByMoen.Core.Client;
using UByMoen.Core.Models;

namespace UByMoen.Tests;

public class PusherReconnectTests
{
    private readonly IMoenApiClient _api = Substitute.For<IMoenApiClient>();
    private readonly IMoenPusherClient _pusher = Substitute.For<IMoenPusherClient>();
    private readonly DeviceStateService _state = new();
    private readonly ILogger<PusherBackgroundService> _logger =
        NullLogger<PusherBackgroundService>.Instance;

    private PusherBackgroundService CreateService() =>
        new(_api, _pusher, _state, _logger);

    /// <summary>
    /// Sets up default API and Pusher mocks. ConnectAsync is NOT configured here;
    /// call SetupConnectGates() to configure it with synchronization.
    /// </summary>
    private void SetupDefaultApi(ShowerDevice[]? devices = null)
    {
        _api.AuthenticateAsync(Arg.Any<CancellationToken>()).Returns("token");
        _api.GetPusherCredentialsAsync(Arg.Any<CancellationToken>())
            .Returns(new PusherCredentials { AppKey = "key", Cluster = "mt1" });

        var deviceList = devices?.ToList() ?? [];
        _api.GetDevicesAsync(Arg.Any<CancellationToken>()).Returns(deviceList);
        foreach (var d in deviceList)
            _api.GetDeviceDetailsAsync(d.SerialNumber, Arg.Any<CancellationToken>()).Returns(d);

        _pusher.SocketId.Returns("socket-123");
        _pusher.SubscribeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    /// <summary>
    /// Configures ConnectAsync to return true and complete two TaskCompletionSources:
    /// one on the initial call and one on the first reconnect.
    /// </summary>
    private (TaskCompletionSource Initial, TaskCompletionSource Reconnect) SetupConnectGates()
    {
        var initial = new TaskCompletionSource();
        var reconnect = new TaskCompletionSource();
        int count = 0;

        _pusher.ConnectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var n = Interlocked.Increment(ref count);
                if (n == 1) initial.TrySetResult();
                else reconnect.TrySetResult();
                return Task.FromResult(true);
            });

        return (initial, reconnect);
    }

    // ── MoenPusherClient property tests ───────────────────────────────────────

    [Fact]
    public async Task MoenPusherClient_OnDisconnected_DefaultsToNull()
    {
        await using var client = new MoenPusherClient(NullLogger<MoenPusherClient>.Instance);

        Assert.Null(client.OnDisconnected);
    }

    [Fact]
    public async Task MoenPusherClient_OnDisconnected_CanBeAssignedAndRead()
    {
        await using var client = new MoenPusherClient(NullLogger<MoenPusherClient>.Instance);
        Func<object, Task> handler = _ => Task.CompletedTask;

        client.OnDisconnected = handler;

        Assert.Same(handler, client.OnDisconnected);
    }

    [Fact]
    public async Task MoenPusherClient_IsNotConnected_BeforeAnyConnect()
    {
        await using var client = new MoenPusherClient(NullLogger<MoenPusherClient>.Instance);

        Assert.False(client.IsConnected);
    }

    // ── PusherBackgroundService reconnect tests ───────────────────────────────

    [Fact]
    public async Task ExecuteAsync_RegistersOnDisconnectedHandler_OnStart()
    {
        SetupDefaultApi();
        var (initial, _) = SetupConnectGates();

        using var cts = new CancellationTokenSource();
        using var svc = CreateService();

        await svc.StartAsync(cts.Token);
        await initial.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.NotNull(_pusher.OnDisconnected);

        await cts.CancelAsync();
        await svc.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OnDisconnected_ReAuthenticates_AfterUnexpectedDisconnect()
    {
        SetupDefaultApi();
        var (initial, reconnect) = SetupConnectGates();

        using var cts = new CancellationTokenSource();
        using var svc = CreateService();

        await svc.StartAsync(cts.Token);
        await initial.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Simulate an unexpected server-side disconnect
        await _pusher.OnDisconnected!(this);

        await reconnect.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await cts.CancelAsync();
        await svc.StopAsync(CancellationToken.None);

        // AuthenticateAsync should have been called once for initial start and once for reconnect
        await _api.Received(2).AuthenticateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnDisconnected_ReconnectsToPusher_AfterUnexpectedDisconnect()
    {
        SetupDefaultApi();
        var (initial, reconnect) = SetupConnectGates();

        using var cts = new CancellationTokenSource();
        using var svc = CreateService();

        await svc.StartAsync(cts.Token);
        await initial.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await _pusher.OnDisconnected!(this);

        await reconnect.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await cts.CancelAsync();
        await svc.StopAsync(CancellationToken.None);

        await _pusher.Received(2).ConnectAsync("key", "mt1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnDisconnected_ResubscribesAllDeviceChannels_AfterReconnect()
    {
        const string serial = "SN001";
        const string channelId = "chan-abc";
        const string channelName = $"{MoenConstants.PusherChannelPrefix}{channelId}";

        var device = new ShowerDevice { SerialNumber = serial, Channel = channelId, Mode = "off", Outlets = [] };

        SetupDefaultApi([device]);
        _api.GetPusherAuthAsync(Arg.Any<string>(), channelName, Arg.Any<CancellationToken>())
            .Returns("pusher-auth");

        // Gate on the second SubscribeAsync call (first = initial, second = reconnect)
        var secondSubscribeTcs = new TaskCompletionSource();
        int subscribeCount = 0;
        _pusher.SubscribeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref subscribeCount) == 2)
                    secondSubscribeTcs.TrySetResult();
                return Task.FromResult(true);
            });

        var (initial, _) = SetupConnectGates();

        using var cts = new CancellationTokenSource();
        using var svc = CreateService();

        await svc.StartAsync(cts.Token);
        await initial.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await _pusher.OnDisconnected!(this);

        await secondSubscribeTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await cts.CancelAsync();
        await svc.StopAsync(CancellationToken.None);

        await _pusher.Received(2).SubscribeAsync(channelName, "pusher-auth", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnDisconnected_DoesNotSubscribe_WhenReconnectFails()
    {
        SetupDefaultApi();

        var initial = new TaskCompletionSource();
        var secondConnect = new TaskCompletionSource();
        int connectCount = 0;

        _pusher.ConnectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var n = Interlocked.Increment(ref connectCount);
                if (n == 1) initial.TrySetResult();
                else secondConnect.TrySetResult();
                return Task.FromResult(n == 1); // succeeds initially, fails on reconnect
            });

        using var cts = new CancellationTokenSource();
        using var svc = CreateService();

        await svc.StartAsync(cts.Token);
        await initial.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await _pusher.OnDisconnected!(this);

        await secondConnect.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await cts.CancelAsync();
        await svc.StopAsync(CancellationToken.None);

        await _pusher.DidNotReceive().SubscribeAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnDisconnected_CanReconnectMultipleTimes()
    {
        SetupDefaultApi();

        var connectTcs = new[] { new TaskCompletionSource(), new TaskCompletionSource(), new TaskCompletionSource() };
        int connectCount = 0;

        _pusher.ConnectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var n = Interlocked.Increment(ref connectCount) - 1;
                if (n < connectTcs.Length) connectTcs[n].TrySetResult();
                return Task.FromResult(true);
            });

        using var cts = new CancellationTokenSource();
        using var svc = CreateService();

        await svc.StartAsync(cts.Token);
        await connectTcs[0].Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // First disconnect → reconnect
        await _pusher.OnDisconnected!(this);
        await connectTcs[1].Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Second disconnect → reconnect
        await _pusher.OnDisconnected!(this);
        await connectTcs[2].Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await cts.CancelAsync();
        await svc.StopAsync(CancellationToken.None);

        await _api.Received(3).AuthenticateAsync(Arg.Any<CancellationToken>());
        await _pusher.Received(3).ConnectAsync("key", "mt1", Arg.Any<CancellationToken>());
    }
}
