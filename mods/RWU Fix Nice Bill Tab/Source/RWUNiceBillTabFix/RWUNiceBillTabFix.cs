using HarmonyLib;
using RimWorld;
using Verse;

namespace RWUNiceBillTabFix
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        private const string TargetPackageId = "Andromeda.NiceBillTab";

        static Bootstrap()
        {
            if (!ModsConfig.IsActive(TargetPackageId))
            {
                Log.Message("[RWU Fix Nice Bill Tab] Target mod is not active; skipping.");
                return;
            }

            var drawerType = AccessTools.TypeByName("NiceBillTab.TabBillsDrawer");
            if (drawerType == null)
            {
                Log.Warning("[RWU Fix Nice Bill Tab] TabBillsDrawer type was not found; skipping.");
                return;
            }

            var target = AccessTools.Method(drawerType, "GetMaterialFromBill", new[] { typeof(Bill) });
            if (target == null)
            {
                Log.Warning("[RWU Fix Nice Bill Tab] GetMaterialFromBill(Bill) was not found; skipping.");
                return;
            }

            var harmony = new Harmony("dieruki.rwu.nicebilltabfix");
            harmony.Patch(
                target,
                postfix: new HarmonyMethod(AccessTools.Method(
                    typeof(MaterialPatches),
                    nameof(MaterialPatches.GetMaterialFromBillPostfix))));

            Log.Message("[RWU Fix Nice Bill Tab] Patched invalid bill material detection.");
        }
    }

    public static class MaterialPatches
    {
        private static bool warnedAboutInvalidMaterial;

        public static void GetMaterialFromBillPostfix(Bill __0, ref ThingDef __result)
        {
            if (__result == null || __result.stuffProps != null)
            {
                return;
            }

            var invalidMaterial = __result;
            __result = null;

            if (!warnedAboutInvalidMaterial)
            {
                warnedAboutInvalidMaterial = true;
                var recipeName = __0?.recipe?.defName ?? "unknown";
                Log.Warning(
                    "[RWU Fix Nice Bill Tab] Ignored non-stuff material "
                    + invalidMaterial.defName
                    + " while drawing bill "
                    + recipeName
                    + ".");
            }
        }
    }
}
