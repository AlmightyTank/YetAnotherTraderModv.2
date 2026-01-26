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
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reflection;
using System.Linq;
using System.Collections; // Added for IDictionary support
using Path = System.IO.Path;

namespace PrisciluOrigins;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.priscilu.origins";
    public override string Name { get; init; } = "Priscilu_Origins_v2";
    public override string Author { get; init; } = "Reis | Update/Contributor: Anigx";
    public override List<string>? Contributors { get; init; } = ["Anigx"];
    public override SemanticVersioning.Version Version { get; init; } = new("6.3.1");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.11");
    public override List<string>? Incompatibilities { get; init; } = [];
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = null;
    public override string? Url { get; init; } = null;
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class PrisciluOriginsMod(
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
        
        // [DEBUG LOG] Initialize Logger
        PrisciluLogger.Init(pathToMod);
        PrisciluLogger.Log("Mod OnLoad started.");

        var traderBase = modHelper.GetJsonDataFromFile<TraderBase>(pathToMod, "data/base.json");
        var assort = modHelper.GetJsonDataFromFile<TraderAssort>(pathToMod, "data/assort.json");
        var traderImagePath = Path.Combine(pathToMod, "data/Priscilu_Origins.jpg");

        // [NEW] Load Configuration
        var config = new PrisciluOrigins.Config.PrisciluConfig(pathToMod, databaseServer);
        config.LoadOrGenerate(traderBase, assort);

        // [LOG] Set Debug Flag
        PrisciluLogger.IsDebugEnabled = config.Settings.DebugLogging;
        if (PrisciluLogger.IsDebugEnabled)
        {
             PrisciluLogger.LogDebug($"Debug Mode Enabled. Config Loaded.");
             PrisciluLogger.LogDebug($"  MinLevel: {config.Settings.MinLevel}");
             PrisciluLogger.LogDebug($"  UnlockedByDefault: {config.Settings.UnlockedByDefault}");
             PrisciluLogger.LogDebug($"  UnlimitedStock: {config.Settings.UnlimitedStock}");
             PrisciluLogger.LogDebug($"  RandomizeStock: {config.Settings.RandomizeStockAvailable} (Chance: {config.Settings.OutOfStockChance}%)");
             PrisciluLogger.LogDebug($"  PriceMultiplier: {config.Settings.PriceMultiplier}");
        }

        // [NEW] Apply Settings (Level & Unlock)
        traderBase.UnlockedByDefault = config.Settings.UnlockedByDefault;
        
        if (traderBase.LoyaltyLevels.Count > 0)
        {
            traderBase.LoyaltyLevels[0].MinLevel = config.Settings.MinLevel;
        }

        // [NEW] Apply Configurable Services
        // [FIX] Apply Insurance Coefficient to Loyalty Levels (Correct Place!)
        if (traderBase.LoyaltyLevels != null)
        {
            foreach (var level in traderBase.LoyaltyLevels)
            {
                try 
                {
                    // InsurancePriceCoefficient might be int? or double?
                    // We use reflection to set it safely
                    var prop = level.GetType().GetProperty("InsurancePriceCoefficient");
                    if (prop != null && prop.CanWrite)
                    {
                        var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                        object val = Convert.ChangeType(config.Settings.InsurancePriceCoef, targetType);
                        prop.SetValue(level, val);
                        PrisciluLogger.LogDebug($"[Insurance] Set Level {level.MinLevel} Coef to: {val}");
                    }
                    else
                    {
                         // Fallback to ExtensionData if property missing?
                         // But Inspector confirmed property exists.
                         PrisciluLogger.LogDebug($"[Insurance] Warning: InsurancePriceCoefficient property not found on LoyaltyLevel.");
                    }
                }
                catch (Exception ex)
                {
                    PrisciluLogger.Log($"[Insurance] Error settingcoef for level: {ex.Message}");
                }
            }
        }
        
        // [OLD] Global Insurance setting (likely ineffective but kept for safety/legacy if core uses it)
        if (traderBase.Insurance != null)
        {
             // Try valid ExtensionData injection just in case
             if (traderBase.Insurance.ExtensionData == null) traderBase.Insurance.ExtensionData = new Dictionary<string, object>();
             traderBase.Insurance.ExtensionData["insurance_price_coef"] = config.Settings.InsurancePriceCoef;
        }
        
        if (traderBase.Repair != null)
        {
            // Repair.Quality is double?, so we assign it directly without ToString()
            traderBase.Repair.Quality = config.Settings.RepairQuality;
        }

        if (!config.Settings.UnlockedByDefault)
        {
            TraderUnlockService.EnableLevelLock = true;
            TraderUnlockService.MinLevelRequired = config.Settings.MinLevel;
            traderUnlockService.OnLoad();
            PrisciluLogger.Log($"Level-based unlock enabled. Required level: {config.Settings.MinLevel}");
        }
        else
        {
            TraderUnlockService.EnableLevelLock = false;
            TraderUnlockService.ForceUnlock = true; // [FIX] Force unlock for existing profiles
            PrisciluLogger.Log("Trader unlocked by default (ForceUnlock active).");
        }

        // [FIX] Ensure ID consistency
        if (string.IsNullOrEmpty(traderBase.Id))
        {
             PrisciluLogger.Log("CRITICAL ERROR: traderBase.Id is null or empty! Hardcoding ID to ensure stability.");
             traderBase.Id = "6748adca5c70634464b214a8"; 
        }

        // Ensure non-null collections
        if (traderBase.ItemsBuy == null) traderBase.ItemsBuy = new() { Category = [], IdList = [] };
        if (traderBase.ItemsBuyProhibited == null) traderBase.ItemsBuyProhibited = new() { Category = [], IdList = [] };
        if (traderBase.ItemsSell == null) traderBase.ItemsSell = [];

        // Apply Price Overrides
        foreach (var priceConfig in config.Prices)
        {
            foreach (var item in assort.Items)
            {
               var tpl = PrisciluOrigins.Config.PrisciluConfig.GetTemplateId(item);
               if (!string.IsNullOrEmpty(tpl) && tpl == priceConfig.TplId && item.ParentId == "hideout")
               {
                   if (assort.BarterScheme.ContainsKey(item.Id))
                   {
                        var scheme = assort.BarterScheme[item.Id][0][0];
                        scheme.Count = priceConfig.Price;

                        // [NEW] Apply Currency
                        var currencyTpl = priceConfig.Currency switch
                        {
                            "USD" => "5696686a4bdc2da3298b456a",
                            "EUR" => "569668774bdc2da2298b4568",
                            "RUB" => "5449016a4bdc2d6f028b456f",
                            _ => null // Keep original if unknown/OTHER
                        };

                        if (currencyTpl != null)
                        {
                            scheme.Template = currencyTpl;
                        }
                   }
               }
            }
        }

        var avatarRoute = traderBase.Avatar ?? string.Empty;
        avatarRoute = avatarRoute.Replace(".png", "").Replace(".jpg", "").Replace(".jpeg", "");
        imageRouter.AddRoute(avatarRoute, traderImagePath);

        // [LOGIC] Flea Market Visibility
        if (config.Settings.AddTraderToFleaMarket)
        {
            _ragfairConfig.Traders.TryAdd(traderBase.Id, true);
        }
        else
        {
             _ragfairConfig.Traders.Remove(traderBase.Id);
        }

        // [LOGIC] Stock Manipulation (Randomization & Unlimited)
        if (config.Settings.RandomizeStockAvailable || config.Settings.UnlimitedStock)
        {
            PrisciluLogger.LogDebug("Starting Stock Manipulation...");
            var itemsToRemove = new List<string>();
            var itemsToRemoveNames = new List<string>(); // [NEW] Track names
            var random = new Random();
            int modifiedCount = 0;
            
            // [NEW] Get Locales for Name Resolution
            var locales = databaseServer.GetTables().Locales.Global["en"];

            foreach (var item in assort.Items)
            {
                 if (item.ParentId == "hideout")
                 {
                     // Randomize Availability
                     if (config.Settings.RandomizeStockAvailable)
                     {
                         if (random.Next(0, 100) < config.Settings.OutOfStockChance)
                         {
                             // Mark for removal
                             itemsToRemove.Add(item.Id);
                             
                             // [NEW] Resolve Name
                             string itemName = item.Id;
                             var tpl = PrisciluOrigins.Config.PrisciluConfig.GetTemplateId(item);
                             if (!string.IsNullOrEmpty(tpl) && locales.Value != null && locales.Value.TryGetValue($"{tpl} Name", out var nameVal))
                             {
                                 itemName = nameVal.ToString();
                             }
                             itemsToRemoveNames.Add($"{itemName} ({item.Id})");

                             PrisciluLogger.LogDebug($"[Random Stock] removing: {itemName} ({item.Id})");
                             continue;
                         }
                     }

                     // Unlimited Stock Override
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
            }

            PrisciluLogger.LogDebug($"Total items modified for Stock setting: {modifiedCount}");

            // Perform Removals
            if (itemsToRemove.Count > 0)
            {
                assort.Items.RemoveAll(x => itemsToRemove.Contains(x.Id) || itemsToRemove.Contains(x.ParentId)); 
                foreach (var id in itemsToRemove)
                {
                    assort.BarterScheme.Remove(id);
                    assort.LoyalLevelItems.Remove(id);
                }
                PrisciluLogger.Log($"[Stock] Removed {itemsToRemove.Count} offers due to randomization.");
                PrisciluLogger.LogDebug($"Removed Items:\n  {string.Join("\n  ", itemsToRemoveNames)}");
            }
            else 
            {
                 PrisciluLogger.LogDebug("No items removed by randomization this turn.");
            }
        }

        // [LOGIC] Price Multiplier
        if (Math.Abs(config.Settings.PriceMultiplier - 1.0) > 0.001)
        {
             PrisciluLogger.LogDebug($"Applying Price Multiplier {config.Settings.PriceMultiplier}...");
             int changedCount = 0;
             
             // [NEW] Dictionary for quick item lookup to resolve names
             var itemMap = assort.Items.ToDictionary(x => x.Id, x => x);
             // Ensure locales are fetched (might be fetched above, but fetch here to be safe/local)
             var localesForPrice = databaseServer.GetTables().Locales.Global["en"];

             foreach (var itemSchemePair in assort.BarterScheme)
             {
                 var itemId = itemSchemePair.Key;
                 var schemeList = itemSchemePair.Value;

                 foreach (var schemeSubList in schemeList)
                 {
                     foreach (var component in schemeSubList)
                     {
                         if (component.Count.HasValue)
                         {
                             var oldPrice = component.Count.Value;
                             component.Count = (double)Math.Round(component.Count.Value * config.Settings.PriceMultiplier);
                             
                             // [NEW] Resolve Name for logging
                             string itemName = itemId;
                             if (itemMap.TryGetValue(itemId, out var item))
                             {
                                 var tpl = PrisciluOrigins.Config.PrisciluConfig.GetTemplateId(item);
                                 if (!string.IsNullOrEmpty(tpl) && localesForPrice.Value != null && localesForPrice.Value.TryGetValue($"{tpl} Name", out var nameVal))
                                 {
                                     itemName = nameVal.ToString();
                                 }
                             }

                             PrisciluLogger.LogDebug($"  Price adjust: {oldPrice} -> {component.Count} | {itemName} ({itemId})");
                             changedCount++;
                         }
                     }
                 }
             }
             PrisciluLogger.Log($"[Pricing] Applied Global Price Multiplier: {config.Settings.PriceMultiplier} to {changedCount} items.");
        }

        // [TIMER] Configurable Refresh Time
        var timerRandom = new Random();
        int restockTime = timerRandom.Next(config.Settings.TraderRefreshMin, config.Settings.TraderRefreshMax);
        
        PrisciluLogger.Log($"Setting trader restock timer to {restockTime} seconds.");
        addCustomTraderHelper.SetTraderUpdateTime(
            _traderConfig,
            traderBase,
            restockTime,
            restockTime);

        traderBase.NextResupply = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + restockTime);

        addCustomTraderHelper.AddTraderToDb(traderBase, assort);
        
        if (config.Settings.DebugLogging)
        {
             PrisciluLogger.Log($"Trader initialized. Debug Enabled.");
        }

        var localeFirstName = traderBase.Nickname ?? traderBase.Name ?? "Priscilu";
        var localeDescription = string.Empty;
        addCustomTraderHelper.AddTraderToLocales(traderBase, localeFirstName, localeDescription);

        return Task.CompletedTask;
    }
}
