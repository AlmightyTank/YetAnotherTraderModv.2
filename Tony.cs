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
using Path = System.IO.Path;
using Tony.config;
using Tony.Config;

namespace Tony;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.amightytank.yetanothertradermod";
    public override string Name { get; init; } = "YetAnotherTraderMod";
    public override string Author { get; init; } = "AMightyTank | Based on PrisciluOrigins by Reis/Anigx";
    public override List<string>? Contributors { get; init; } = ["Reis", "Anigx"];
    public override SemanticVersioning.Version Version { get; init; } = new("0.0.1");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.11");
    public override List<string>? Incompatibilities { get; init; } = [];
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = null;
    public override string? Url { get; init; } = null;
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class TonyMod(
    ModHelper modHelper,
    ImageRouter imageRouter,
    ConfigServer configServer,
    DatabaseServer databaseServer,
    AddCustomTraderHelper addCustomTraderHelper,
    TraderUnlockService traderUnlockService)
    : IOnLoad
{
    private readonly TraderConfig _traderConfig = configServer.GetConfig<TraderConfig>();
    private readonly RagfairConfig _ragfairConfig = configServer.GetConfig<RagfairConfig>();

    public Task OnLoad()
    {
        var pathToMod = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

        TonyLogger.Init(pathToMod);
        TonyLogger.Log("Mod OnLoad started.");

        var traderBase = modHelper.GetJsonDataFromFile<TraderBase>(pathToMod, "data/base.json");
        var assort = modHelper.GetJsonDataFromFile<TraderAssort>(pathToMod, "data/assort.json");
        var traderImagePath = Path.Combine(pathToMod, "data/Tony.jpg");

        var config = new TonyConfig(pathToMod, databaseServer);
        config.LoadOrGenerate(traderBase, assort);

        TonyLogger.IsDebugEnabled = config.Settings.DebugLogging;
        if (TonyLogger.IsDebugEnabled)
        {
            TonyLogger.LogDebug("Debug Mode Enabled. Config Loaded.");
            TonyLogger.LogDebug($"  MinLevel: {config.Settings.MinLevel}");
            TonyLogger.LogDebug($"  UnlockedByDefault: {config.Settings.UnlockedByDefault}");
            TonyLogger.LogDebug($"  UnlimitedStock: {config.Settings.UnlimitedStock}");
            TonyLogger.LogDebug($"  RandomizeStock: {config.Settings.RandomizeStockAvailable} (Chance: {config.Settings.OutOfStockChance}%)");
            TonyLogger.LogDebug($"  PriceMultiplier: {config.Settings.PriceMultiplier}");
            TonyLogger.LogDebug($"  ForceCashOnly: {config.Settings.ForceCashOnly}");
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
                        TonyLogger.LogDebug($"[Insurance] Set Level {level.MinLevel} Coef to: {val}");
                    }
                    else
                    {
                        TonyLogger.LogDebug("[Insurance] Warning: InsurancePriceCoefficient property not found on LoyaltyLevel.");
                    }
                }
                catch (Exception ex)
                {
                    TonyLogger.Log($"[Insurance] Error setting coef for level: {ex.Message}");
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
            TraderUnlockService.EnableLevelLock = true;
            TraderUnlockService.MinLevelRequired = config.Settings.MinLevel;
            traderUnlockService.OnLoad();
            TonyLogger.Log($"Level-based unlock enabled. Required level: {config.Settings.MinLevel}");
        }
        else
        {
            TraderUnlockService.EnableLevelLock = false;
            TraderUnlockService.ForceUnlock = true;
            TonyLogger.Log("Trader unlocked by default (ForceUnlock active).");
        }

        if (string.IsNullOrEmpty(traderBase.Id))
        {
            TonyLogger.Log("CRITICAL ERROR: traderBase.Id is null or empty! Hardcoding ID to ensure stability.");
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
            TonyLogger.LogDebug("Starting Stock Manipulation...");
            var itemsToRemove = new List<string>();
            var itemsToRemoveNames = new List<string>();
            var random = new Random();
            int modifiedCount = 0;

            var locales = databaseServer.GetTables().Locales.Global["en"];

            foreach (var item in assort.Items)
            {
                if (item.ParentId != "hideout")
                {
                    continue;
                }

                if (config.Settings.RandomizeStockAvailable)
                {
                    if (random.Next(0, 100) < config.Settings.OutOfStockChance)
                    {
                        itemsToRemove.Add(item.Id);

                        string itemName = item.Id;
                        var tpl = TonyConfig.GetTemplateId(item);
                        if (!string.IsNullOrEmpty(tpl) && locales.Value != null && locales.Value.TryGetValue($"{tpl} Name", out var nameVal))
                        {
                            itemName = nameVal?.ToString() ?? item.Id;
                        }

                        itemsToRemoveNames.Add($"{itemName} ({item.Id})");
                        TonyLogger.LogDebug($"[Random Stock] removing: {itemName} ({item.Id})");
                        continue;
                    }
                }

                if (item.Upd != null)
                {
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
            }

            TonyLogger.LogDebug($"Total items modified for Stock setting: {modifiedCount}");

            if (itemsToRemove.Count > 0)
            {
                assort.Items.RemoveAll(x => itemsToRemove.Contains(x.Id) || itemsToRemove.Contains(x.ParentId));

                foreach (var id in itemsToRemove)
                {
                    assort.BarterScheme.Remove(id);
                    assort.LoyalLevelItems.Remove(id);
                }

                TonyLogger.Log($"[Stock] Removed {itemsToRemove.Count} offers due to randomization.");
                TonyLogger.LogDebug($"Removed Items:\n  {string.Join("\n  ", itemsToRemoveNames)}");
            }
            else
            {
                TonyLogger.LogDebug("No items removed by randomization this turn.");
            }
        }

        // Price multiplier now only affects money components, not barter item counts.
        if (Math.Abs(config.Settings.PriceMultiplier - 1.0) > 0.001)
        {
            TonyLogger.LogDebug($"Applying Price Multiplier {config.Settings.PriceMultiplier}...");
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
                        if (component.Count.HasValue && TonyConfig.IsCurrencyTemplate(component.Template.ToString()))
                        {
                            var oldPrice = component.Count.Value;
                            component.Count = (double)Math.Round(component.Count.Value * config.Settings.PriceMultiplier);

                            string itemName = itemId;
                            if (itemMap.TryGetValue(itemId, out var item))
                            {
                                var tpl = TonyConfig.GetTemplateId(item);
                                if (!string.IsNullOrEmpty(tpl) && localesForPrice.Value != null && localesForPrice.Value.TryGetValue($"{tpl} Name", out var nameVal))
                                {
                                    itemName = nameVal?.ToString() ?? itemId;
                                }
                            }

                            TonyLogger.LogDebug($"  Price adjust: {oldPrice} -> {component.Count} | {itemName} ({itemId})");
                            changedCount++;
                        }
                    }
                }
            }

            TonyLogger.Log($"[Pricing] Applied Global Price Multiplier: {config.Settings.PriceMultiplier} to {changedCount} money components.");
        }

        var timerRandom = new Random();
        int restockTime = timerRandom.Next(config.Settings.TraderRefreshMin, config.Settings.TraderRefreshMax);

        TonyLogger.Log($"Setting trader restock timer to {restockTime} seconds.");
        addCustomTraderHelper.SetTraderUpdateTime(
            _traderConfig,
            traderBase,
            restockTime,
            restockTime);

        traderBase.NextResupply = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + restockTime);

        addCustomTraderHelper.AddTraderToDb(traderBase, assort);

        if (config.Settings.DebugLogging)
        {
            TonyLogger.Log("Trader initialized. Debug Enabled.");
        }

        var localeFirstName = traderBase.Nickname ?? traderBase.Name ?? "Tony";
        var localeDescription = "An ex-BEAR operator and former enforcer for Russian organized crime. After Tarkov collapsed, Volkov turned old connections into a quiet business, supplying weapons, armor, and contraband to smugglers, mercenaries, and criminals. He respects usefulness, hates weakness, and only opens doors for those who earn his trust.";
        addCustomTraderHelper.AddTraderToLocales(traderBase, localeFirstName, localeDescription);

        return Task.CompletedTask;
    }

    private static void ApplyConfiguredPayments(TraderAssort assort, TonyConfig config)
    {
        var rootItems = assort.Items
            .Where(x => x.ParentId == "hideout")
            .ToList();

        foreach (var priceConfig in config.Prices)
        {
            var matchingOffers = rootItems
                .Where(item => DoesConfigMatchOffer(item, priceConfig))
                .ToList();

            if (matchingOffers.Count == 0)
            {
                TonyLogger.LogDebug($"[Pricing] No matching offer for {priceConfig.ItemName} / {priceConfig.TplId}");
                continue;
            }

            if (matchingOffers.Count > 1 && string.IsNullOrWhiteSpace(priceConfig.OfferId))
            {
                TonyLogger.LogDebug($"[Pricing] Multiple offers matched TplId {priceConfig.TplId}. Add OfferId to items.json for exact control.");
            }

            foreach (var offer in matchingOffers)
            {
                ApplyPaymentToOffer(assort, offer.Id, priceConfig, config.Settings.ForceCashOnly);
            }
        }
    }

    private static bool DoesConfigMatchOffer(object item, PriceConfigItem priceConfig)
    {
        var itemId = GetMemberValue(item, "Id")?.ToString();

        if (!string.IsNullOrWhiteSpace(priceConfig.OfferId))
        {
            return itemId == priceConfig.OfferId;
        }

        var tpl = TonyConfig.GetTemplateId(item);
        return !string.IsNullOrEmpty(tpl) && tpl == priceConfig.TplId;
    }

    private static void ApplyPaymentToOffer(
        TraderAssort assort,
        string offerId,
        PriceConfigItem priceConfig,
        bool forceCashOnly)
    {
        if (!assort.BarterScheme.TryGetValue(offerId, out var existingSchemeList))
        {
            TonyLogger.LogDebug($"[Pricing] Offer {offerId} has no barter_scheme entry.");
            return;
        }

        var shouldUseCash = forceCashOnly
            || priceConfig.CashOnly
            || priceConfig.BarterScheme == null
            || priceConfig.BarterScheme.Count == 0;

        if (shouldUseCash)
        {
            var currencyTpl = TonyConfig.CurrencyToTemplate(priceConfig.Currency);

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

            TonyLogger.LogDebug($"[Pricing] Cash override: {priceConfig.ItemName} = {priceConfig.Price} {priceConfig.Currency}");
            return;
        }

        ReplaceOfferPaymentScheme(existingSchemeList, priceConfig.BarterScheme);
        TonyLogger.LogDebug($"[Pricing] Barter override: {priceConfig.ItemName}");
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

        return field?.GetValue(target);
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
