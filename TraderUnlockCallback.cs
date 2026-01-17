using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;

namespace PrisciluOrigins;

/// <summary>
/// Helper class to check and unlock trader based on player level.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 2)]
public class TraderUnlockService(
    ISptLogger<TraderUnlockService> logger,
    SaveServer saveServer)
{
    private const string PrisciluTraderId = "6748adca5c70634464b214a8";
    
    // Static config set by main mod
    public static int MinLevelRequired { get; set; } = 1;
    public static bool EnableLevelLock { get; set; } = false;
    
    public void CheckAllProfiles()
    {
        if (!EnableLevelLock)
        {
            return;
        }
        
        try
        {
            var profiles = saveServer.GetProfiles();
            foreach (var (sessionId, profile) in profiles)
            {
                CheckAndUnlockTraderForSession(sessionId);
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"[PrisciluOrigins] Error checking profiles: {ex.Message}");
        }
    }
    
    public void CheckAndUnlockTraderForSession(string sessionId)
    {
        if (!EnableLevelLock)
        {
            return;
        }
        
        try
        {
            var profile = saveServer.GetProfile(sessionId);
            if (profile == null)
            {
                return;
            }
            
            // Use dynamic to access PMC character data
            dynamic profileDyn = profile;
            var pmcProfile = profileDyn.Characters?.Pmc;
            if (pmcProfile == null)
            {
                return;
            }
            
            int playerLevel = (int)(pmcProfile.Info?.Level ?? 0);
            
            // Check if trader exists in profile's tradersInfo
            var tradersInfo = pmcProfile.TradersInfo;
            if (tradersInfo != null)
            {
                try
                {
                    var traderInfo = ((IDictionary<string, object>)tradersInfo)[PrisciluTraderId];
                    if (traderInfo != null)
                    {
                        dynamic traderDyn = traderInfo;
                        bool unlocked = traderDyn.Unlocked ?? false;
                        
                        if (playerLevel >= MinLevelRequired && !unlocked)
                        {
                            traderDyn.Unlocked = true;
                            logger.Info($"[PrisciluOrigins] Trader unlocked for session {sessionId} at level {playerLevel}");
                        }
                    }
                }
                catch
                {
                    // Trader not in profile yet
                }
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"[PrisciluOrigins] Error checking trader unlock for {sessionId}: {ex.Message}");
        }
    }
}
