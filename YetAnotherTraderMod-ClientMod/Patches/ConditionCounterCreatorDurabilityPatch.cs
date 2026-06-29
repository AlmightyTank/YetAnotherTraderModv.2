using System.Reflection;
using EFT.Quests;
using HarmonyLib;
using SPT.Reflection.Patching;
using YATMQuestConditions.Client.Models;
using YATMQuestConditions.Client.Services;

namespace YATMQuestConditions.Client.Patches
{
    internal class ConditionCounterCreatorDurabilityPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(ConditionCounterCreator),
                nameof(ConditionCounterCreator.OnDeserializedMethod)
            );
        }

        [PatchPostfix]
        private static void Postfix(ConditionCounterCreator __instance)
        {
            if (__instance == null || __instance.Conditions == null)
            {
                return;
            }

            ConditionKills killCondition = null;
            ConditionweaponDurability durabilityCondition = null;

            foreach (Condition condition in __instance.Conditions)
            {
                if (killCondition == null)
                {
                    killCondition = condition as ConditionKills;
                }

                if (durabilityCondition == null)
                {
                    durabilityCondition = condition as ConditionweaponDurability;
                }
            }

            if (killCondition == null || durabilityCondition == null)
            {
                return;
            }

            var killConditionId = killCondition.id;
            var durabilityConditionId = durabilityCondition.id;

            if (string.IsNullOrWhiteSpace(killConditionId))
            {
                return;
            }

            var rule = new WeaponDurabilityRule
            {
                Enabled = true,
                CompareMethod = ReflectionValueReader.TryReadString(durabilityCondition, "compareMethod") ?? "<=",
                Value = ReflectionValueReader.TryReadFloat(durabilityCondition, "value") ?? 60f,
                UseCurrentDurability = durabilityCondition.useCurrentDurability,
                SourceConditionId = durabilityConditionId,
                BoundKillConditionId = killConditionId
            };

            WeaponDurabilityRules.AddOrUpdateRule(killConditionId, rule);

            Plugin.LogSource.LogInfo(
                "[YATM Quest Conditions] Bound ConditionweaponDurability " +
                durabilityConditionId +
                " to Kills condition " +
                killConditionId +
                " durability " +
                rule.CompareMethod +
                " " +
                rule.Value
            );
        }
    }
}
