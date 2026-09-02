using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RWUMoreGravshipWorkbenchesFix
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        private const string PackageId = "LTS.MGW";

        static Bootstrap()
        {
            if (!ModsConfig.IsActive(PackageId))
            {
                Log.Message("[RWU Fix More Gravship Workbenches] Target mod is not active; skipping.");
                return;
            }

            var grinderType = AccessTools.TypeByName("MoreGravshipWorkbenches.LTS_Building_NutrientGrinderAndHopper");
            if (grinderType == null)
            {
                Log.Warning("[RWU Fix More Gravship Workbenches] Nutrient grinder type was not found; skipping.");
                return;
            }

            var harmony = new Harmony("dieruki.rwu.moregravshipworkbenchesfix");
            PatchMethod(harmony, AccessTools.Method(grinderType, "ExposeData"), postfix: nameof(GrinderPatches.ExposeDataPostfix));
            PatchMethod(harmony, AccessTools.Method(grinderType, "Notify_ReceivedThing"), prefix: nameof(GrinderPatches.NotifyReceivedThingPrefix), finalizer: nameof(GrinderPatches.SuppressNullRefFinalizer));
            PatchMethod(harmony, AccessTools.Method(grinderType, "Notify_LostThing"), prefix: nameof(GrinderPatches.NotifyLostThingPrefix), finalizer: nameof(GrinderPatches.SuppressNullRefFinalizer));
            PatchMethod(harmony, AccessTools.Method(grinderType, "HasEnoughFeed"), prefix: nameof(GrinderPatches.HasEnoughFeedPrefix), finalizer: nameof(GrinderPatches.BoolFalseNullRefFinalizer));
            PatchMethod(harmony, AccessTools.Method(grinderType, "FindFeedInAnyHopper"), prefix: nameof(GrinderPatches.FindFeedInAnyHopperPrefix), finalizer: nameof(GrinderPatches.ThingNullRefFinalizer));
            Log.Message("[RWU Fix More Gravship Workbenches] Patched nutrient grinders.");
        }

        private static void PatchMethod(Harmony harmony, System.Reflection.MethodInfo original, string prefix = null, string postfix = null, string finalizer = null)
        {
            if (original == null)
            {
                Log.Warning("[RWU Fix More Gravship Workbenches] Expected target method was not found.");
                return;
            }

            var patchType = typeof(GrinderPatches);
            harmony.Patch(
                original,
                prefix == null ? null : new HarmonyMethod(AccessTools.Method(patchType, prefix)),
                postfix == null ? null : new HarmonyMethod(AccessTools.Method(patchType, postfix)),
                finalizer: finalizer == null ? null : new HarmonyMethod(AccessTools.Method(patchType, finalizer)));
        }
    }

    public static class GrinderPatches
    {
        private const string ContentsFieldName = "Contents";
        private static readonly Dictionary<Type, AccessTools.FieldRef<object, List<Thing>>> ContentsAccessors = new Dictionary<Type, AccessTools.FieldRef<object, List<Thing>>>();

        public static void ExposeDataPostfix(object __instance)
        {
            CleanContents(__instance);
        }

        public static bool NotifyReceivedThingPrefix(object __instance, Thing newItem)
        {
            CleanContents(__instance);
            return newItem != null && !newItem.Destroyed;
        }

        public static bool NotifyLostThingPrefix(object __instance, Thing newItem)
        {
            var contents = CleanContents(__instance);
            contents?.Remove(newItem);
            return true;
        }

        public static bool HasEnoughFeedPrefix(object __instance, ref bool __result)
        {
            if (!(__instance is ThingWithComps building))
            {
                __result = false;
                return false;
            }

            float nutrition = 0f;
            var contents = CleanContents(__instance);
            if (contents != null)
            {
                for (int i = 0; i < contents.Count; i++)
                {
                    var thing = contents[i];
                    if (IsUsableFeedstock(thing))
                    {
                        nutrition += thing.stackCount * thing.GetStatValue(StatDefOf.Nutrition, true);
                    }
                }
            }

            __result = nutrition >= building.def.building.nutritionCostPerDispense;
            return false;
        }

        public static bool FindFeedInAnyHopperPrefix(object __instance, ref Thing __result)
        {
            __result = null;
            var contents = CleanContents(__instance);
            if (contents == null)
            {
                return false;
            }

            for (int i = 0; i < contents.Count; i++)
            {
                var thing = contents[i];
                if (IsUsableFeedstock(thing))
                {
                    __result = thing;
                    return false;
                }
            }

            return false;
        }

        public static Exception SuppressNullRefFinalizer(Exception __exception, object __instance)
        {
            if (__exception is NullReferenceException)
            {
                CleanContents(__instance);
                return null;
            }

            return __exception;
        }

        public static Exception BoolFalseNullRefFinalizer(Exception __exception, object __instance, ref bool __result)
        {
            if (__exception is NullReferenceException)
            {
                CleanContents(__instance);
                __result = false;
                return null;
            }

            return __exception;
        }

        public static Exception ThingNullRefFinalizer(Exception __exception, object __instance, ref Thing __result)
        {
            if (__exception is NullReferenceException)
            {
                CleanContents(__instance);
                __result = null;
                return null;
            }

            return __exception;
        }

        private static bool IsUsableFeedstock(Thing thing)
        {
            if (thing == null || thing.Destroyed || thing.def == null || thing.stackCount <= 0)
            {
                return false;
            }

            var def = thing.def;
            return def.IsNutritionGivingIngestible
                && def.ingestible != null
                && def.ingestible.preferability != FoodPreferability.Undefined
                && (def.ingestible.foodType & FoodTypeFlags.Plant) != FoodTypeFlags.Plant
                && (def.ingestible.foodType & FoodTypeFlags.Tree) != FoodTypeFlags.Tree;
        }

        private static List<Thing> CleanContents(object instance)
        {
            if (instance == null)
            {
                return null;
            }

            var contentsRef = GetContentsAccessor(instance.GetType());
            if (contentsRef == null)
            {
                return null;
            }

            var contents = contentsRef(instance);
            if (contents == null)
            {
                contents = new List<Thing>();
                contentsRef(instance) = contents;
                return contents;
            }

            contents.RemoveAll(thing => thing == null || thing.Destroyed || thing.def == null || thing.stackCount <= 0);
            return contents;
        }

        private static AccessTools.FieldRef<object, List<Thing>> GetContentsAccessor(Type type)
        {
            if (ContentsAccessors.TryGetValue(type, out var accessor))
            {
                return accessor;
            }

            try
            {
                accessor = AccessTools.FieldRefAccess<List<Thing>>(type, ContentsFieldName);
                ContentsAccessors[type] = accessor;
                return accessor;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[RWU Fix More Gravship Workbenches] Could not access Contents field: " + ex, 0x4D475746);
                ContentsAccessors[type] = null;
                return null;
            }
        }
    }
}
