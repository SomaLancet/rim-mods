using HarmonyLib;
using Verse;

namespace RWUPortraitsOfTheRimFix
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Bootstrap()
        {
            var harmony = new Harmony("dieruki.rwu.portraitsoftherimfix");
            var patchType = AccessTools.TypeByName("PortraitsOfTheRim.PawnRenderNodeHair_Init_Patch");
            if (patchType == null)
            {
                Log.Message("[RWU Fix Portraits of the Rim] Portraits of the Rim is not active; skipping hair graphic guard.");
                return;
            }

            var postfix = AccessTools.Method(patchType, "Postfix");
            if (postfix == null)
            {
                Log.Message("[RWU Fix Portraits of the Rim] Hair patch was not found; skipping compatibility patch.");
                return;
            }

            harmony.Patch(
                postfix,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(Bootstrap), nameof(SkipNullHairGraphicPrefix))));
            Log.Message("[RWU Fix Portraits of the Rim] Patched null hair graphic guard.");
        }

        public static bool SkipNullHairGraphicPrefix(object[] __args)
        {
            return __args == null || __args.Length < 2 || __args[1] != null;
        }
    }
}
