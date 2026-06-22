using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Servers;
using YetAnotherTraderMod.src;
using Path = System.IO.Path;

namespace YetAnotherTraderMod.config;

public class YATMConfig
{
    private readonly string _modPath;
    private readonly string _configDir;
    private readonly string _settingsPath;
    private readonly string _pricesPath;

    private readonly DatabaseServer _databaseServer;

    private sealed class AmmoPackMetadata
    {
        public AmmoPackMetadata(string tplId, string itemName, int size)
        {
            TplId = tplId;
            ItemName = itemName;
            Size = size;
        }

        public string TplId { get; }
        public string ItemName { get; }
        public int Size { get; }
    }

    // Loose ammo tpl -> matching ammo pack tpl.
    // Used when config/items.json is generated or upgraded by the mod itself.
    private static readonly Dictionary<string, AmmoPackMetadata> AmmoPacksByAmmoTpl = new()
    {
        ["5f0596629e22f464da6bbdd9"] = new("657023f81419851aef03e6f1", ".366 TKM AP-M ammo pack (20 pcs)", 20),
        ["59e655cb86f77411dc52a77b"] = new("657024011419851aef03e6f4", ".366 TKM EKO ammo pack (20 pcs)", 20),
        ["560d5e524bdc2d25448b4571"] = new("657024361419851aef03e6fa", "12/70 7mm buckshot ammo pack (25 pcs)", 25),
        ["5d6e68a8a4b9360b6c0d54e2"] = new("64898838d5b4df6140000a20", "12/70 AP-20 ammo pack (25 pcs)", 25),
        ["5d6e6911a4b9361bd5780d52"] = new("65702474bfc87b3a34093226", "12/70 flechette ammo pack (25 pcs)", 25),
        ["56dfef82d2720bbd668b4567"] = new("57372ac324597767001bc261", "5.45x39mm BP gs ammo pack (30 pcs)", 30),
        ["56dff026d2720bb8668b4567"] = new("57372bd3245977670b7cd243", "5.45x39mm BS gs ammo pack (30 pcs)", 30),
        ["56dff061d2720bb5668b4567"] = new("57372c89245977685d4159b1", "5.45x39mm BT gs ammo pack (30 pcs)", 30),
        ["56dff2ced2720bb4668b4567"] = new("57372db0245977685d4159b2", "5.45x39mm PP gs ammo pack (30 pcs)", 30),
        ["5c0d5e4486f77478390952fe"] = new("5c1262a286f7743f8a69aab2", "5.45x39mm PPBS gs Igolnik ammo pack (30 pcs)", 30),
        ["56dff3afd2720bba668b4567"] = new("57372ebf2459776862260582", "5.45x39mm PS gs ammo pack (30 pcs)", 30),
        ["59e0d99486f7744a32234762"] = new("64acea16c4eda9354b0226b0", "7.62x39mm BP gzh ammo pack (20 pcs)", 20),
        ["601aa3d2b2bcb34913271e6d"] = new("6489851fc827d4637f01791b", "7.62x39mm MAI AP ammo pack (20 pcs)", 20),
        ["64b7af434b75259c590fa893"] = new("64ace9f9c4eda9354b0226aa", "7.62x39mm PP gzh ammo pack (20 pcs)", 20),
        ["5656d7c34bdc2d9d198b4587"] = new("5649ed104bdc2d3d1c8b458b", "7.62x39mm PS gzh ammo pack (20 pcs)", 20),
        ["57372140245977611f70ee91"] = new("573728cc24597765cc785b5d", "9x18mm PM SP7 gzh ammo pack (16 pcs)", 16),
        ["5c925fa22e221601da359b7b"] = new("65702591c5d7d4cb4d07857c", "9x19mm AP 6.3 ammo pack (50 pcs)", 50),
        ["5efb0da7a29a85116f6ea05f"] = new("648987d673c462723909a151", "9x19mm PBP ammo pack (50 pcs)", 50),
        ["56d59d3ad2720bdb418b4577"] = new("5739d41224597779c3645501", "9x19mm Pst gzh ammo pack (16 pcs)", 16),
        ["5c0d56a986f774449d5de529"] = new("5c1127bdd174af44217ab8b9", "9x19mm RIP ammo pack (20 pcs)", 20),
        ["5c0d688c86f77413ae3407b2"] = new("6489854673c462723909a14e", "9x39mm BP ammo pack (20 pcs)", 20),
        ["61962d879bb3d20b0946d385"] = new("657025cfbfc87b3a34093253", "9x39mm PAB-9 gs ammo pack (20 pcs)", 20),
        ["57a0dfb82459774d3078b56c"] = new("657025d4c5d7d4cb4d078585", "9x39mm SP-5 gs ammo pack (20 pcs)", 20),
        ["57a0e5022459774d1673f889"] = new("657025dabfc87b3a34093256", "9x39mm SP-6 gs ammo pack (20 pcs)", 20),
        ["5c0d668f86f7747ccb7f13b2"] = new("657025dfcfc010a0f5006a3b", "9x39mm SPP gs ammo pack (20 pcs)", 20),
    };

