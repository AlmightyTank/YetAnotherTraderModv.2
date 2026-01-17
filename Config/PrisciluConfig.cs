using System.Reflection; // [NEW] For reflection
using System.Text.Json;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Servers;
using Path = System.IO.Path; // [FIX] Ambiguity

namespace PrisciluOrigins.Config;

public class PrisciluConfig
{
    private readonly string _modPath;
    private readonly string _configDir;
    private readonly string _settingsPath;
    private readonly string _pricesPath;
    
    private readonly DatabaseServer _databaseServer;

    public SettingsConfig Settings { get; private set; } = new();
    public List<PriceConfigItem> Prices { get; private set; } = new();

    public PrisciluConfig(string modPath, DatabaseServer databaseServer)
    {
        _modPath = modPath;
        _databaseServer = databaseServer;
        _configDir = Path.Combine(_modPath, "config");
        _settingsPath = Path.Combine(_configDir, "settings.json");
        _pricesPath = Path.Combine(_configDir, "prices.json");
    }

    // Helper to find Template ID robustly
    public static string GetTemplateId(object item)
    {
        try 
        {
            var type = item.GetType();
            
            // Try properties
            var props = new[] { "Tpl", "_tpl", "TemplateId" };
            foreach (var propName in props)
            {
                var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop != null) return prop.GetValue(item)?.ToString();
            }

            // Try fields
            var fields = new[] { "Tpl", "_tpl", "TemplateId" };
            foreach (var fieldName in fields)
            {
                var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (field != null) return field.GetValue(item)?.ToString();
            }

            // Fallback: If absolutely nothing matches, try getting ANY property that has the value matching ID for simple checks, or dump properties (debug)
            // Debug Log (Consoles only visible in server window)
            Console.WriteLine($"[Priscilu] COULD NOT FIND TEMPLATE ID FOR ITEM TYPE: {type.FullName}");
            Console.WriteLine($"[Priscilu] Available Properties: {string.Join(", ", type.GetProperties().Select(p => p.Name))}");
            Console.WriteLine($"[Priscilu] Available Fields: {string.Join(", ", type.GetFields().Select(f => f.Name))}");

            return ((dynamic)item).Id; 
        }
        catch (Exception ex)
        {
             // Console.WriteLine($"[Priscilu] Error getting template id: {ex.Message}");
             return null;
        }
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
                Settings = JsonSerializer.Deserialize<SettingsConfig>(json) ?? new SettingsConfig();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Priscilu] Error loading settings.json: {ex.Message}");
            }
        }
        else
        {
            Settings = new SettingsConfig
            {
                MinLevel = baseJson.LoyaltyLevels.FirstOrDefault()?.MinLevel ?? 1,
                UnlockedByDefault = baseJson.UnlockedByDefault ?? false,
                RestockTimerSeconds = 3600
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
                Prices = JsonSerializer.Deserialize<List<PriceConfigItem>>(json) ?? new List<PriceConfigItem>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Priscilu] Error loading prices.json: {ex.Message}");
            }
        }
        else
        {
            Prices = new List<PriceConfigItem>();

            foreach (var item in assortJson.Items)
            {
                if (item.ParentId == "hideout")
                {
                    // Find price
                    if (!assortJson.BarterScheme.ContainsKey(item.Id)) continue;
                    
                    var schemeList = assortJson.BarterScheme[item.Id];
                    if (schemeList == null || schemeList.Count == 0) continue;
                    
                    var scheme = schemeList[0][0]; // Assuming first scheme, first component

                    // [FIX] Use Reflection Helper
                    var tpl = GetTemplateId(item);
                    if (string.IsNullOrEmpty(tpl)) 
                    {
                        continue;
                    }

                    var name = tpl; 

                    Prices.Add(new PriceConfigItem
                    {
                        TplId = tpl,
                        ItemName = name,
                        Price = scheme.Count ?? 0, 
                        Currency = scheme.Template == "5449016a4bdc2d6f028b456f" ? "RUB" : 
                                   scheme.Template == "5696686a4bdc2da3298b456a" ? "USD" : 
                                   scheme.Template == "569668774bdc2da2298b4568" ? "EUR" : "OTHER"
                    });
                }
            }
            
            Prices = Prices.OrderBy(x => x.ItemName).ToList();

            SaveJson(_pricesPath, Prices);
        }
    }

    private void SaveJson<T>(string path, T data)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(data, options));
    }
}
