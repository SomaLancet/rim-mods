using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RWUCornerConstruction
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Bootstrap()
        {
            var harmony = new Harmony("dieruki.rwu.cornerconstruction");
            harmony.PatchAll();
            Log.Message("[RWU Extension Corner Construction] Loaded corner construction access patches.");
        }
    }

    internal static class CornerConstructionAccess
    {
        private static readonly IntVec3[] DiagonalOffsets =
        {
            new IntVec3(1, 0, 1),
            new IntVec3(1, 0, -1),
            new IntVec3(-1, 0, 1),
            new IntVec3(-1, 0, -1)
        };

        public static bool CanUseFor(Pawn pawn, Thing target)
        {
            return TryFindDiagonalWorkCell(pawn, target, out _);
        }

        public static void SetDiagonalWorkCellIfAvailable(Pawn pawn, Thing target, Job job)
        {
            if (job == null || !TryFindDiagonalWorkCell(pawn, target, out var cell))
            {
                return;
            }

            job.targetB = cell;
        }

        public static bool TryGetDiagonalWorkCell(Job job, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (job == null || !job.targetA.HasThing || !job.targetB.IsValid)
            {
                return false;
            }

            if (!IsSupportedWorkTarget(job.targetA.Thing))
            {
                return false;
            }

            var candidate = job.targetB.Cell;
            if (!candidate.IsValid || !IsDiagonalTouch(candidate, job.targetA))
            {
                return false;
            }

            cell = candidate;
            return true;
        }

        public static bool TryFindDiagonalWorkCell(Pawn pawn, Thing target, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (pawn == null || target == null || target.Destroyed || target.Map == null || pawn.Map != target.Map)
            {
                return false;
            }

            if (!IsSupportedWorkTarget(target))
            {
                return false;
            }

            if (target.IsForbidden(pawn) || !pawn.CanReserve(target, 1, -1, null, false))
            {
                return false;
            }

            var map = target.Map;
            var best = IntVec3.Invalid;
            var bestDistance = float.MaxValue;
            for (int i = 0; i < DiagonalOffsets.Length; i++)
            {
                var candidate = target.Position + DiagonalOffsets[i];
                if (!candidate.InBounds(map) || !candidate.Standable(map))
                {
                    continue;
                }

                if (!pawn.CanReach(candidate, PathEndMode.OnCell, Danger.Some))
                {
                    continue;
                }

                var distance = pawn.Position.DistanceToSquared(candidate);
                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            if (!best.IsValid)
            {
                return false;
            }

            cell = best;
            return true;
        }

        public static bool IsSupportedWorkTarget(Thing target)
        {
            if (target == null || target.Destroyed)
            {
                return false;
            }

            if (target is Frame)
            {
                return true;
            }

            var map = target.Map;
            return map != null && map.designationManager.DesignationOn(target, DesignationDefOf.Deconstruct) != null;
        }

        public static bool IsDiagonalTouch(IntVec3 root, LocalTargetInfo target)
        {
            if (!target.IsValid)
            {
                return false;
            }

            var targetCell = target.Cell;
            return Math.Abs(root.x - targetCell.x) == 1 && Math.Abs(root.z - targetCell.z) == 1;
        }
    }

    [HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.CanTouchTargetFromValidCell))]
    public static class GenConstructCanTouchTargetFromValidCellPatch
    {
        public static void Postfix(Thing t, Pawn pawn, ref bool __result)
        {
            if (!__result && CornerConstructionAccess.CanUseFor(pawn, t))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructFinishFrames), nameof(WorkGiver_ConstructFinishFrames.JobOnThing))]
    public static class WorkGiverConstructFinishFramesJobOnThingPatch
    {
        public static void Postfix(Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            if (!(t is Frame))
            {
                return;
            }

            CornerConstructionAccess.SetDiagonalWorkCellIfAvailable(pawn, t, __result);
        }
    }

    [HarmonyPatch(typeof(WorkGiver_RemoveBuilding), nameof(WorkGiver_RemoveBuilding.HasJobOnThing))]
    public static class WorkGiverRemoveBuildingHasJobOnThingPatch
    {
        public static void Postfix(Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            if (__result || !CornerConstructionAccess.CanUseFor(pawn, t))
            {
                return;
            }

            __result = true;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_RemoveBuilding), nameof(WorkGiver_RemoveBuilding.JobOnThing))]
    public static class WorkGiverRemoveBuildingJobOnThingPatch
    {
        public static void Postfix(Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            CornerConstructionAccess.SetDiagonalWorkCellIfAvailable(pawn, t, __result);
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
    public static class PawnPathFollowerStartPathPatch
    {
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(Pawn_PathFollower), "pawn");

        public static void Prefix(Pawn_PathFollower __instance, ref LocalTargetInfo dest, ref PathEndMode peMode)
        {
            var pawn = PawnField?.GetValue(__instance) as Pawn;
            var job = pawn?.CurJob;
            if (job == null || !dest.HasThing || dest.Thing != job.targetA.Thing)
            {
                return;
            }

            if (!CornerConstructionAccess.TryGetDiagonalWorkCell(job, out var cell))
            {
                return;
            }

            dest = cell;
            peMode = PathEndMode.OnCell;
        }
    }

    [HarmonyPatch(typeof(TouchPathEndModeUtility), nameof(TouchPathEndModeUtility.IsAdjacentOrInsideAndAllowedToTouch))]
    public static class TouchPathEndModeUtilityIsAdjacentOrInsidePatch
    {
        public static void Postfix(IntVec3 root, LocalTargetInfo target, PathingContext pc, ref bool __result)
        {
            if (__result || !target.HasThing || !CornerConstructionAccess.IsSupportedWorkTarget(target.Thing))
            {
                return;
            }

            if (CornerConstructionAccess.IsDiagonalTouch(root, target))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(TouchPathEndModeUtility), nameof(TouchPathEndModeUtility.IsAdjacentCornerAndNotAllowed))]
    public static class TouchPathEndModeUtilityCornerBlockedPatch
    {
        public static void Postfix(Thing forThing, ref bool __result)
        {
            if (__result && CornerConstructionAccess.IsSupportedWorkTarget(forThing))
            {
                __result = false;
            }
        }
    }
}
