using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using System;
using System.Threading.Tasks;

namespace PrisciluOrigins;

/// <summary>
/// Service to check and unlock trader based on player level.
/// Uses a timer to actively check profiles for "Live" unlock.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 2)]
public class TraderUnlockService : IOnLoad, IDisposable
{
    private readonly ISptLogger<TraderUnlockService> _logger;
    private readonly SaveServer _saveServer;
    private System.Threading.Timer? _timer;
    
    private const string PrisciluTraderId = "6748adca5c70634464b214a8";
    
    // Static config set by main mod
    public static int MinLevelRequired { get; set; } = 1;
    public static bool EnableLevelLock { get; set; } = false;
    public static bool ForceUnlock { get; set; } = false; // [NEW] Force unlock if enabled by default

    public TraderUnlockService(
        ISptLogger<TraderUnlockService> logger,
        SaveServer saveServer)
    {
        _logger = logger;
        _saveServer = saveServer;
    }
    
    public Task OnLoad()
    {
        if (EnableLevelLock)
        {
            PrisciluLogger.Log($"Unlock Service Active. Required Level: {MinLevelRequired}");
            // Initial check
            CheckAllProfiles();
            
            // "Live" check every 10 seconds
            _timer = new System.Threading.Timer(
                _ => CheckAllProfiles(), 
                null, 
                TimeSpan.FromSeconds(10), 
                TimeSpan.FromSeconds(10));
        }
        else if (ForceUnlock)
        {
             PrisciluLogger.Log("Forcing Unlock for all profiles (UnlockedByDefault).");
             CheckAllProfiles();
        }

        return Task.CompletedTask;
    }
    
    public void Dispose()
    {
        _timer?.Dispose();
    }
    
    public void CheckAllProfiles()
    {
        // If Logic: 
        // 1. If LevelLock enabled -> Check level
        // 2. If ForceUnlock -> Just unlock
        if (!EnableLevelLock && !ForceUnlock) return;
        
        try
        {
            var profiles = _saveServer.GetProfiles();
            // Optional: Log every check tick? That's spammy. Maybe only if profiles found.
            // PrisciluLogger.Log($"Checking {profiles.Count} profiles...");
            
            foreach (var (sessionId, profile) in profiles)
            {
                CheckAndUnlockTrader(sessionId, profile);
            }
        }
        catch (Exception ex)
        {
            PrisciluLogger.Log($"Error checking profiles: {ex.Message}");
        }
    }
    
    // Helper to get value from Property OR Field
    private object? GetMemberValue(object target, string name)
    {
        if (target == null) return null;
        var type = target.GetType();
        var bindingFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase;
        
        // Try Property
        var prop = type.GetProperty(name, bindingFlags);
        if (prop != null) return prop.GetValue(target);
        
        // Try Field
        var field = type.GetField(name, bindingFlags);
        if (field != null) return field.GetValue(target);
        
        return null;
    }

    public void CheckAndUnlockTrader(string sessionId, object profile)
    {
        if (!EnableLevelLock || profile == null) return;
        
        try
        {
            // Access Characters
            // [FIX] Found member "CharacterData" via reflection dump
            var characters = GetMemberValue(profile, "Characters") 
                             ?? GetMemberValue(profile, "CharacterData");
            if (characters == null) 
            {
                PrisciluLogger.Log($"Session {sessionId}: CharacterData missing.");
                return;
            }
            
            var pmcProfile = GetMemberValue(characters, "Pmc") 
                             ?? GetMemberValue(characters, "PmcData")
                             ?? GetMemberValue(characters, "Pmcs");
            
            if (pmcProfile == null) 
            {
                 PrisciluLogger.Log($"Session {sessionId}: PmcData missing.");
                 return;
            }
            
            var info = GetMemberValue(pmcProfile, "Info");
            if (info == null) return;
            
            var levelValue = GetMemberValue(info, "Level");
            int playerLevel = levelValue != null ? Convert.ToInt32(levelValue) : 0;
            
            var tradersInfo = GetMemberValue(pmcProfile, "TradersInfo");
            if (tradersInfo == null) return;
            
            // Try to get the trader info from dictionary
            if (tradersInfo is System.Collections.IDictionary tradersDict)
            {
                object? targetKey = null;
                foreach (var key in tradersDict.Keys)
                {
                    if (key.ToString() == PrisciluTraderId)
                    {
                        targetKey = key;
                        break;
                    }
                }

                if (targetKey != null)
                {
                    var traderInfo = tradersDict[targetKey];
                    if (traderInfo != null)
                    {
                        var unlockedValue = GetMemberValue(traderInfo, "Unlocked");
                        bool isUnlocked = unlockedValue != null && (bool)unlockedValue;

                        // Log status periodically? No, only on change or explicit debug request.
                        // But user asked for "everything in background".
                        // Logging every 10s for every user is too much IO.
                        // I will log ONLY if it CHANGES state.
                        
                        if (playerLevel >= MinLevelRequired && !isUnlocked)
                        {
                            SetUnlocked(traderInfo, true);
                            var msg = $"LIVE UNLOCK: Session {sessionId} reached Level {playerLevel}. UNLOCKED!";
                            _logger.Info($"[PrisciluOrigins] {msg}"); // Keep console for critical event
                            PrisciluLogger.Log(msg);
                        }
                    }
                }
                else
                {
                     // Only log warning once? Or every time?
                     // Start spamming file is better than silent failure for debug mode.
                     // PrisciluLogger.Log($"WARNING: Trader {PrisciluTraderId} missing in TradersInfo dict for {sessionId}.");
                }
            }
        }
        catch (Exception ex)
        {
            PrisciluLogger.Log($"EXCEPTION in Unlock: {ex.Message}");
        }
    }

    private void SetUnlocked(object target, bool value)
    {
        var type = target.GetType();
        var bindingFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase;
        
        var prop = type.GetProperty("Unlocked", bindingFlags);
        if (prop != null) 
        {
            prop.SetValue(target, value);
            return;
        }
        var field = type.GetField("Unlocked", bindingFlags);
        if (field != null)
        {
            field.SetValue(target, value);
        }
    }
}
