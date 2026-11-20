using System.Text.Json.Serialization;

namespace DailyReminder;

public sealed class ReminderData
{
    [JsonPropertyName("settings")]
    public ReminderSettings Settings { get; set; } = new();

    [JsonPropertyName("tasks")]
    public List<ReminderTask> Tasks { get; set; } = new();
}

public sealed class ReminderSettings
{
    [JsonPropertyName("always_on_top")]
    public bool AlwaysOnTop { get; set; } = true;

    [JsonPropertyName("window_width")]
    public double WindowWidth { get; set; } = 380;

    [JsonPropertyName("window_height")]
    public double WindowHeight { get; set; } = 220;

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "night";

    [JsonPropertyName("glass_opacity")]
    public double GlassOpacity { get; set; } = 0.88;

    [JsonPropertyName("background_image")]
    public string BackgroundImage { get; set; } = "";

    [JsonPropertyName("background_opacity")]
    public double BackgroundOpacity { get; set; } = 0.65;

    [JsonPropertyName("reminder_style")]
    public string ReminderStyle { get; set; } = "bounce";
}

public sealed class ReminderTask
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("time")]
    public string Time { get; set; } = "";

    [JsonPropertyName("repeat")]
    public string Repeat { get; set; } = "daily";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("start_date")]
    public string StartDate { get; set; } = "";

    [JsonPropertyName("done")]
    public string Done { get; set; } = "";

    [JsonPropertyName("last_fired")]
    public string LastFired { get; set; } = "";

    [JsonPropertyName("created")]
    public string Created { get; set; } = "";
}

public sealed class TaskRow
{
    public required string Id { get; init; }
    public required string Time { get; init; }
    public required string Title { get; set; }
    public required string Meta { get; init; }
    public required bool IsDone { get; init; }
    public required bool HasReminder { get; init; }
}
