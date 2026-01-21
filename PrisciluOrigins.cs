using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using System.Reflection;
using System.Linq;
using Path = System.IO.Path;

namespace PrisciluOrigins;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.priscilu.origins";
    public override string Name { get; init; } = "Priscilu_Origins_v2";
    public override string Author { get; init; } = "Reis | Update/Contributor: Anigx";
    public override List<string>? Contributors { get; init; } = ["Anigx"];
    public override SemanticVersioning.Version Version { get; init; } = new("6.2.9");
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

        // [NEW] Apply Settings (Level & Unlock)
        traderBase.UnlockedByDefault = config.Settings.UnlockedByDefault;
        
        if (traderBase.LoyaltyLevels.Count > 0)
        {
            traderBase.LoyaltyLevels[0].MinLevel = config.Settings.MinLevel;
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
            PrisciluLogger.Log("Trader unlocked by default.");
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

        // [TIMER] Hardcoded to 1 hour (3600s) as dynamic config caused issues
        const int HardcodedRestockSeconds = 3600;
        
        PrisciluLogger.Log($"Setting trader restock timer to {HardcodedRestockSeconds} seconds.");
        addCustomTraderHelper.SetTraderUpdateTime(
            _traderConfig,
            traderBase,
            HardcodedRestockSeconds,
            HardcodedRestockSeconds);

        // Ensure NextResupply is set to something valid
        traderBase.NextResupply = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + HardcodedRestockSeconds);

        _ragfairConfig.Traders.TryAdd(traderBase.Id, true);
        addCustomTraderHelper.AddTraderToDb(traderBase, assort);
        
        // Log verification
        PrisciluLogger.Log($"Trader initialized with NextResupply: {traderBase.NextResupply}");

        var localeFirstName = traderBase.Nickname ?? traderBase.Name ?? "Priscilu";
        var localeDescription = string.Empty;
        addCustomTraderHelper.AddTraderToLocales(traderBase, localeFirstName, localeDescription);

        return Task.CompletedTask;
    }
}
