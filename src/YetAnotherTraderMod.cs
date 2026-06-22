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
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.11");
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
            YATMLogger.LogDebug($"  ForceCashOnly: {config.Settings.ForceCashOnly}");
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

        var forceCashOnly = config.Settings.ForceCashOnly;
        var randomizeCashBarter = GetBoolSetting(config.Settings, "RandomizeCashBarterOffers", true) && !forceCashOnly;
        var selectedBarterOfferIds = new HashSet<string>();

        if (randomizeCashBarter)
        {
            var cashPercent = Math.Clamp(GetIntSetting(config.Settings, "CashOfferPercent", 85), 0, 100);
            var barterPercent = 100 - cashPercent;
            var requestedBarterCount = (int)Math.Round(
                configuredOffers.Count * (barterPercent / 100.0),
                MidpointRounding.AwayFromZero);

            var random = new Random();
            var eligibleBarterOfferIds = configuredOffers
                .Where(x => HasUsableBarterScheme(x.PriceConfig))
                .Select(x => GetMemberValue(x.Offer, "Id")?.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .OrderBy(_ => random.Next())
                .ToList();

            var targetBarterCount = Math.Clamp(requestedBarterCount, 0, eligibleBarterOfferIds.Count);
            selectedBarterOfferIds = eligibleBarterOfferIds
                .Take(targetBarterCount)
                .ToHashSet();

            var targetCashCount = configuredOffers.Count - selectedBarterOfferIds.Count;
            YATMLogger.Log($"[Pricing] Random payment split enabled: {targetCashCount} cash offers / {selectedBarterOfferIds.Count} barter offers.");

            if (eligibleBarterOfferIds.Count < requestedBarterCount)
            {
                YATMLogger.Log($"[Pricing] Warning: requested {requestedBarterCount} barter offers, but only {eligibleBarterOfferIds.Count} offers have real barter schemes available.");
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
                forceCashOnly,
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
        bool forceCashOnly,
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

        var shouldUseCash = forceCashOnly
            || priceConfig.CashOnly
            || !HasUsableBarterScheme(priceConfig);

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
        // Normal items keep their configured sold tpl.
        // Ammo rows generated with AmmoBarterPackTplId swap the sold item to the ammo pack tpl
        // only when that offer is actually using barter.
        var ammoPackTpl = GetStringMember(priceConfig, "AmmoBarterPackTplId");
        var ammoPackName = GetStringMember(priceConfig, "AmmoBarterPackItemName");
        var ammoPackSize = GetIntMember(priceConfig, "AmmoBarterPackSize", 0);

        if (!string.IsNullOrWhiteSpace(ammoPackTpl))
        {
            SetOfferTemplate(offer, ammoPackTpl);

            if (!string.IsNullOrWhiteSpace(ammoPackName) && ammoPackSize > 0)
            {
                YATMLogger.LogDebug($"[Pricing] Ammo pack barter offer: {priceConfig.ItemName} -> {ammoPackName} ({ammoPackSize} pcs)");
            }
            else
            {
                YATMLogger.LogDebug($"[Pricing] Ammo pack barter offer: {priceConfig.ItemName} -> {ammoPackTpl}");
            }
        }
        else
        {
            SetOfferTemplate(offer, priceConfig.TplId);
            YATMLogger.LogDebug($"[Pricing] Barter offer: {priceConfig.ItemName}");
        }

        ReplaceOfferPaymentScheme(existingSchemeList, priceConfig.BarterScheme!);
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

        // Different SPT model versions expose the sold template id under different names.
        // Setting all known aliases is safe because SetMemberValue ignores missing members.
        SetMemberValue(offer, "Template", templateId);
        SetMemberValue(offer, "Tpl", templateId);
        SetMemberValue(offer, "_tpl", templateId);
        SetMemberValue(offer, "TemplateId", templateId);
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
