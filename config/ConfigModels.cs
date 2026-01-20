using System.Text.Json.Serialization;

namespace PrisciluOrigins.Config;

public class SettingsConfig
{
    public int MinLevel { get; set; } = 1;
    public bool UnlockedByDefault { get; set; } = false;
    public int RestockTimerSeconds { get; set; } = 3600;
}

public class PriceConfigItem
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? _Comment { get; set; } // For user readability

    public string TplId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public double Price { get; set; }
    public string Currency { get; set; } = "RUB";
}