    public SettingsConfig Settings { get; private set; } = new();
    public List<PriceConfigItem> Prices { get; private set; } = new();

    public YATMConfig(string modPath, DatabaseServer databaseServer)
    {
        _modPath = modPath;
        _databaseServer = databaseServer;
        _configDir = Path.Combine(_modPath, "config");
        _settingsPath = Path.Combine(_configDir, "settings.json");
        _pricesPath = Path.Combine(_configDir, "items.json");
    }

    // Helper to find Template ID robustly.
    public static string? GetTemplateId(object item)
    {
        try
        {
            var type = item.GetType();

            // Try properties.
            var props = new[] { "Template", "Tpl", "_tpl", "TemplateId" };
            foreach (var propName in props)
            {
                var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop != null)
                {
                    return prop.GetValue(item)?.ToString();
                }
            }

            // Try fields.
            var fields = new[] { "Template", "Tpl", "_tpl", "TemplateId" };
            foreach (var fieldName in fields)
            {
                var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (field != null)
                {
                    return field.GetValue(item)?.ToString();
                }
            }

            return ((dynamic)item).Id;
        }
        catch
        {
            return null;
        }
    }

    public static string CurrencyToTemplate(string? currency)
    {
        return (currency ?? "RUB").ToUpperInvariant() switch
        {
            "USD" => "5696686a4bdc2da3298b456a",
            "EUR" => "569668774bdc2da2298b4568",
            "RUB" => "5449016a4bdc2d6f028b456f",
            _ => "5449016a4bdc2d6f028b456f"
        };
    }

    public static string TemplateToCurrency(string? tpl)
    {
        return tpl switch
        {
            "5696686a4bdc2da3298b456a" => "USD",
            "569668774bdc2da2298b4568" => "EUR",
            "5449016a4bdc2d6f028b456f" => "RUB",
            _ => "OTHER"
        };
    }

    public static bool IsCurrencyTemplate(string? tpl)
    {
        return tpl == "5449016a4bdc2d6f028b456f"
            || tpl == "5696686a4bdc2da3298b456a"
            || tpl == "569668774bdc2da2298b4568";
    }

    public void LoadOrGenerate(TraderBase baseJson, TraderAssort assortJson)
    {
        if (!Directory.Exists(_configDir))
        {
            Directory.CreateDirectory(_configDir);
        }

        LoadOrGenerateSettings(baseJson);
        LoadOrGeneratePrices(assortJson);
    }

    private void LoadOrGenerateSettings(TraderBase baseJson)
    {
        if (File.Exists(_settingsPath))
        {
            try
            {
                var json = File.ReadAllText(_settingsPath);
                var options = new JsonSerializerOptions
                {
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    PropertyNameCaseInsensitive = true
                };

                Settings = JsonSerializer.Deserialize<SettingsConfig>(json, options) ?? new SettingsConfig();
                NormalizeSettings();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Tony] Error loading settings.json: {ex.Message}");
                Settings = new SettingsConfig();
                NormalizeSettings();
            }
        }
        else
        {
            Settings = new SettingsConfig
            {
                MinLevel = baseJson.LoyaltyLevels?.FirstOrDefault()?.MinLevel ?? 1,
                UnlockedByDefault = baseJson.UnlockedByDefault ?? false,

                TraderRefreshMin = 1800,
                TraderRefreshMax = 3600,
                AddTraderToFleaMarket = true,
                InsurancePriceCoef = 25,
                RepairQuality = 0.8,

                RandomizeStockAvailable = true,
                OutOfStockChance = 15,
                UnlimitedStock = false,

                PriceMultiplier = 1.0,

                // false = allow custom barter recipes in items.json.
                // true = force every configured offer to use Price + Currency only.
                ForceCashOnly = false,

                // If true, configured offers are randomly split between cash and barter.
                // CashOfferPercent = 85 means roughly 85% cash and 15% barter.
                RandomizeCashBarterOffers = true,
                CashOfferPercent = 85,

                DebugLogging = false
            };

            NormalizeSettings();
            SaveJson(_settingsPath, Settings);
        }
    }

    private void NormalizeSettings()
    {
        // Keep the setting safe. 0 = all barter, 100 = all cash.
        Settings.CashOfferPercent = Math.Clamp(Settings.CashOfferPercent, 0, 100);
    }

    private void LoadOrGeneratePrices(TraderAssort assortJson)
    {
        if (File.Exists(_pricesPath))
        {
            try
            {
                var json = File.ReadAllText(_pricesPath);
                var options = new JsonSerializerOptions
                {
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    PropertyNameCaseInsensitive = true
                };

                Prices = JsonSerializer.Deserialize<List<PriceConfigItem>>(json, options) ?? new List<PriceConfigItem>();

                var upgradedAmmoPackRows = ApplyAmmoPackMetadata(Prices);
                if (upgradedAmmoPackRows > 0)
                {
                    SaveJson(_pricesPath, Prices);
                    YATMLogger.Log($"[Pricing] Added/updated ammo pack metadata on {upgradedAmmoPackRows} items.json rows.");
                }

                YATMLogger.LogDebug($"Loaded {Prices.Count} custom price entries from items.json");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Tony] Error loading items.json: {ex.Message}");
                YATMLogger.Log($"Error loading items.json: {ex.Message}");
            }
        }
        else
        {
            YATMLogger.LogDebug("items.json not found. Generating default prices from assort...");

            Prices = new List<PriceConfigItem>();
            var locales = _databaseServer.GetTables().Locales.Global["en"];
            int generatedCount = 0;

            foreach (var item in assortJson.Items)
            {
                if (item.ParentId != "hideout")
                {
                    continue;
                }

                if (!assortJson.BarterScheme.ContainsKey(item.Id))
                {
                    continue;
                }

                var schemeList = assortJson.BarterScheme[item.Id];
                if (schemeList == null || schemeList.Count == 0)
                {
                    continue;
                }

                var tpl = GetTemplateId(item);
                if (string.IsNullOrEmpty(tpl))
                {
                    continue;
                }

                var itemName = tpl;
                if (locales.Value != null && locales.Value.TryGetValue($"{tpl} Name", out var nameVal))
                {
                    itemName = nameVal?.ToString() ?? tpl;
                }

                var copiedBarterScheme = new List<List<PaymentConfigItem>>();

                foreach (var schemeOption in schemeList)
                {
                    var copiedOption = new List<PaymentConfigItem>();

                    foreach (var component in schemeOption)
                    {
                        var componentTpl = component.Template.ToString();
                        var componentName = componentTpl;

                        if (locales.Value != null && locales.Value.TryGetValue($"{componentTpl} Name", out var componentNameVal))
                        {
                            componentName = componentNameVal?.ToString() ?? componentTpl;
                        }

                        copiedOption.Add(new PaymentConfigItem
                        {
                            TplId = componentTpl,
                            ItemName = componentName,
                            Count = component.Count ?? 0
                        });
                    }

                    copiedBarterScheme.Add(copiedOption);
                }

                var firstPayment = copiedBarterScheme.FirstOrDefault()?.FirstOrDefault();
                var firstPaymentIsCash = firstPayment != null && IsCurrencyTemplate(firstPayment.TplId);

                var priceEntry = new PriceConfigItem
                {
                    OfferId = item.Id,
                    TplId = tpl,
                    ItemName = itemName,

                    Price = firstPaymentIsCash ? firstPayment!.Count : 0,
                    Currency = firstPaymentIsCash ? TemplateToCurrency(firstPayment!.TplId) : "RUB",

                    CashOnly = firstPaymentIsCash,
                    BarterScheme = copiedBarterScheme
                };

                ApplyAmmoPackMetadata(priceEntry);
                Prices.Add(priceEntry);

                generatedCount++;
            }

            Prices = Prices
                .OrderBy(x => x.ItemName)
                .ThenBy(x => x.OfferId)
                .ToList();

            SaveJson(_pricesPath, Prices);
            YATMLogger.Log($"Generated items.json with {generatedCount} entries.");
        }
    }


    private static int ApplyAmmoPackMetadata(List<PriceConfigItem> prices)
    {
        var changedCount = 0;

        foreach (var priceConfig in prices)
        {
            if (ApplyAmmoPackMetadata(priceConfig))
            {
                changedCount++;
            }
        }

        return changedCount;
    }

    private static bool ApplyAmmoPackMetadata(PriceConfigItem priceConfig)
    {
        if (string.IsNullOrWhiteSpace(priceConfig.TplId))
        {
            return false;
        }

        if (!AmmoPacksByAmmoTpl.TryGetValue(priceConfig.TplId, out var packMetadata))
        {
            return false;
        }

        var changed = false;

        if (priceConfig.AmmoBarterPackTplId != packMetadata.TplId)
        {
            priceConfig.AmmoBarterPackTplId = packMetadata.TplId;
            changed = true;
        }

        if (priceConfig.AmmoBarterPackItemName != packMetadata.ItemName)
        {
            priceConfig.AmmoBarterPackItemName = packMetadata.ItemName;
            changed = true;
        }

        if (priceConfig.AmmoBarterPackSize != packMetadata.Size)
        {
            priceConfig.AmmoBarterPackSize = packMetadata.Size;
            changed = true;
        }

        if (!string.Equals(priceConfig.BarterSchemeValueBasis, "Pack", StringComparison.OrdinalIgnoreCase))
        {
            priceConfig.BarterSchemeValueBasis = "Pack";
            changed = true;
        }

        return changed;
    }

    private void SaveJson<T>(string path, T data)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(data, options));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Tony] Failed to save config: {ex.Message}");
        }
    }
}
