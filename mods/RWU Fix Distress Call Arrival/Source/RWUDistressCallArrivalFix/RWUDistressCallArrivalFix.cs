using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RWUDistressCallArrivalFix
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        private const string HarmonyId = "dieruki.rwu.distresscallarrivalfix";

        static Bootstrap()
        {
            var harmony = new Harmony(HarmonyId);
            PatchNullParentLookup(harmony);
            PatchDistressCallCorpses(harmony);
        }

        private static void PatchNullParentLookup(Harmony harmony)
        {
            var target = AccessTools.Method(
                typeof(ParentRelationUtility),
                "GetParent",
                new[] { typeof(Pawn), typeof(Gender) });
            var prefix = AccessTools.Method(typeof(NullParentLookupPatch), nameof(NullParentLookupPatch.Prefix));

            if (target == null || prefix == null)
            {
                Log.Error("[RWU Fix Distress Call Arrival] ParentRelationUtility.GetParent target was not found; null-parent guard skipped.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix) { priority = Priority.First });
            Log.Message("[RWU Fix Distress Call Arrival] Patched ParentRelationUtility.GetParent.");
        }

        private static void PatchDistressCallCorpses(Harmony harmony)
        {
            var target = AccessTools.Method(
                typeof(DistressCallUtility),
                "SpawnCorpses",
                new[]
                {
                    typeof(Map),
                    typeof(IEnumerable<Pawn>),
                    typeof(IEnumerable<Pawn>),
                    typeof(IntVec3),
                    typeof(int)
                });
            var prefix = AccessTools.Method(typeof(DistressCallCorpsesPatch), nameof(DistressCallCorpsesPatch.Prefix));
            var transpiler = AccessTools.Method(typeof(DistressCallCorpsesPatch), nameof(DistressCallCorpsesPatch.Transpiler));

            if (target == null || prefix == null || transpiler == null)
            {
                Log.Error("[RWU Fix Distress Call Arrival] DistressCallUtility.SpawnCorpses target was not found; empty-killer guard skipped.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix) { priority = Priority.First },
                transpiler: new HarmonyMethod(transpiler) { priority = Priority.Last });
            Log.Message("[RWU Fix Distress Call Arrival] Patched DistressCallUtility.SpawnCorpses.");
        }
    }

    public static class NullParentLookupPatch
    {
        private static readonly HashSet<string> ReportedPawns = new HashSet<string>();

        public static bool Prefix(Pawn pawn, Gender parentGender, ref Pawn __result)
        {
            __result = null;
            if (pawn == null || pawn.RaceProps == null || !pawn.RaceProps.IsFlesh || pawn.relations == null)
            {
                return false;
            }

            List<DirectPawnRelation> relations = pawn.relations.DirectRelations;
            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation relation = relations[i];
                if (relation == null || relation.def != PawnRelationDefOf.Parent)
                {
                    continue;
                }

                Pawn parent = relation.otherPawn;
                if (parent == null)
                {
                    ReportMalformedRelationOnce(pawn);
                    continue;
                }

                if (parent.gender == parentGender)
                {
                    __result = parent;
                    return false;
                }
            }

            return false;
        }

        private static void ReportMalformedRelationOnce(Pawn pawn)
        {
            string id = pawn.ThingID ?? pawn.LabelShort ?? "<unknown pawn>";
            if (!ReportedPawns.Add(id))
            {
                return;
            }

            Log.Warning(
                "[RWU Fix Distress Call Arrival] Ignored Parent relation with null otherPawn on "
                + (pawn.LabelShort ?? "<unnamed pawn>") + " (" + id + ").");
        }
    }

    public static class DistressCallCorpsesPatch
    {
        private static readonly List<Pawn> MissingKillerSentinel = new List<Pawn> { null };
        private static bool reportedMissingKillers;

        public static void Prefix(ref IEnumerable<Pawn> killers)
        {
            var validKillers = new List<Pawn>();
            if (killers != null)
            {
                foreach (Pawn killer in killers)
                {
                    if (killer != null)
                    {
                        validKillers.Add(killer);
                    }
                }
            }

            if (validKillers.Count > 0)
            {
                killers = validKillers;
                return;
            }

            killers = MissingKillerSentinel;
            if (!reportedMissingKillers)
            {
                reportedMissingKillers = true;
                Log.Warning(
                    "[RWU Fix Distress Call Arrival] Distress-call corpse generation had no valid killers; "
                    + "using unattributed deaths so map generation and transporter arrival can continue.");
            }
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(
                typeof(HealthUtility),
                nameof(HealthUtility.SimulateKilledByPawn),
                new[] { typeof(Pawn), typeof(Pawn) });
            var replacement = AccessTools.Method(
                typeof(DistressCallCorpsesPatch),
                nameof(SimulateKilledByPawnSafe));

            foreach (CodeInstruction instruction in instructions)
            {
                if (original != null && replacement != null && instruction.Calls(original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                }

                yield return instruction;
            }
        }

        public static void SimulateKilledByPawnSafe(Pawn victim, Pawn killer)
        {
            if (victim == null)
            {
                return;
            }

            if (killer != null)
            {
                HealthUtility.SimulateKilledByPawn(victim, killer);
                return;
            }

            victim.Kill(null);
        }
    }
}
