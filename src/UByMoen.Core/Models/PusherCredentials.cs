using System.Text.Json.Serialization;

namespace UByMoen.Core.Models;

public class PusherCredentials
{
    [JsonPropertyName("app_key")]
    public string AppKey { get; set; } = string.Empty;

    [JsonPropertyName("cluster")]
    public string Cluster { get; set; } = string.Empty;
}
