using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFT.Quests;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;
using YATMQuestConditions.Client.Models;
using YATMQuestConditions.Client.Services;

namespace YATMQuestConditions.Client.Patches
{
    internal class KillConditionDurabilityPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(GClass3999<QuestClass>),
                nameof(GClass3999<QuestClass>.CheckKillConditionCounter)
            );
        }

        [PatchPrefix]
        private static bool Prefix(
            GClass3999<QuestClass> __instance,
            string target,
            string enemyProfileId,
            List<string> targetEquipment,
            Item weapon,
            EBodyPart bodyPart,
            string locationId,
            float distance,
            string role,
            int hour,
            HealthEffects enemyEffects,
            HealthEffects effects,
            List<string> zoneIds,
            string[] buffs)
        {
            var killData = __instance.method_2(
                target,
                targetEquipment,
                weapon,
                bodyPart,
                distance,
                role,
                hour,
                enemyEffects
            );

            if (killData == null)
            {
                return false;
            }

            var durability = WeaponDurabilityReader.TryReadDurability(weapon);
            if (!durability.HasValue)
            {
                durability = 999f;
            }

            Plugin.LogSource.LogInfo("[YATM Quest Conditions] Kill counter check. Weapon durability: " + durability.Value);

            __instance.ConditionalBook.TestConditions(1, new GStruct458[]
            {
                new GStruct458(new object[] { typeof(ConditionKills) }).Test(killData),
                new GStruct458(new object[] { typeof(ConditionLocation) }).Test(locationId),
                new GStruct458(new object[] { typeof(ConditionEquipment) }).Test(__instance.Profile.Inventory),
                new GStruct458(new object[] { typeof(ConditionHealthEffect) }).Test(effects),
                new GStruct458(new object[] { typeof(ConditionInZone) }).Test(zoneIds),
                new GStruct458(new object[] { typeof(ConditionTime) }).Test(GClass1891.PastTime),
                new GStruct458(new object[] { typeof(ConditionHealthBuff) }).Test(buffs),

                // YATM custom condition support.
                // CounterCreator conditions that include ConditionweaponDurability now receive the actual weapon durability.
                new GStruct458(new object[] { typeof(ConditionweaponDurability) }).Test(durability.Value)
            });

            return false;
        }
    }
}
