using UByMoen.Api.Services;
using UByMoen.Core.Client;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ────────────────────────────────────────────────────────────
var email = builder.Configuration["Moen:Email"]
    ?? throw new InvalidOperationException("Moen:Email is required in configuration");
var password = builder.Configuration["Moen:Password"]
    ?? throw new InvalidOperationException("Moen:Password is required in configuration");

// ── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Pusher WebSocket client (singleton — holds the live WS connection)
builder.Services.AddSingleton<IMoenPusherClient, MoenPusherClient>();

// HTTP client for the Moen REST API
builder.Services.AddHttpClient("moen", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("UByMoen-DotNet/10");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
})
.AddTypedClient<IMoenApiClient>((httpClient, sp) =>
{
    var pusher = sp.GetRequiredService<IMoenPusherClient>();
    var logger = sp.GetRequiredService<ILogger<MoenApiClient>>();
    return new MoenApiClient(httpClient, pusher, email, password, logger);
});

// In-memory device state cache
builder.Services.AddSingleton<DeviceStateService>();

// Background service: Pusher connection + polling
builder.Services.AddHostedService<PusherBackgroundService>();

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
