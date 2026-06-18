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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Tony] Error loading settings.json: {ex.Message}");
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

                DebugLogging = false
            };

            SaveJson(_settingsPath, Settings);
        }
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

                Prices.Add(new PriceConfigItem
                {
                    OfferId = item.Id,
                    TplId = tpl,
                    ItemName = itemName,

                    Price = firstPaymentIsCash ? firstPayment!.Count : 0,
                    Currency = firstPaymentIsCash ? TemplateToCurrency(firstPayment!.TplId) : "RUB",

                    CashOnly = firstPaymentIsCash,
                    BarterScheme = copiedBarterScheme
                });

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
