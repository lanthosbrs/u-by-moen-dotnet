namespace UByMoen.Core;

public static class MoenConstants
{
    // API base
    public const string ApiBaseUrl = "https://www.moen-iot.com";

    // Endpoints
    public const string ApiAuthenticate = "/v2/authenticate";
    public const string ApiCredentials = "/v3/credentials";
    public const string ApiShowers = "/v2/showers";
    public const string ApiShowerDetail = "/v5/showers/{0}";
    public const string ApiPusherAuth = "/v3/pusher-auth";

    // Pusher
    public const string PusherChannelPrefix = "private-";
    public const string PusherWsUrlTemplate =
        "wss://ws-{0}.pusher.com/app/{1}?protocol=7&client=dotnet-client&version=1.0";

    // Device modes
    public const string ModeOff = "off";
    public const string ModeAdjusting = "adjusting";
    public const string ModeReady = "ready";
    public const string ModePause = "pause";
    public const string ModePausedByPreset = "paused-by-preset";

    // Control actions
    public const string ActionShowerOn = "shower_on";
    public const string ActionShowerOff = "shower_off";
    public const string ActionShowerSet = "shower_set";
    public const string ActionTemperatureSet = "temperature_set";
    public const string ActionOutletsSet = "outlets_set";

    // Temperature limits (°F)
    public const double MinTemperature = 60.0;
    public const double DefaultMaxTemperature = 115.0;

    // Polling
    public const int UpdateIntervalSeconds = 30;
}
