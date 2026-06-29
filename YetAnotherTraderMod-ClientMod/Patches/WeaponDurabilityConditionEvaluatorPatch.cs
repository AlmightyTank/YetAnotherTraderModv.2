using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT.Quests;
using HarmonyLib;
using SPT.Reflection.Patching;
using YATMQuestConditions.Client.Models;

namespace YATMQuestConditions.Client.Patches
{
    // Custom evaluator for ConditionweaponDurability.
    //
    // ConditionCounterManager.smethod_0 is the central place where a CounterCreator
    // is tested and incremented. Native EFT can deserialize our custom condition after
    // ConditionTypeResolverPatch, but native TestAll does not know how to compare it.
    //
    // This patch:
    // 1. Detects counters that include ConditionweaponDurability.
    // 2. Reads the current weapon durability from the GStruct458 checks.
    // 3. Fails the counter if the durability comparison fails.
    // 4. Temporarily removes ConditionweaponDurability before native TestAll runs,
    //    so native Kills/Location/etc. checks still work normally.
    // 5. Restores the custom condition after native TestAll finishes.
    internal class WeaponDurabilityConditionEvaluatorPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(ConditionCounterManager),
                nameof(ConditionCounterManager.smethod_0)
            );
        }

        [PatchPrefix]
        private static bool Prefix(
            int valueToAdd,
            TaskConditionCounterClass counter,
            GStruct458[] checks,
            ref List<ConditionweaponDurability> __state)
        {
            __state = null;

            if (counter == null)
            {
                return true;
            }

            var counterCreator = counter.Template as ConditionCounterCreator;
            if (counterCreator == null || counterCreator.Conditions == null)
            {
                return true;
            }

            var durabilityConditions = counterCreator.Conditions
                .OfType<ConditionweaponDurability>()
                .ToList();

            if (durabilityConditions.Count == 0)
            {
                return true;
            }

            var currentDurability = TryGetDurabilityCheckValue(checks);
            if (!currentDurability.HasValue)
            {
                Plugin.LogSource.LogInfo(
                    "[YATM Quest Conditions] Blocking counter " +
                    counterCreator.id +
                    ": weaponDurability condition exists but no durability test value was supplied."
                );

                HandleFailedCustomCondition(counter, counterCreator);
                return false;
            }

            foreach (var durabilityCondition in durabilityConditions)
            {
                if (durabilityCondition.IsValid(currentDurability.Value))
                {
                    Plugin.LogSource.LogInfo(
                        "[YATM Quest Conditions] Passed weaponDurability " +
                        durabilityCondition.id +
                        ": current=" +
                        currentDurability.Value +
                        " rule=" +
                        durabilityCondition.GetCompareMethod() +
                        " " +
                        durabilityCondition.GetRequiredValue()
                    );

                    continue;
                }

                Plugin.LogSource.LogInfo(
                    "[YATM Quest Conditions] Blocking counter " +
                    counterCreator.id +
                    " due to weaponDurability " +
                    durabilityCondition.id +
                    ": current=" +
                    currentDurability.Value +
                    " rule=" +
                    durabilityCondition.GetCompareMethod() +
                    " " +
                    durabilityCondition.GetRequiredValue()
                );

                HandleFailedCustomCondition(counter, counterCreator);
                return false;
            }

            // The custom condition passed. Remove it before native TestAll runs, because
            // native EFT does not have a built-in comparator for ConditionweaponDurability.
            __state = durabilityConditions;

            foreach (var durabilityCondition in durabilityConditions)
            {
                if (!TryRemoveCondition(counterCreator.Conditions, durabilityCondition))
                {
                    Plugin.LogSource.LogWarning(
                        "[YATM Quest Conditions] Failed to temporarily remove ConditionweaponDurability. " +
                        "Native TestAll may fail this counter. Condition id: " +
                        durabilityCondition.id
                    );
                }
            }

            return true;
        }

        [PatchPostfix]
        private static void Postfix(TaskConditionCounterClass counter, List<ConditionweaponDurability> __state)
        {
            if (__state == null || __state.Count == 0 || counter == null)
            {
                return;
            }

            var counterCreator = counter.Template as ConditionCounterCreator;
            if (counterCreator == null || counterCreator.Conditions == null)
            {
                return;
            }

            foreach (var durabilityCondition in __state)
            {
                TryAddCondition(counterCreator.Conditions, durabilityCondition);
            }
        }

        private static float? TryGetDurabilityCheckValue(GStruct458[] checks)
        {
            if (checks == null)
            {
                return null;
            }

            var durabilityIdentity = Condition.CalculateIdentity(new object[]
            {
                typeof(ConditionweaponDurability)
            });

            foreach (var check in checks)
            {
                if (check.identity != durabilityIdentity)
                {
                    continue;
                }

                try
                {
                    return Convert.ToSingle(check.testValue);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private static void HandleFailedCustomCondition(TaskConditionCounterClass counter, ConditionCounterCreator counterCreator)
        {
            if (counterCreator.doNotResetIfCounterCompleted && (float)counter.Value >= counter.Template.value)
            {
                return;
            }

            if (counterCreator.isResetOnConditionFailed)
            {
                counter.Value = 0;
            }
        }

        private static bool TryRemoveCondition(object conditions, Condition condition)
        {
            if (conditions == null || condition == null)
            {
                return false;
            }

            var list = conditions as IList;
            if (list != null)
            {
                list.Remove(condition);
                return true;
            }

            var typedCollection = conditions as ICollection<Condition>;
            if (typedCollection != null)
            {
                return typedCollection.Remove(condition);
            }

            return TryInvokeBoolCollectionMethod(conditions, "Remove", condition);
        }

        private static bool TryAddCondition(object conditions, Condition condition)
        {
            if (conditions == null || condition == null)
            {
                return false;
            }

            var list = conditions as IList;
            if (list != null)
            {
                if (!list.Contains(condition))
                {
                    list.Add(condition);
                }

                return true;
            }

            var typedCollection = conditions as ICollection<Condition>;
            if (typedCollection != null)
            {
                if (!typedCollection.Contains(condition))
                {
                    typedCollection.Add(condition);
                }

                return true;
            }

            return TryInvokeVoidCollectionMethod(conditions, "Add", condition);
        }

        private static bool TryInvokeBoolCollectionMethod(object target, string methodName, Condition condition)
        {
            try
            {
                var method = target.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(x =>
                    {
                        if (x.Name != methodName)
                        {
                            return false;
                        }

                        var parameters = x.GetParameters();
                        return parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(condition.GetType());
                    });

                if (method == null)
                {
                    return false;
                }

                var result = method.Invoke(target, new object[] { condition });
                return result == null || Convert.ToBoolean(result);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryInvokeVoidCollectionMethod(object target, string methodName, Condition condition)
        {
            try
            {
                var method = target.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(x =>
                    {
                        if (x.Name != methodName)
                        {
                            return false;
                        }

                        var parameters = x.GetParameters();
                        return parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(condition.GetType());
                    });

                if (method == null)
                {
                    return false;
                }

                method.Invoke(target, new object[] { condition });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
