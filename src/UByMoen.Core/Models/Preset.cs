using System.Text.Json.Serialization;

namespace UByMoen.Core.Models;

public class Preset
{
    [JsonPropertyName("position")]
    public int Position { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("greeting")]
    public string Greeting { get; set; } = string.Empty;

    [JsonPropertyName("target_temperature")]
    public double TargetTemperature { get; set; }

    [JsonPropertyName("outlets")]
    public List<Outlet> Outlets { get; set; } = [];

    [JsonPropertyName("timer_enabled")]
    public bool TimerEnabled { get; set; }

    [JsonPropertyName("timer_length")]
    public int TimerLength { get; set; } = 600;

    [JsonPropertyName("timer_ends_shower")]
    public bool TimerEndsShower { get; set; }

    [JsonPropertyName("timer_sounds_alert")]
    public bool TimerSoundsAlert { get; set; } = true;

    [JsonPropertyName("ready_pauses_water")]
    public bool ReadyPausesWater { get; set; }

    [JsonPropertyName("ready_pushes_notification")]
    public bool ReadyPushesNotification { get; set; }

    [JsonPropertyName("ready_sounds_alert")]
    public bool ReadySoundsAlert { get; set; } = true;
}
