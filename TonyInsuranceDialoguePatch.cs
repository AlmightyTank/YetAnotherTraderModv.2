using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils.Logger;

namespace Tony;

[Injectable(TypePriority = OnLoadOrder.PostSptModLoader + 10)]
public class TonyInsuranceDialoguePatch(
    ISptLogger<TonyInsuranceDialoguePatch> logger,
    DatabaseService databaseService
) : IOnLoad
{
    private static readonly MongoId TonyId = new("66a0f6b2c4d8e90123456789");

    public Task OnLoad()
    {
        PatchTraderDialogue();
        PatchLocales();

        return Task.CompletedTask;
    }

    private void PatchTraderDialogue()
    {
        var trader = databaseService.GetTrader(TonyId);

        if (trader is null)
        {
            logger.Error($"[Tony] Could not patch insurance dialogue. Trader {TonyId} was not found.");
            return;
        }

        if (trader.Dialogue == null)
        {
            logger.Error($"[Tony] Could not patch insurance dialogue. Trader {TonyId} Dialogue property is null and cannot be set (init-only).");
            return;
        }

        trader.Dialogue["insuranceStart"] =
        [
            $"{TonyId} insuranceStart 1",
            $"{TonyId} insuranceStart 2"
        ];

        trader.Dialogue["insuranceFound"] =
        [
            $"{TonyId} insuranceFound 1",
            $"{TonyId} insuranceFound 2"
        ];

        trader.Dialogue["insuranceFailed"] =
        [
            $"{TonyId} insuranceFailed 1"
        ];

        logger.Success("[Tony] Patched insurance dialogue keys.");
    }

    private void PatchLocales()
    {
        var locales = databaseService.GetLocales();

        if (!locales.Global.TryGetValue("en", out var englishLocale) || englishLocale is null)
        {
            logger.Error("[Tony] Could not patch insurance locale text. English locale was not found.");
            return;
        }

        englishLocale.AddTransformer(locale =>
        {
            if (locale is null)
            {
                logger.Error("[Tony] Locale transformer received a null locale.");
                return locale;
            }

            locale[$"{TonyId} insuranceStart 1"] =
                "You paid for speed. My people are already moving.";

            locale[$"{TonyId} insuranceStart 2"] =
                "Volkov prices buy Volkov speed. Watch your messages.";

            locale[$"{TonyId} insuranceFound 1"] =
                "Your gear is back. Fast work is not cheap, but you already knew that.";

            locale[$"{TonyId} insuranceFound 2"] =
                "Recovered and stored. Take it before I start charging for the space.";

            locale[$"{TonyId} insuranceFailed 1"] =
                "Nothing came back. Someone got to it first, or there was nothing worth saving.";

            return locale;
        });

        logger.Success("[Tony] Patched insurance locale text.");
    }
}