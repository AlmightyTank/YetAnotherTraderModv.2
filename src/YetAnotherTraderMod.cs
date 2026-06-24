using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using YetAnotherTraderMod.config;
using Path = System.IO.Path;

namespace YetAnotherTraderMod.src;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.amightytank.yetanothertradermod";
    public override string Name { get; init; } = "YetAnotherTraderMod";
    public override string Author { get; init; } = "AMightyTank | Based on PrisciluOrigins by Reis/Anigx";
    public override List<string>? Contributors { get; init; } = ["Reis", "Anigx"];
    public override SemanticVersioning.Version Version { get; init; } = new("0.0.5");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.13");
    public override List<string>? Incompatibilities { get; init; } = [];
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new()
    {
        { "com.wtt.commonlib", new SemanticVersioning.Range("^2.0.20") }
    };
    public override string? Url { get; init; } = null;
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class YetAnotherTraderMod(
    ModHelper modHelper,
    ImageRouter imageRouter,
    ConfigServer configServer,
    DatabaseServer databaseServer,
    AddCustomTraderHelper addCustomTraderHelper,
    YATMUnlockService YATMUnlockService)
    : IOnLoad
{
    private readonly TraderConfig _traderConfig = configServer.GetConfig<TraderConfig>();
    private readonly RagfairConfig _ragfairConfig = configServer.GetConfig<RagfairConfig>();

    public Task OnLoad()
    {
        var pathToMod = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

        YATMLogger.Init(pathToMod);
        YATMLogger.Log("Mod OnLoad started.");

        var traderBase = modHelper.GetJsonDataFromFile<TraderBase>(pathToMod, "db/CustomTrader/Tony/base.json");
        var assort = modHelper.GetJsonDataFromFile<TraderAssort>(pathToMod, "db/CustomTrader/Tony/assort.json");
        var traderImagePath = Path.Combine(pathToMod, "db/CustomTrader/Tony/Tony.jpg");

        var config = new YATMConfig(pathToMod, databaseServer);
        config.LoadOrGenerate(traderBase, assort);

        YATMLogger.IsDebugEnabled = config.Settings.DebugLogging;
        if (YATMLogger.IsDebugEnabled)
        {
            YATMLogger.LogDebug("Debug Mode Enabled. Config Loaded.");
            YATMLogger.LogDebug($"  MinLevel: {config.Settings.MinLevel}");
            YATMLogger.LogDebug($"  UnlockedByDefault: {config.Settings.UnlockedByDefault}");
            YATMLogger.LogDebug($"  UnlimitedStock: {config.Settings.UnlimitedStock}");
            YATMLogger.LogDebug($"  RandomizeStock: {config.Settings.RandomizeStockAvailable} (Chance: {config.Settings.OutOfStockChance}%)");
            YATMLogger.LogDebug($"  PriceMultiplier: {config.Settings.PriceMultiplier}");
            YATMLogger.LogDebug($"  RandomizeCashBarterOffers: {GetBoolSetting(config.Settings, "RandomizeCashBarterOffers", true)}");
            YATMLogger.LogDebug($"  CashOfferPercent: {GetIntSetting(config.Settings, "CashOfferPercent", 85)}");
            YATMLogger.LogDebug($"  ForceCashOnly: {config.Settings.CashOffersOnly}");
        }

        traderBase.UnlockedByDefault = config.Settings.UnlockedByDefault;

        if (traderBase.LoyaltyLevels.Count > 0)
        {
            traderBase.LoyaltyLevels[0].MinLevel = config.Settings.MinLevel;
        }

        if (traderBase.LoyaltyLevels != null)
        {
            foreach (var level in traderBase.LoyaltyLevels)
            {
                try
                {
                    var prop = level.GetType().GetProperty("InsurancePriceCoefficient");
                    if (prop != null && prop.CanWrite)
                    {
                        var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                        object val = Convert.ChangeType(config.Settings.InsurancePriceCoef, targetType);
                        prop.SetValue(level, val);
                        YATMLogger.LogDebug($"[Insurance] Set Level {level.MinLevel} Coef to: {val}");
                    }
                    else
                    {
                        YATMLogger.LogDebug("[Insurance] Warning: InsurancePriceCoefficient property not found on LoyaltyLevel.");
                    }
                }
                catch (Exception ex)
                {
                    YATMLogger.Log($"[Insurance] Error setting coef for level: {ex.Message}");
                }
            }
        }

        if (traderBase.Insurance != null)
        {
            traderBase.Insurance.ExtensionData ??= new Dictionary<string, object>();
            traderBase.Insurance.ExtensionData["insurance_price_coef"] = config.Settings.InsurancePriceCoef;
        }

        if (traderBase.Repair != null)
        {
            traderBase.Repair.Quality = config.Settings.RepairQuality;
        }

        if (!config.Settings.UnlockedByDefault)
        {
            YATMUnlockService.EnableLevelLock = true;
            YATMUnlockService.MinLevelRequired = config.Settings.MinLevel;
            YATMUnlockService.OnLoad();
            YATMLogger.Log($"Level-based unlock enabled. Required level: {config.Settings.MinLevel}");
        }
        else
        {
            YATMUnlockService.EnableLevelLock = false;
            YATMUnlockService.ForceUnlock = true;
            YATMLogger.Log("Trader unlocked by default (ForceUnlock active).");
        }

        if (string.IsNullOrEmpty(traderBase.Id))
        {
            YATMLogger.Log("CRITICAL ERROR: traderBase.Id is null or empty! Hardcoding ID to ensure stability.");
            traderBase.Id = "66a0f6b2c4d8e90123456789";
        }

        traderBase.ItemsBuy ??= new() { Category = [], IdList = [] };
        traderBase.ItemsBuyProhibited ??= new() { Category = [], IdList = [] };
        traderBase.ItemsSell ??= [];

        // Apply configurable cash/barter overrides from config/items.json.
        ApplyConfiguredPayments(assort, config);

        var avatarRoute = traderBase.Avatar ?? string.Empty;
        avatarRoute = avatarRoute.Replace(".png", "").Replace(".jpg", "").Replace(".jpeg", "");
        imageRouter.AddRoute(avatarRoute, traderImagePath);

        if (config.Settings.AddTraderToFleaMarket)
        {
            _ragfairConfig.Traders.TryAdd(traderBase.Id, true);
        }
        else
        {
            _ragfairConfig.Traders.Remove(traderBase.Id);
        }

        if (config.Settings.RandomizeStockAvailable || config.Settings.UnlimitedStock)
        {
            YATMLogger.LogDebug("Starting Stock Manipulation...");

            var outOfStockNames = new List<string>();
            var random = new Random();

            int modifiedCount = 0;
            int zeroedCount = 0;

            var locales = databaseServer.GetTables().Locales.Global["en"];

            var ammoBarterPackConfigsByTpl = config.Prices
                .Select(x => new
                {
                    PackTpl = GetStringMember(x, "AmmoBarterPackTplId"),
                    PriceConfig = x
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.PackTpl))
                .GroupBy(x => x.PackTpl!)
                .ToDictionary(x => x.Key, x => x.First().PriceConfig);

            foreach (var item in assort.Items)
            {
                if (item.ParentId != "hideout")
                {
                    continue;
                }

                if (item.Upd == null)
                {
                    YATMLogger.LogDebug($"[Stock] Skipping offer with no Upd data: {item.Id}");
                    continue;
                }

                string itemName = item.Id;
                var tpl = YATMConfig.GetTemplateId(item);

                if (!string.IsNullOrEmpty(tpl)
                    && locales.Value != null
                    && locales.Value.TryGetValue($"{tpl} Name", out var nameVal))
                {
                    itemName = nameVal?.ToString() ?? item.Id;
                }

                // Ammo offers that rolled into barter were swapped to their ammo pack tpl.
                // Keep these pack offers limited to 10 stock with a 1-3 buy restriction.
                // This must run before the general random stock/unlimited logic so ammo pack barters
                // do not get zeroed out or expanded to normal/unlimited stock.
                if (!string.IsNullOrWhiteSpace(tpl)
                    && ammoBarterPackConfigsByTpl.TryGetValue(tpl, out var ammoPackPriceConfig))
                {
                    ApplyAmmoPackBarterOfferLimits(item, ammoPackPriceConfig);
                    modifiedCount++;
                    continue;
                }

                if (config.Settings.RandomizeStockAvailable)
                {
                    if (random.Next(0, 100) < config.Settings.OutOfStockChance)
                    {
                        // Keep the offer loaded in the trader assort.
                        // Do not remove the root item, child items, barter scheme, or loyalty entry.
                        // Setting stock to 0 makes it show as out of stock instead of disappearing.
                        item.Upd.UnlimitedCount = false;
                        item.Upd.StackObjectsCount = 0;

                        if (item.Upd.BuyRestrictionMax > 0)
                        {
                            item.Upd.BuyRestrictionCurrent = 0;
                        }

                        zeroedCount++;
                        outOfStockNames.Add($"{itemName} ({item.Id})");

                        YATMLogger.LogDebug($"[Random Stock] zeroed stock: {itemName} ({item.Id})");
                        continue;
                    }
                }

                if (config.Settings.UnlimitedStock)
                {
                    item.Upd.UnlimitedCount = true;
                    item.Upd.StackObjectsCount = 999999;

                    if (item.Upd.BuyRestrictionMax > 0)
                    {
                        item.Upd.BuyRestrictionMax = 9999;
                        item.Upd.BuyRestrictionCurrent = 0;
                    }

                    modifiedCount++;
                }
                else
                {
                    item.Upd.UnlimitedCount = false;
                    item.Upd.StackObjectsCount = 100;
                    modifiedCount++;
                }
            }

            YATMLogger.LogDebug($"Total items modified for Stock setting: {modifiedCount}");

            if (zeroedCount > 0)
            {
                YATMLogger.Log($"[Stock] Zeroed {zeroedCount} offers due to randomization.");
                YATMLogger.LogDebug($"Out of Stock Items:\n  {string.Join("\n  ", outOfStockNames)}");
            }
            else
            {
                YATMLogger.LogDebug("No items were zeroed by randomization this turn.");
            }
        }

        // Price multiplier now only affects money components, not barter item counts.
        if (Math.Abs(config.Settings.PriceMultiplier - 1.0) > 0.001)
        {
            YATMLogger.LogDebug($"Applying Price Multiplier {config.Settings.PriceMultiplier}...");
            int changedCount = 0;

            var itemMap = assort.Items.ToDictionary(x => x.Id, x => x);
            var localesForPrice = databaseServer.GetTables().Locales.Global["en"];

            foreach (var itemSchemePair in assort.BarterScheme)
            {
                var itemId = itemSchemePair.Key;
                var schemeList = itemSchemePair.Value;

                foreach (var schemeSubList in schemeList)
                {
                    foreach (var component in schemeSubList)
                    {
                        if (component.Count.HasValue && YATMConfig.IsCurrencyTemplate(component.Template.ToString()))
                        {
                            var oldPrice = component.Count.Value;
                            component.Count = (double)Math.Round(component.Count.Value * config.Settings.PriceMultiplier);

                            string itemName = itemId;
                            if (itemMap.TryGetValue(itemId, out var item))
                            {
                                var tpl = YATMConfig.GetTemplateId(item);
                                if (!string.IsNullOrEmpty(tpl) && localesForPrice.Value != null && localesForPrice.Value.TryGetValue($"{tpl} Name", out var nameVal))
                                {
                                    itemName = nameVal?.ToString() ?? itemId;
                                }
                            }

                            YATMLogger.LogDebug($"  Price adjust: {oldPrice} -> {component.Count} | {itemName} ({itemId})");
                            changedCount++;
                        }
                    }
                }
            }

            YATMLogger.Log($"[Pricing] Applied Global Price Multiplier: {config.Settings.PriceMultiplier} to {changedCount} money components.");
        }

        var timerRandom = new Random();
        int restockTime = timerRandom.Next(config.Settings.TraderRefreshMin, config.Settings.TraderRefreshMax);

        YATMLogger.Log($"Setting trader restock timer to {restockTime} seconds.");
        addCustomTraderHelper.SetTraderUpdateTime(
            _traderConfig,
            traderBase,
            restockTime,
            restockTime);

        traderBase.NextResupply = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + restockTime);

        addCustomTraderHelper.AddTraderToDb(traderBase, assort);

        if (config.Settings.DebugLogging)
        {
            YATMLogger.Log("Trader initialized. Debug Enabled.");
        }

        var localeFirstName = traderBase.Nickname ?? traderBase.Name ?? "Tony";
        var localeDescription = "An ex-BEAR operator and former enforcer for Russian organized crime. After Tarkov collapsed, Volkov turned old connections into a quiet business, supplying weapons, armor, and contraband to smugglers, mercenaries, and criminals. He respects usefulness, hates weakness, and only opens doors for those who earn his trust.";
        addCustomTraderHelper.AddTraderToLocales(traderBase, localeFirstName, localeDescription);

        return Task.CompletedTask;
    }

    private static void ApplyConfiguredPayments(TraderAssort assort, YATMConfig config)
    {
        var rootItems = assort.Items
            .Where(x => x.ParentId == "hideout")
            .ToList();

        var configuredOffers = new List<(object Offer, PriceConfigItem PriceConfig)>();
        var configuredOfferIds = new HashSet<string>();

        foreach (var priceConfig in config.Prices)
        {
            var matchingOffers = rootItems
                .Where(item => DoesConfigMatchOffer(item, priceConfig))
                .ToList();

            if (matchingOffers.Count == 0)
            {
                YATMLogger.LogDebug($"[Pricing] No matching offer for {priceConfig.ItemName} / {priceConfig.TplId}");
                continue;
            }

            if (matchingOffers.Count > 1 && string.IsNullOrWhiteSpace(priceConfig.OfferId))
            {
                YATMLogger.LogDebug($"[Pricing] Multiple offers matched TplId {priceConfig.TplId}. Add OfferId to items.json for exact control.");
            }

            foreach (var offer in matchingOffers)
            {
                var offerId = GetMemberValue(offer, "Id")?.ToString();
                if (string.IsNullOrWhiteSpace(offerId))
                {
                    YATMLogger.LogDebug($"[Pricing] Matched offer for {priceConfig.ItemName} has no Id.");
                    continue;
                }

                // Avoid applying the same offer more than once if items.json has duplicate tpl matches.
                if (!configuredOfferIds.Add(offerId))
                {
                    YATMLogger.LogDebug($"[Pricing] Duplicate configured offer skipped: {priceConfig.ItemName} ({offerId})");
                    continue;
                }

                configuredOffers.Add((offer, priceConfig));
            }
        }

        if (configuredOffers.Count == 0)
        {
            YATMLogger.LogDebug("[Pricing] No configured offers were matched.");
            return;
        }

        var CashOffersOnly = config.Settings.CashOffersOnly;
        var randomizeCashBarter = GetBoolSetting(config.Settings, "RandomizeCashBarterOffers", true) && !CashOffersOnly;
        var selectedBarterOfferIds = new HashSet<string>();

        if (randomizeCashBarter)
        {
            var cashPercent = Math.Clamp(GetIntSetting(config.Settings, "CashOfferPercent", 85), 0, 100);
            var barterPercent = 100 - cashPercent;
            var requestedBarterCount = (int)Math.Round(
                configuredOffers.Count * (barterPercent / 100.0),
                MidpointRounding.AwayFromZero);

            var forcedBarterOfferIds = configuredOffers
                .Where(x => IsAlwaysBarter(x.PriceConfig) && HasUsableBarterScheme(x.PriceConfig))
                .Select(x => GetMemberValue(x.Offer, "Id")?.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToHashSet();

            var invalidAlwaysBarterCount = configuredOffers
                .Count(x => IsAlwaysBarter(x.PriceConfig) && !HasUsableBarterScheme(x.PriceConfig));

            if (invalidAlwaysBarterCount > 0)
            {
                YATMLogger.Log($"[Pricing] Warning: {invalidAlwaysBarterCount} AlwaysBarter rows have no usable barter scheme and cannot be forced to barter.");
            }

            var random = new Random();
            var eligibleRandomBarterOfferIds = configuredOffers
                .Where(x => HasUsableBarterScheme(x.PriceConfig))
                .Select(x => GetMemberValue(x.Offer, "Id")?.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .Where(x => !forcedBarterOfferIds.Contains(x))
                .OrderBy(_ => random.Next())
                .ToList();

            // AlwaysBarter offers are guaranteed barter and still count against the target barter percent.
            // Example: 15 target barter offers and 2 AlwaysBarter rows means only 13 more are randomly selected.
            var targetBarterCount = Math.Clamp(requestedBarterCount, 0, forcedBarterOfferIds.Count + eligibleRandomBarterOfferIds.Count);
            var randomBarterSlots = Math.Max(0, targetBarterCount - forcedBarterOfferIds.Count);

            selectedBarterOfferIds = forcedBarterOfferIds
                .Concat(eligibleRandomBarterOfferIds.Take(randomBarterSlots))
                .ToHashSet();

            var targetCashCount = configuredOffers.Count - selectedBarterOfferIds.Count;
            YATMLogger.Log($"[Pricing] Random payment split enabled: {targetCashCount} cash offers / {selectedBarterOfferIds.Count} barter offers ({forcedBarterOfferIds.Count} forced barter).");

            if (forcedBarterOfferIds.Count > requestedBarterCount)
            {
                YATMLogger.Log($"[Pricing] Warning: AlwaysBarter rows ({forcedBarterOfferIds.Count}) exceed requested barter count ({requestedBarterCount}). All AlwaysBarter rows were kept as barter and no random barter offers were added.");
            }

            if ((forcedBarterOfferIds.Count + eligibleRandomBarterOfferIds.Count) < requestedBarterCount)
            {
                YATMLogger.Log($"[Pricing] Warning: requested {requestedBarterCount} barter offers, but only {forcedBarterOfferIds.Count + eligibleRandomBarterOfferIds.Count} offers have real barter schemes available.");
            }
        }

        foreach (var configuredOffer in configuredOffers)
        {
            var offerId = GetMemberValue(configuredOffer.Offer, "Id")?.ToString();
            if (string.IsNullOrWhiteSpace(offerId))
            {
                continue;
            }

            var useBarter = randomizeCashBarter && selectedBarterOfferIds.Contains(offerId);
            ApplyPaymentToOffer(
                assort,
                configuredOffer.Offer,
                offerId,
                configuredOffer.PriceConfig,
                CashOffersOnly,
                randomizeCashBarter,
                useBarter);
        }
    }

    private static bool DoesConfigMatchOffer(object item, PriceConfigItem priceConfig)
    {
        var itemId = GetMemberValue(item, "Id")?.ToString();

        if (!string.IsNullOrWhiteSpace(priceConfig.OfferId))
        {
            return itemId == priceConfig.OfferId;
        }

        var tpl = YATMConfig.GetTemplateId(item);
        return !string.IsNullOrEmpty(tpl) && tpl == priceConfig.TplId;
    }

    private static void ApplyPaymentToOffer(
        TraderAssort assort,
        object offer,
        string offerId,
        PriceConfigItem priceConfig,
        bool CashOffersOnly,
        bool randomizeCashBarter,
        bool useBarter)
    {
        if (!assort.BarterScheme.TryGetValue(offerId, out var existingSchemeList))
        {
            YATMLogger.LogDebug($"[Pricing] Offer {offerId} has no barter_scheme entry.");
            return;
        }

        if (randomizeCashBarter)
        {
            if (useBarter && HasUsableBarterScheme(priceConfig))
            {
                ApplyBarterPaymentToOffer(offer, existingSchemeList, priceConfig);
                return;
            }

            ApplyCashPaymentToOffer(offer, existingSchemeList, priceConfig);
            YATMLogger.LogDebug($"[Pricing] Random cash offer: {priceConfig.ItemName} = {priceConfig.Price} {priceConfig.Currency}");
            return;
        }

        var shouldUseCash = CashOffersOnly
            || !HasUsableBarterScheme(priceConfig)
            || (!IsAlwaysBarter(priceConfig) && priceConfig.CashOnly);

        if (shouldUseCash)
        {
            ApplyCashPaymentToOffer(offer, existingSchemeList, priceConfig);
            YATMLogger.LogDebug($"[Pricing] Cash override: {priceConfig.ItemName} = {priceConfig.Price} {priceConfig.Currency}");
            return;
        }

        ApplyBarterPaymentToOffer(offer, existingSchemeList, priceConfig);
    }

    private static void ApplyBarterPaymentToOffer(object offer, object existingSchemeList, PriceConfigItem priceConfig)
    {
        // If this items.json row has AmmoBarterPackTplId, that tpl is the actual sold item.
        // Do not calculate or choose a pack here; trust config/items.json as the source of truth.
        var ammoPackTpl = GetStringMember(priceConfig, "AmmoBarterPackTplId");
        var targetTpl = !string.IsNullOrWhiteSpace(ammoPackTpl)
            ? ammoPackTpl
            : priceConfig.TplId;

        SetOfferTemplate(offer, targetTpl);

        if (!string.IsNullOrWhiteSpace(ammoPackTpl))
        {
            ApplyAmmoPackBarterOfferLimits(offer, priceConfig);
            YATMLogger.LogDebug($"[Pricing] Ammo pack barter offer: {priceConfig.ItemName} | _tpl = {targetTpl}");
        }
        else
        {
            YATMLogger.LogDebug($"[Pricing] Barter offer: {priceConfig.ItemName} | _tpl = {targetTpl}");
        }

        ReplaceOfferPaymentScheme(existingSchemeList, priceConfig.BarterScheme!);
    }

    private static void ApplyAmmoPackBarterOfferLimits(object offer, PriceConfigItem priceConfig)
    {
        var upd = GetMemberValue(offer, "Upd");
        if (upd == null)
        {
            YATMLogger.LogDebug($"[Pricing] Ammo pack barter stock skipped because offer has no Upd data: {priceConfig.ItemName ?? "Unknown item"}");
            return;
        }

        var buyRestrictionMax = GetAmmoPackBuyRestrictionMax(priceConfig);

        SetMemberValue(upd, "UnlimitedCount", false);
        SetMemberValue(upd, "StackObjectsCount", 10);
        SetMemberValue(upd, "BuyRestrictionMax", buyRestrictionMax);
        SetMemberValue(upd, "BuyRestrictionCurrent", 0);

        YATMLogger.LogDebug($"[Pricing] Ammo pack barter stock: {priceConfig.ItemName ?? "Unknown item"} | StackObjectsCount 10 | BuyRestrictionMax {buyRestrictionMax}");
    }

    private static int GetAmmoPackBuyRestrictionMax(PriceConfigItem priceConfig)
    {
        // Use the actual ammo pack tpl from items.json.
        // This is the tpl that the live assort root item is changed to when ammo rolls barter.
        var packTpl = GetStringMember(priceConfig, "AmmoBarterPackTplId") ?? string.Empty;

        if (IsHighTierAmmoPack(packTpl))
        {
            return 1;
        }

        if (IsMidTierAmmoPack(packTpl))
        {
            return 2;
        }

        // Anything not listed as high/mid is treated as low-tier ammo pack.
        return 3;
    }

    private static bool IsHighTierAmmoPack(string packTpl)
    {
        return IsTplMatch(packTpl,
            // .366 TKM AP-M ammo pack (20 pcs)
            "657023f81419851aef03e6f1",

            // 12/70 AP-20 ammo pack (25 pcs)
            "64898838d5b4df6140000a20",

            // 5.45x39mm PPBS Igolnik ammo pack (120 pcs)
            "657025ebc5d7d4cb4d078588",

            // 5.45x39mm BS gs ammo pack (120 pcs)
            "57372b832459776701014e41",

            // 7.62x39mm MAI AP ammo pack (20 pcs)
            "6489851fc827d4637f01791b",

            // 9x19mm PBP ammo pack (50 pcs)
            "648987d673c462723909a151",

            // 9x39mm BP ammo pack (20 pcs)
            "6489854673c462723909a14e",

            // 9x39mm SP-6 ammo pack (20 pcs)
            "657025dabfc87b3a34093256"
        );
    }

    private static bool IsMidTierAmmoPack(string packTpl)
    {
        return IsTplMatch(packTpl,
            // 12/70 flechette ammo pack (25 pcs)
            "65702474bfc87b3a34093226",

            // 5.45x39mm BP gs ammo pack (120 pcs)
            "5737292724597765e5728562",

            // 5.45x39mm BT gs ammo pack
            "57372c21245977670937c6c2",

            // 5.45x39mm PP gs ammo pack (120 pcs)
            "57372d1b2459776862260581",

            // 7.62x39mm BP gzh ammo pack (20 pcs)
            "64acea16c4eda9354b0226b0",

            // 7.62x39mm PP gzh ammo pack (20 pcs)
            "64ace9f9c4eda9354b0226aa",

            // 9x19mm AP 6.3 ammo pack (50 pcs)
            "65702591c5d7d4cb4d07857c",

            // 9x19mm RIP ammo pack (20 pcs)
            "5c1127bdd174af44217ab8b9",

            // 9x39mm SPP ammo pack (20 pcs)
            "657025dfcfc010a0f5006a3b",

            // 9x39mm PAB-9 ammo pack (20 pcs)
            "657025cfbfc87b3a34093253"
        );
    }

    private static bool IsTplMatch(string tpl, params string[] tplIds)
    {
        foreach (var tplId in tplIds)
        {
            if (tpl.Equals(tplId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void ApplyCashPaymentToOffer(object offer, object existingSchemeList, PriceConfigItem priceConfig)
    {
        // Cash offers sell the normal configured item. For ammo this means the loose bullet tpl.
        SetOfferTemplate(offer, priceConfig.TplId);

        var currencyTpl = YATMConfig.CurrencyToTemplate(priceConfig.Currency);

        ReplaceOfferPaymentScheme(existingSchemeList, new List<List<PaymentConfigItem>>
        {
            new()
            {
                new PaymentConfigItem
                {
                    TplId = currencyTpl,
                    ItemName = priceConfig.Currency.ToUpperInvariant(),
                    Count = priceConfig.Price
                }
            }
        });
    }

    private static void SetOfferTemplate(object offer, string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return;
        }

        // This is the important live assort mutation.
        // For ammo barter offers, templateId must be priceConfig.AmmoBarterPackTplId from items.json.
        // The serialized SPT assort uses _tpl, so write _tpl first and also update aliases used by model wrappers.
        SetMemberValue(offer, "_tpl", templateId);
        SetMemberValue(offer, "Template", templateId);
        SetMemberValue(offer, "Tpl", templateId);
        SetMemberValue(offer, "TemplateId", templateId);

        // Some SPT model objects keep raw JSON-only fields in ExtensionData.
        // Force _tpl there too so AddTraderToDb serializes the ammo pack tpl, not the loose ammo tpl.
        SetExtensionDataValue(offer, "_tpl", templateId);
        SetExtensionDataValue(offer, "tpl", templateId);
        SetExtensionDataValue(offer, "Template", templateId);
        SetExtensionDataValue(offer, "Tpl", templateId);
        SetExtensionDataValue(offer, "TemplateId", templateId);

        var rawTpl = GetMemberValue(offer, "_tpl")?.ToString();
        var resolvedTpl = YATMConfig.GetTemplateId(offer);
        if (!string.Equals(rawTpl, templateId, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(resolvedTpl, templateId, StringComparison.OrdinalIgnoreCase))
        {
            var offerId = GetMemberValue(offer, "Id")?.ToString() ?? "unknown offer";
            YATMLogger.LogDebug($"[Pricing] Warning: attempted to set assort _tpl for {offerId} to {templateId}, but readback returned _tpl={rawTpl ?? "null"}, resolved={resolvedTpl ?? "null"}.");
        }
    }

    private static void SetExtensionDataValue(object target, string key, object? value)
    {
        var type = target.GetType();

        var extensionMember = type.GetProperty(
                "ExtensionData",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? (MemberInfo?)type.GetField(
                "ExtensionData",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (extensionMember == null)
        {
            return;
        }

        object? extensionData = extensionMember switch
        {
            PropertyInfo prop => prop.GetValue(target),
            FieldInfo field => field.GetValue(target),
            _ => null
        };

        if (extensionData == null)
        {
            // Most SPT models use Dictionary<string, object> for ExtensionData.
            extensionData = new Dictionary<string, object?>();

            try
            {
                switch (extensionMember)
                {
                    case PropertyInfo prop when prop.CanWrite:
                        prop.SetValue(target, extensionData);
                        break;
                    case FieldInfo field:
                        field.SetValue(target, extensionData);
                        break;
                }
            }
            catch
            {
                return;
            }
        }

        if (extensionData is IDictionary dictionary)
        {
            dictionary[key] = value;
        }
    }

    private static string? GetStringMember(object target, string memberName)
    {
        return GetMemberValue(target, memberName)?.ToString();
    }

    private static int GetIntMember(object target, string memberName, int defaultValue)
    {
        var value = GetMemberValue(target, memberName);
        if (value == null)
        {
            return defaultValue;
        }

        if (value is int intValue)
        {
            return intValue;
        }

        if (int.TryParse(value.ToString(), out var parsedInt))
        {
            return parsedInt;
        }

        return defaultValue;
    }

    private static bool IsAlwaysBarter(PriceConfigItem priceConfig)
    {
        return priceConfig.AlwaysBarter;
    }

    private static bool HasUsableBarterScheme(PriceConfigItem priceConfig)
    {
        if (priceConfig.BarterScheme == null || priceConfig.BarterScheme.Count == 0)
        {
            return false;
        }

        foreach (var paymentOption in priceConfig.BarterScheme)
        {
            if (paymentOption == null || paymentOption.Count == 0)
            {
                continue;
            }

            foreach (var paymentConfig in paymentOption)
            {
                if (paymentConfig == null || string.IsNullOrWhiteSpace(paymentConfig.TplId))
                {
                    continue;
                }

                if (!YATMConfig.IsCurrencyTemplate(paymentConfig.TplId))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void ReplaceOfferPaymentScheme(object existingSchemeListObject, List<List<PaymentConfigItem>> newScheme)
    {
        if (existingSchemeListObject is not IList existingSchemeList)
        {
            throw new InvalidOperationException("Trader barter scheme list is not IList-compatible.");
        }

        var paymentComponentType = FindExistingPaymentComponentType(existingSchemeList);
        if (paymentComponentType == null)
        {
            throw new InvalidOperationException("Could not determine SPT barter payment component type.");
        }

        var paymentListType = typeof(List<>).MakeGenericType(paymentComponentType);

        existingSchemeList.Clear();

        foreach (var paymentOption in newScheme)
        {
            var newPaymentOptionList = (IList)Activator.CreateInstance(paymentListType)!;

            foreach (var paymentConfig in paymentOption)
            {
                var newPaymentComponent = Activator.CreateInstance(paymentComponentType)!;

                // Set Template and Count using the robust helper below
                SetMemberValue(newPaymentComponent, "Template", paymentConfig.TplId);
                SetMemberValue(newPaymentComponent, "Count", paymentConfig.Count);

                newPaymentOptionList.Add(newPaymentComponent);
            }

            existingSchemeList.Add(newPaymentOptionList);
        }
    }

    private static Type? FindExistingPaymentComponentType(IList existingSchemeList)
    {
        foreach (var paymentOption in existingSchemeList)
        {
            if (paymentOption is not IList paymentComponents)
            {
                continue;
            }

            if (paymentComponents.Count > 0 && paymentComponents[0] != null)
            {
                return paymentComponents[0]!.GetType();
            }
        }

        return null;
    }

    private static bool GetBoolSetting(object settings, string settingName, bool defaultValue)
    {
        var value = GetMemberValue(settings, settingName);
        if (value == null)
        {
            return defaultValue;
        }

        if (value is bool boolValue)
        {
            return boolValue;
        }

        if (bool.TryParse(value.ToString(), out var parsedBool))
        {
            return parsedBool;
        }

        if (int.TryParse(value.ToString(), out var parsedInt))
        {
            return parsedInt != 0;
        }

        return defaultValue;
    }

    private static int GetIntSetting(object settings, string settingName, int defaultValue)
    {
        var value = GetMemberValue(settings, settingName);
        if (value == null)
        {
            return defaultValue;
        }

        if (value is int intValue)
        {
            return intValue;
        }

        if (int.TryParse(value.ToString(), out var parsedInt))
        {
            return parsedInt;
        }

        return defaultValue;
    }

    private static object? GetMemberValue(object target, string memberName)
    {
        var type = target.GetType();

        var prop = type.GetProperty(
            memberName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (prop != null)
        {
            return prop.GetValue(target);
        }

        var field = type.GetField(
            memberName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (field != null)
        {
            return field.GetValue(target);
        }

        if (!memberName.Equals("ExtensionData", StringComparison.OrdinalIgnoreCase))
        {
            var extensionData = type.GetProperty(
                    "ExtensionData",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                ?.GetValue(target)
                ?? type.GetField(
                    "ExtensionData",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                ?.GetValue(target);

            if (extensionData is IDictionary genericDictionary)
            {
                foreach (DictionaryEntry entry in genericDictionary)
                {
                    if (entry.Key?.ToString()?.Equals(memberName, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        return entry.Value;
                    }
                }
            }
        }

        return null;
    }

    private static void SetMemberValue(object target, string memberName, object? value)
    {
        var type = target.GetType();

        var prop = type.GetProperty(
            memberName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (prop != null && prop.CanWrite)
        {
            var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

            object? convertedValue = null;

            if (value != null)
            {
                try
                {
                    // Special-case MongoId-like types which can't be handled by Convert.ChangeType from string.
                    if (value is string s && (targetType.Name == "MongoId" || targetType.FullName?.EndsWith(".MongoId") == true))
                    {
                        // Try constructor(string)
                        var ctor = targetType.GetConstructor(new[] { typeof(string) });
                        if (ctor != null)
                        {
                            convertedValue = ctor.Invoke(new object[] { s });
                        }
                        else
                        {
                            // Try static Parse/TryParse/FromString methods if present
                            var parseMethod = targetType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null)
                                           ?? targetType.GetMethod("FromString", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);

                            if (parseMethod != null)
                            {
                                convertedValue = parseMethod.Invoke(null, new object[] { s });
                            }
                            else
                            {
                                // As a last resort, attempt Activator.CreateInstance with the string (some structs support it)
                                try
                                {
                                    convertedValue = Activator.CreateInstance(targetType, new object[] { s });
                                }
                                catch
                                {
                                    convertedValue = null;
                                }
                            }
                        }
                    }
                    else
                    {
                        convertedValue = Convert.ChangeType(value, targetType);
                    }
                }
                catch
                {
                    convertedValue = null;
                }
            }

            try
            {
                prop.SetValue(target, convertedValue);
            }
            catch (Exception)
            {
                // If assignment fails silently skip - best effort (avoids throwing on incompatible runtime SPT types)
            }

            return;
        }

        var field = type.GetField(
            memberName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (field != null)
        {
            var targetType = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
            object? convertedValue = null;

            if (value != null)
            {
                try
                {
                    if (value is string s && (targetType.Name == "MongoId" || targetType.FullName?.EndsWith(".MongoId") == true))
                    {
                        var ctor = targetType.GetConstructor(new[] { typeof(string) });
                        if (ctor != null)
                        {
                            convertedValue = ctor.Invoke(new object[] { s });
                        }
                        else
                        {
                            var parseMethod = targetType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null)
                                           ?? targetType.GetMethod("FromString", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);

                            if (parseMethod != null)
                            {
                                convertedValue = parseMethod.Invoke(null, new object[] { s });
                            }
                            else
                            {
                                try
                                {
                                    convertedValue = Activator.CreateInstance(targetType, new object[] { s });
                                }
                                catch
                                {
                                    convertedValue = null;
                                }
                            }
                        }
                    }
                    else
                    {
                        convertedValue = Convert.ChangeType(value, targetType);
                    }
                }
                catch
                {
                    convertedValue = null;
                }
            }

            try
            {
                field.SetValue(target, convertedValue);
            }
            catch (Exception)
            {
                // swallow - best effort
            }
        }
    }
}
