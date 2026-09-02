using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RWUTransferableCompIngredientsFix
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        private const string HarmonyId = "dieruki.rwu.transferablecompingredientsfix";

        static Bootstrap()
        {
            var target = AccessTools.Method(
                typeof(TransferableUtility),
                nameof(TransferableUtility.TransferAsOne),
                new[] { typeof(Thing), typeof(Thing), typeof(TransferAsOneMode) });

            if (target == null)
            {
                Log.Error("[RWU Fix Transferable CompIngredients] TransferableUtility.TransferAsOne target was not found; skipping.");
                return;
            }

            var prefix = new HarmonyMethod(AccessTools.Method(typeof(TransferAsOnePatch), nameof(TransferAsOnePatch.Prefix)))
            {
                priority = Priority.First,
                before = new[] { "mrcool92.agoc" }
            };
            var finalizer = new HarmonyMethod(AccessTools.Method(typeof(TransferAsOnePatch), nameof(TransferAsOnePatch.Finalizer)))
            {
                priority = Priority.Last
            };

            new Harmony(HarmonyId).Patch(target, prefix: prefix, finalizer: finalizer);
            Log.Message("[RWU Fix Transferable CompIngredients] Patched TransferableUtility.TransferAsOne.");
        }
    }

    public static class TransferAsOnePatch
    {
        private static readonly HashSet<string> ReportedSignatures = new HashSet<string>();

        public static bool Prefix(Thing a, Thing b, ref bool __result)
        {
            var badA = FindMalformedIngredientsComp(a);
            var badB = FindMalformedIngredientsComp(b);
            if (badA == null && badB == null)
            {
                return true;
            }

            __result = false;
            ReportOnce(a, badA, b, badB);
            return false;
        }

        public static Exception Finalizer(Exception __exception, Thing a, Thing b, ref bool __result)
        {
            if (!(__exception is InvalidCastException))
            {
                return __exception;
            }

            __result = false;
            ReportInvalidCastOnce(a, b, __exception);
            return null;
        }

        private static CompIngredients FindMalformedIngredientsComp(Thing thing)
        {
            if (!(thing is ThingWithComps thingWithComps))
            {
                return null;
            }

            var comps = thingWithComps.AllComps;
            for (int i = 0; i < comps.Count; i++)
            {
                if (comps[i] is CompIngredients ingredients
                    && !(ingredients.props is CompProperties_Ingredients))
                {
                    return ingredients;
                }
            }

            return null;
        }

        private static void ReportOnce(Thing a, CompIngredients badA, Thing b, CompIngredients badB)
        {
            string descriptionA = Describe(a, badA);
            string descriptionB = Describe(b, badB);
            string signature = descriptionA + " | " + descriptionB;
            if (!ReportedSignatures.Add(signature))
            {
                return;
            }

            Log.Warning(
                "[RWU Fix Transferable CompIngredients] Prevented an invalid ingredient-comp comparison; "
                + "the affected things will remain separate transfer rows. A=" + descriptionA
                + "; B=" + descriptionB);
        }

        private static void ReportInvalidCastOnce(Thing a, Thing b, Exception exception)
        {
            string descriptionA = DescribeRuntimeType(a);
            string descriptionB = DescribeRuntimeType(b);
            string signature = "InvalidCast | " + descriptionA + " | " + descriptionB;
            if (!ReportedSignatures.Add(signature))
            {
                return;
            }

            Log.Warning(
                "[RWU Fix Transferable CompIngredients] Suppressed InvalidCastException while comparing "
                + "transferable things; the affected things will remain separate transfer rows. A="
                + descriptionA + "; B=" + descriptionB + "; exception=" + exception.Message);
        }

        private static string Describe(Thing thing, CompIngredients badComp)
        {
            if (thing == null)
            {
                return "null";
            }

            string defName = thing.def?.defName ?? "<no def>";
            string thingId = thing.ThingID ?? "<no id>";
            if (badComp == null)
            {
                return defName + "#" + thingId + "[valid]";
            }

            string propsType = badComp.props?.GetType().FullName ?? "null";
            return defName + "#" + thingId + "[CompIngredients.props=" + propsType + "]";
        }

        private static string DescribeRuntimeType(Thing thing)
        {
            if (thing == null)
            {
                return "null";
            }

            string defName = thing.def?.defName ?? "<no def>";
            string category = thing.def == null ? "<no category>" : thing.def.category.ToString();
            string thingId = thing.ThingID ?? "<no id>";
            return defName + "#" + thingId + "[runtime=" + thing.GetType().FullName
                + ", category=" + category + "]";
        }
    }
}
