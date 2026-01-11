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
    public override string ModGuid { get; init; } = "com.priscilu.origins";
    public override string Name { get; init; } = "Priscilu_Origins";
    public override string Author { get; init; } = "Reis | Update: CyberByteCraft";
    public override List<string>? Contributors { get; init; } = ["CyberByteCraft"];
    public override SemanticVersioning.Version Version { get; init; } = new("6.1.1");
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
    TimeUtil timeUtil,
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

        var avatarRoute = traderBase.Avatar ?? string.Empty;
        avatarRoute = avatarRoute.Replace(".png", "").Replace(".jpg", "").Replace(".jpeg", "");
        imageRouter.AddRoute(avatarRoute, traderImagePath);

        addCustomTraderHelper.SetTraderUpdateTime(
            _traderConfig,
            traderBase,
            timeUtil.GetHoursAsSeconds(1),
            timeUtil.GetHoursAsSeconds(2));

        _ragfairConfig.Traders.TryAdd(traderBase.Id, true);
        addCustomTraderHelper.AddTraderWithEmptyAssortToDb(traderBase);

        var localeFirstName = traderBase.Nickname ?? traderBase.Name ?? "Priscilu";
        var localeDescription = string.Empty;
        addCustomTraderHelper.AddTraderToLocales(traderBase, localeFirstName, localeDescription);

        addCustomTraderHelper.OverwriteTraderAssort(traderBase.Id, assort);

        addCustomTraderHelper.LogInfo("[Priscilu_Origins] Contributors: CyberByteCraft");
        return Task.CompletedTask;
    }
}
