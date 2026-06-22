using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using WTTServerCommonLib;
using YetAnotherTraderMod.src.Services.ItemHelpers;
using Path = System.IO.Path;

namespace YetAnotherTraderMod.src;

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 2)]
public sealed class YATMWTTLoader(
    WTTServerCommonLib.WTTServerCommonLib wttCommon,
    YATMSlotCopyBootstrap yatmSlotCopyBootstrap) : IOnLoad
{
    public async Task OnLoad()
    {
        YATMLogger.Log("[CustomContentLoader] Starting custom content load...");

        var assembly = Assembly.GetExecutingAssembly();

        var modPath = Path.GetDirectoryName(assembly.Location)
            ?? throw new InvalidOperationException("Could not resolve mod path.");

        var dbPath = Path.Combine(modPath, "db");

        YATMLogger.LogDebug($"[CustomContentLoader] Mod path: {modPath}");
        YATMLogger.LogDebug($"[CustomContentLoader] DB path: {dbPath}");

        if (!Directory.Exists(dbPath))
        {
            YATMLogger.Log($"[CustomContentLoader] DB folder not found: {dbPath}");
            return;
        }

        var itemPaths = new[]
        {
            Path.Join("db", "CustomItems"),
            Path.Join("db", "CustomWeapons"),
        };

        var slotCopyPaths = new[]
        {
            Path.Join("db", "CustomWeapons")
        };

        var presetPath = Path.Join("db", "CustomWeaponPresets");

        // 1. WTT creates custom ammo/parts/weapons
        YATMLogger.LogDebug("[CustomContentLoader] Loading WTT custom items...");

        foreach (var path in itemPaths)
        {
            await wttCommon.CustomItemServiceExtended.CreateCustomItems(assembly, path);
        }

        // 2. YATM slot clone helper copies missing slots onto custom weapons
        YATMLogger.LogDebug("[CustomContentLoader] Processing YATM slot copies...");

        foreach (var path in slotCopyPaths)
        {
            await yatmSlotCopyBootstrap.ProcessSlotCopies(assembly, path);
        }

        // 3. WTT creates weapon presets
        YATMLogger.LogDebug("[CustomContentLoader] Loading custom weapon presets...");

        await wttCommon.CustomWeaponPresetService.CreateCustomWeaponPresets(assembly, presetPath);

        // 4. WTT loads locales, loot spawns, quests, and quest zones
        YATMLogger.LogDebug("[CustomContentLoader] Loading locales, loot spawns, quests, and quest zones...");

        await wttCommon.CustomLocaleService.CreateCustomLocales(assembly);
        await wttCommon.CustomLootspawnService.CreateCustomLootSpawns(assembly);
        await wttCommon.CustomQuestService.CreateCustomQuests(assembly);
        await wttCommon.CustomQuestZoneService.CreateCustomQuestZones(assembly);

        YATMLogger.Log("[CustomContentLoader] Finished loading all custom content.");
    }
}