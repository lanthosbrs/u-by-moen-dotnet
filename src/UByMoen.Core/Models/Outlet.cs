using System.Text.Json.Serialization;

namespace UByMoen.Core.Models;

public class Outlet
{
    [JsonPropertyName("position")]
    public int Position { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("icon_index")]
    public int IconIndex { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
