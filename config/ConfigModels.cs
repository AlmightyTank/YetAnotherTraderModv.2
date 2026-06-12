using System.Text.Json.Serialization;

namespace Tony.Config;

public class SettingsConfig
{
    public int MinLevel { get; set; } = 1;
    public bool UnlockedByDefault { get; set; } = false;

    public int TraderRefreshMin { get; set; } = 1800;
    public int TraderRefreshMax { get; set; } = 3600;
    public bool AddTraderToFleaMarket { get; set; } = true;
    public int InsurancePriceCoef { get; set; } = 25;
    public double RepairQuality { get; set; } = 0.8;

    public bool RandomizeStockAvailable { get; set; } = true;
    public int OutOfStockChance { get; set; } = 15;
    public bool UnlimitedStock { get; set; } = false;
    public double PriceMultiplier { get; set; } = 1.0;

    // false = items.json can use BarterScheme per offer.
    // true = every offer uses Price + Currency only.
    public bool ForceCashOnly { get; set; } = false;

    public bool DebugLogging { get; set; } = false;
}

public class PriceConfigItem
{
    // Exact root assort offer id. Use this for duplicate TplId offers/presets.
    public string? OfferId { get; set; }

    // Item being sold.
    public string TplId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;

    // Used when CashOnly is true, or when settings.json ForceCashOnly is true.
    public double Price { get; set; }
    public string Currency { get; set; } = "RUB";

    // true = replace the offer payment with Price + Currency.
    // false = use BarterScheme below.
    public bool CashOnly { get; set; } = true;

    // SPT barter format:
    // Outer list = alternate payment options.
    // Inner list = required payment items for that option.
    public List<List<PaymentConfigItem>>? BarterScheme { get; set; }
}

public class PaymentConfigItem
{
    public string TplId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public double Count { get; set; } = 1;
}
