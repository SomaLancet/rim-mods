using System;
using System.Collections.Generic;
using HarmonyLib;
using PipeSystem;
using Verse;

namespace RWUAutomaticHydroponicsGravshipFix
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        private const string TargetPackageId = "Poncho.AutomaticHydroponics";

        static Bootstrap()
        {
            if (!ModsConfig.IsActive(TargetPackageId))
            {
                Log.Message("[RWU Fix Automatic Hydroponics Gravship] Target mod is not active; skipping.");
                return;
            }

            var harmony = new Harmony("dieruki.rwu.automatichydroponicsgravshipfix");
            harmony.PatchAll(typeof(Bootstrap).Assembly);
            Log.Message("[RWU Fix Automatic Hydroponics Gravship] Gravship process-state guard enabled.");
        }
    }

    internal sealed class ProcessState
    {
        internal Process Process;
        internal int TickLeft;
        internal int InitialTicks;
        internal int CycleTicks;
        internal float Progress;
        internal bool PickUpReady;
    }

    internal static class HydroponicsStateGuard
    {
        private const string BasePackageId = "Poncho.AutomaticHydroponics";
        private const string ExpandedPackageId = "AutomaticHydroponics.Expanded";

        private static readonly Dictionary<CompAdvancedResourceProcessor, List<ProcessState>> SwapStates =
            new Dictionary<CompAdvancedResourceProcessor, List<ProcessState>>();

        private static readonly AccessTools.FieldRef<CompAdvancedResourceProcessor, ProcessStack> CachedStackRef =
            AccessTools.FieldRefAccess<CompAdvancedResourceProcessor, ProcessStack>("cachedProcessStack");

        private static readonly AccessTools.FieldRef<ProcessStack, ThingWithComps> StackParentRef =
            AccessTools.FieldRefAccess<ProcessStack, ThingWithComps>("parent");

        private static readonly AccessTools.FieldRef<Process, ThingWithComps> ProcessParentRef =
            AccessTools.FieldRefAccess<Process, ThingWithComps>("parent");

        internal static bool IsTarget(CompAdvancedResourceProcessor comp)
        {
            var packageId = comp?.parent?.def?.modContentPack?.PackageId;
            return string.Equals(packageId, BasePackageId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(packageId, ExpandedPackageId, StringComparison.OrdinalIgnoreCase);
        }

        internal static void Capture(CompAdvancedResourceProcessor comp)
        {
            if (!IsTarget(comp) || comp.ProcessStack == null)
            {
                return;
            }

            var states = new List<ProcessState>();
            foreach (var process in comp.ProcessStack)
            {
                if (process == null)
                {
                    continue;
                }

                states.Add(new ProcessState
                {
                    Process = process,
                    TickLeft = process.tickLeft,
                    InitialTicks = process.cachedInitialTicks,
                    CycleTicks = process.ticksOrQualityTicks,
                    Progress = process.Progress,
                    PickUpReady = process.pickUpReady
                });
            }

            SwapStates[comp] = states;
        }

        internal static void RestoreAfterSwap(CompAdvancedResourceProcessor comp)
        {
            if (!IsTarget(comp))
            {
                return;
            }

            if (SwapStates.TryGetValue(comp, out var states))
            {
                for (int i = 0; i < states.Count; i++)
                {
                    var state = states[i];
                    var process = state.Process;
                    if (process == null)
                    {
                        continue;
                    }

                    process.tickLeft = state.TickLeft;
                    process.cachedInitialTicks = state.InitialTicks;
                    process.ticksOrQualityTicks = state.CycleTicks;
                    process.Progress = state.Progress;
                    process.pickUpReady = state.PickUpReady;
                }

                SwapStates.Remove(comp);
            }

            Normalize(comp, true);
        }

        internal static void Normalize(CompAdvancedResourceProcessor comp, bool repairUnfinishedTimer)
        {
            if (!IsTarget(comp) || comp.ProcessStack == null || comp.parent == null)
            {
                return;
            }

            // VEF leaves this reference populated after a gravship swap. Keeping it makes
            // later saves serialize the stale cache instead of the live process stack.
            CachedStackRef(comp) = null;
            StackParentRef(comp.ProcessStack) = comp.parent;

            foreach (var process in comp.ProcessStack)
            {
                if (process == null)
                {
                    continue;
                }

                ProcessParentRef(process) = comp.parent;
                process.advancedProcessor = comp;

                var definitionTicks = process.Def?.ticks ?? 0;
                if (process.ticksOrQualityTicks <= 0)
                {
                    process.ticksOrQualityTicks = definitionTicks;
                }
                if (process.cachedInitialTicks <= 0)
                {
                    process.cachedInitialTicks = process.ticksOrQualityTicks > 0
                        ? process.ticksOrQualityTicks
                        : definitionTicks;
                }

                // A timer at/below zero is valid only while output is being completed.
                // During load/map setup no process ticks, so an unfinished bill in this
                // state is the corrupt state that causes immediate item spawning.
                if (repairUnfinishedTimer && process.tickLeft <= 0 && !process.pickUpReady && process.ShouldDoNow())
                {
                    process.tickLeft = process.cachedInitialTicks > 0
                        ? process.cachedInitialTicks
                        : process.ticksOrQualityTicks;
                    process.Progress = 0f;
                    Log.Warning("[RWU Fix Automatic Hydroponics Gravship] Repaired an invalid unfinished timer on "
                        + comp.parent.LabelCap + ".");
                }

                if (comp.parent.Spawned)
                {
                    process.PostSpawnSetup();
                }
            }
        }
    }

    [HarmonyPatch(typeof(CompAdvancedResourceProcessor), nameof(CompAdvancedResourceProcessor.PreSwapMap))]
    internal static class PreSwapMapPatch
    {
        private static void Prefix(CompAdvancedResourceProcessor __instance)
        {
            HydroponicsStateGuard.Capture(__instance);
        }
    }

    [HarmonyPatch(typeof(CompAdvancedResourceProcessor), nameof(CompAdvancedResourceProcessor.PostSwapMap))]
    internal static class PostSwapMapPatch
    {
        private static void Postfix(CompAdvancedResourceProcessor __instance)
        {
            HydroponicsStateGuard.RestoreAfterSwap(__instance);
        }
    }

    [HarmonyPatch(typeof(CompAdvancedResourceProcessor), nameof(CompAdvancedResourceProcessor.PostSpawnSetup))]
    internal static class PostSpawnSetupPatch
    {
        private static void Postfix(CompAdvancedResourceProcessor __instance, bool respawningAfterLoad)
        {
            HydroponicsStateGuard.Normalize(__instance, respawningAfterLoad);
        }
    }
}
