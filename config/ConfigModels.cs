using System.Collections.Generic;

namespace YetAnotherTraderMod.config;

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

    public bool CashOffersOnly { get; set; } = false;
    public bool RandomizeCashBarterOffers { get; set; } = true;
    public int CashOfferPercent { get; set; } = 85;

    public bool DebugLogging { get; set; } = false;
}

public class PriceConfigItem
{
    public string? OfferId { get; set; }

    // Normal sold item. For ammo cash offers, this stays as the loose bullet tpl.
    public string TplId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;

    // For ammo, this is the per-bullet price.
    public double Price { get; set; }
    public string Currency { get; set; } = "RUB";

    public bool CashOnly { get; set; } = true;

    // true = this row is forced to barter when it has a real BarterScheme.
    // It still counts toward the configured barter percentage.
    public bool AlwaysBarter { get; set; } = false;

    // true = this row can never be zeroed by RandomizeStockAvailable.
    // Useful for quest/progression items like MS2000 Marker and WI-FI Camera.
    public bool AlwaysInStock { get; set; } = false;

    // For ammo pack barter rows, this should already be valued against:
    // Price * AmmoBarterPackSize
    public List<List<PaymentConfigItem>>? BarterScheme { get; set; }

    // Ammo-only barter metadata.
    // When ammo rolls barter, the assort root tpl is changed to this pack tpl.
    public string? AmmoBarterPackTplId { get; set; }
    public string? AmmoBarterPackItemName { get; set; }
    public int AmmoBarterPackSize { get; set; } = 0;

    // "Unit" = BarterScheme is valued against Price.
    // "Pack" = BarterScheme is already valued against Price * AmmoBarterPackSize.
    public string BarterSchemeValueBasis { get; set; } = "Unit";
}

public class PaymentConfigItem
{
    public string TplId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public double Count { get; set; } = 1;
}
