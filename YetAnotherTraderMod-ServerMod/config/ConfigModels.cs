using System.Collections.Generic;

namespace YetAnotherTraderMod.config;

public class SettingsConfig
{
    public int MinLevel { get; set; } = 1;
    public bool UnlockedByDefault { get; set; } = false;

    public int TraderRefreshMin { get; set; } = 1800;
    public int TraderRefreshMax { get; set; } = 3600;

    // true = YATM rerolls Tony's paired ammo offers, payment split, stock,
    // and questassort patch when the trader restocks.
    // false = Tony still uses the normal SPT restock timer, but YATM does not
    // rebuild/reroll the assort on restock.
    public bool RerollAssortOnRestock { get; set; } = true;

    public bool AddTraderToFleaMarket { get; set; } = true;
    public bool EnableCustomQuests { get; set; } = true; //[Quests] If false, Tony custom quests and quest zones will not load.
    public int InsurancePriceCoef { get; set; } = 25;
    public double RepairQuality { get; set; } = 0.8;

    public bool RandomizeStockAvailable { get; set; } = true;
    public int OutOfStockChance { get; set; } = 15;

    // true = offers that rolled into barter cannot be selected by the out-of-stock roll.
    // This keeps barter trades visible/available when they win the payment roll.
    public bool PreventBarterOffersOutOfStock { get; set; } = true;
    public bool UnlimitedStock { get; set; } = false;
    public double PriceMultiplier { get; set; } = 1.0;

    public bool CashOffersOnly { get; set; } = false;
    public bool RandomizeCashBarterOffers { get; set; } = true;
    public int CashOfferPercent { get; set; } = 85;

    public bool DebugLogging { get; set; } = false;
    public bool RealDebugLogging { get; set; } = false;
}

public class PriceConfigItem
{
    public string? OfferId { get; set; }

    // Paired ammo offer ID. Loose ammo keeps OfferId, pack ammo uses PackOfferId.
    // When ammo rolls cash, the pack offer is removed.
    // When ammo rolls barter, the loose offer is removed and the pack offer stays.
    public string? PackOfferId { get; set; }

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
    // With paired ammo offers, this tpl belongs to PackOfferId, not OfferId.
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
