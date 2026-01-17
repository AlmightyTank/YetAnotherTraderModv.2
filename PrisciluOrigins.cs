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
using Path = System.IO.Path;

namespace PrisciluOrigins;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.anigx.priscilu_origins_v2";
    public override string Name { get; init; } = "Priscilu_Origins_v2";
    public override string Author { get; init; } = "Reis | Update/Contributor: Anigx";
    public override List<string>? Contributors { get; init; } = ["Anigx"];
    public override SemanticVersioning.Version Version { get; init; } = new("6.1.3");
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
    ConfigServer configServer, // [FIX] Revert to ConfigServer
    DatabaseServer databaseServer,
    AddCustomTraderHelper addCustomTraderHelper)
    : IOnLoad
{
    private readonly TraderConfig _traderConfig = configServer.GetConfig<TraderConfig>();
    private readonly RagfairConfig _ragfairConfig = configServer.GetConfig<RagfairConfig>();

    public Task OnLoad()
    {
        var pathToMod = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

        var traderBase = modHelper.GetJsonDataFromFile<TraderBase>(pathToMod, "Data/base.json");
        var assort = modHelper.GetJsonDataFromFile<TraderAssort>(pathToMod, "Data/assort.json");
        var traderImagePath = Path.Combine(pathToMod, "Data/Priscilu_Origins.jpg");

        // [NEW] Load Configuration
        var config = new PrisciluOrigins.Config.PrisciluConfig(pathToMod, databaseServer);
        config.LoadOrGenerate(traderBase, assort);

        // [NEW] Apply Settings (Level & Unlock)
        traderBase.UnlockedByDefault = config.Settings.UnlockedByDefault;
        if (traderBase.LoyaltyLevels.Count > 0)
        {
            traderBase.LoyaltyLevels[0].MinLevel = config.Settings.MinLevel;
        }

        // [NEW] Apply Price Overrides
        foreach (var priceConfig in config.Prices)
        {
            // Find item by TPL
            foreach (var item in assort.Items)
            {
               var tpl = PrisciluOrigins.Config.PrisciluConfig.GetTemplateId(item);
               if (!string.IsNullOrEmpty(tpl) && tpl == priceConfig.TplId && item.ParentId == "hideout")
               {
                   // Found the item, update its price in BarterScheme
                   if (assort.BarterScheme.ContainsKey(item.Id))
                   {
                        var scheme = assort.BarterScheme[item.Id][0][0];
                        scheme.Count = priceConfig.Price;
                        // Map config currency string to ID if needed, simplified for now assuming RUB mostly
                        // (You could expand this to switch currency completely if requested)
                   }
               }
            }
        }

        var avatarRoute = traderBase.Avatar ?? string.Empty;
        avatarRoute = avatarRoute.Replace(".png", "").Replace(".jpg", "").Replace(".jpeg", "");
        imageRouter.AddRoute(avatarRoute, traderImagePath);

        // [NEW] Use Configured Timer
        addCustomTraderHelper.SetTraderUpdateTime(
            _traderConfig,
            traderBase,
            config.Settings.RestockTimerSeconds,
            config.Settings.RestockTimerSeconds);

        _ragfairConfig.Traders.TryAdd(traderBase.Id, true);
        addCustomTraderHelper.AddTraderToDb(traderBase, assort);

        var localeFirstName = traderBase.Nickname ?? traderBase.Name ?? "Priscilu";
        var localeDescription = string.Empty;
        addCustomTraderHelper.AddTraderToLocales(traderBase, localeFirstName, localeDescription);

        return Task.CompletedTask;
    }
}
