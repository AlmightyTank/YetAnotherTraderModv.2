using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using WTTServerCommonLib;
using Path = System.IO.Path;

namespace Tony.Loaders;

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 3)]
public class TonyQuestLoader(WTTServerCommonLib.WTTServerCommonLib wttCommon) : IOnLoad
{
    public async Task OnLoad()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        await wttCommon.CustomQuestService.CreateCustomQuests(
            assembly,
            Path.Join("data", "customquests")
        );
    }
}