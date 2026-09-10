using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RWUSpareMarkedEnemies
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Bootstrap()
        {
            var harmony = new Harmony("dieruki.rwu.sparemarkedenemies");
            harmony.PatchAll();
            Log.Message("[RWU Extension Spare Marked Enemies] Loaded hostile pawn marking and colonist attack protection.");
        }
    }

    public sealed class SpareMarkedEnemiesState : GameComponent
    {
        private HashSet<int> markedPawnIds = new HashSet<int>();

        public SpareMarkedEnemiesState(Game game)
        {
        }

        public bool Contains(Pawn pawn)
        {
            return pawn != null && markedPawnIds.Contains(pawn.thingIDNumber);
        }

        public void SetMarked(Pawn pawn, bool marked)
        {
            if (pawn == null)
            {
                return;
            }

            if (marked)
            {
                markedPawnIds.Add(pawn.thingIDNumber);
            }
            else
            {
                markedPawnIds.Remove(pawn.thingIDNumber);
            }
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref markedPawnIds, "spareMarkedPawnIds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && markedPawnIds == null)
            {
                markedPawnIds = new HashSet<int>();
            }
        }
    }

    internal static class SpareMarkedEnemyUtility
    {
        public static SpareMarkedEnemiesState State =>
            Current.Game?.GetComponent<SpareMarkedEnemiesState>();

        public static bool IsEligibleEnemy(Pawn pawn)
        {
            return pawn != null && pawn.Spawned && !pawn.Dead &&
                   Faction.OfPlayer != null && pawn.HostileTo(Faction.OfPlayer);
        }

        public static bool IsMarked(Pawn pawn)
        {
            return IsEligibleEnemy(pawn) && HasPersistentMark(pawn);
        }

        public static bool HasPersistentMark(Pawn pawn)
        {
            return pawn != null && State?.Contains(pawn) == true;
        }

        public static bool ShouldProtectFrom(Pawn attacker, Pawn target)
        {
            return attacker != null && attacker.IsColonistPlayerControlled && IsMarked(target);
        }

        public static bool ShouldBlockVerb(Verb verb, LocalTargetInfo target)
        {
            return verb?.verbProps?.violent == true &&
                   ShouldProtectFrom(verb.caster as Pawn, target.Pawn);
        }

        public static void Toggle(Pawn pawn)
        {
            SpareMarkedEnemiesState state = State;
            if (state == null || !IsEligibleEnemy(pawn))
            {
                return;
            }

            bool mark = !state.Contains(pawn);
            state.SetMarked(pawn, mark);
            if (mark)
            {
                InterruptCurrentColonistAttacks(pawn);
            }
        }

        public static void RemoveMark(Pawn pawn)
        {
            State?.SetMarked(pawn, false);
        }

        public static void DrawMarker(Thing thing, float worldOffsetZ)
        {
            if (thing == null || !thing.Spawned || thing.Map.fogGrid.IsFogged(thing.Position))
            {
                return;
            }

            Vector2 center = GenMapUI.LabelDrawPosFor(thing, worldOffsetZ);
            float size = Mathf.Clamp(UI.CurUICellSize() * 0.7f, 20f, 34f);
            Rect rect = new Rect(center.x - size / 2f, center.y - size / 2f, size, size);
            Color previous = GUI.color;
            GUI.color = Color.white;
            Widgets.DrawTextureFitted(rect, TexCommand.CannotShoot, 1f);
            GUI.color = previous;
        }

        private static void InterruptCurrentColonistAttacks(Pawn target)
        {
            Map map = target.Map;
            if (map == null)
            {
                return;
            }

            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                Job job = colonist.CurJob;
                if (job == null || !Targets(job, target))
                {
                    continue;
                }

                bool attackJob = job.def == JobDefOf.AttackMelee || job.def == JobDefOf.AttackStatic;
                bool violentVerb = job.verbToUse?.verbProps?.violent == true;
                if (attackJob || violentVerb)
                {
                    colonist.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
            }
        }

        private static bool Targets(Job job, Pawn target)
        {
            return job.targetA.Thing == target || job.targetB.Thing == target || job.targetC.Thing == target;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class PawnGetGizmosPatch
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!SpareMarkedEnemyUtility.IsEligibleEnemy(__instance))
            {
                return;
            }

            __result = AppendToggle(__result, __instance);
        }

        private static IEnumerable<Gizmo> AppendToggle(IEnumerable<Gizmo> original, Pawn pawn)
        {
            if (original != null)
            {
                foreach (Gizmo gizmo in original)
                {
                    yield return gizmo;
                }
            }

            bool marked = SpareMarkedEnemyUtility.IsMarked(pawn);
            yield return new Command_Toggle
            {
                defaultLabel = (marked ? "RWU_SpareEnemyUnmark" : "RWU_SpareEnemyMark").Translate(),
                defaultDesc = (marked ? "RWU_SpareEnemyUnmarkDesc" : "RWU_SpareEnemyMarkDesc").Translate(),
                icon = TexCommand.CannotShoot,
                isActive = () => SpareMarkedEnemyUtility.IsMarked(pawn),
                toggleAction = () => SpareMarkedEnemyUtility.Toggle(pawn)
            };
        }
    }

    [HarmonyPatch(typeof(PawnUIOverlay), nameof(PawnUIOverlay.DrawPawnGUIOverlay))]
    public static class PawnUiOverlayPatch
    {
        public static void Postfix(Pawn ___pawn)
        {
            if (!SpareMarkedEnemyUtility.HasPersistentMark(___pawn) || ___pawn.Dead)
            {
                return;
            }

            SpareMarkedEnemyUtility.DrawMarker(___pawn, 0.8f);
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.DrawGUIOverlay))]
    public static class CorpseUiOverlayPatch
    {
        public static void Postfix(Thing __instance)
        {
            Corpse corpse = __instance as Corpse;
            if (corpse == null || !SpareMarkedEnemyUtility.HasPersistentMark(corpse.InnerPawn))
            {
                return;
            }

            SpareMarkedEnemyUtility.DrawMarker(corpse, 0.45f);
        }
    }

    [HarmonyPatch(typeof(Corpse), nameof(Corpse.GetGizmos))]
    public static class CorpseGetGizmosPatch
    {
        public static void Postfix(Corpse __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance?.InnerPawn == null ||
                !SpareMarkedEnemyUtility.HasPersistentMark(__instance.InnerPawn))
            {
                return;
            }

            __result = AppendRemoveToggle(__result, __instance.InnerPawn);
        }

        private static IEnumerable<Gizmo> AppendRemoveToggle(IEnumerable<Gizmo> original, Pawn pawn)
        {
            if (original != null)
            {
                foreach (Gizmo gizmo in original)
                {
                    yield return gizmo;
                }
            }

            yield return new Command_Toggle
            {
                defaultLabel = "RWU_SpareEnemyUnmark".Translate(),
                defaultDesc = "RWU_SpareEnemyUnmarkCorpseDesc".Translate(),
                icon = TexCommand.CannotShoot,
                isActive = () => SpareMarkedEnemyUtility.HasPersistentMark(pawn),
                toggleAction = () => SpareMarkedEnemyUtility.RemoveMark(pawn)
            };
        }
    }

    [HarmonyPatch(typeof(AttackTargetFinder), nameof(AttackTargetFinder.BestAttackTarget))]
    public static class BestAttackTargetPatch
    {
        public static void Prefix(IAttackTargetSearcher searcher, ref Predicate<Thing> validator)
        {
            Pawn attacker = searcher?.Thing as Pawn;
            if (attacker == null || !attacker.IsColonistPlayerControlled)
            {
                return;
            }

            Predicate<Thing> original = validator;
            validator = candidate =>
                !SpareMarkedEnemyUtility.ShouldProtectFrom(attacker, candidate as Pawn) &&
                (original == null || original(candidate));
        }
    }

    [HarmonyPatch]
    public static class VerbTryStartCastOnPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            Type[] signature =
            {
                typeof(LocalTargetInfo),
                typeof(LocalTargetInfo),
                typeof(bool),
                typeof(bool),
                typeof(bool),
                typeof(bool)
            };

            yield return AccessTools.Method(typeof(Verb), nameof(Verb.TryStartCastOn), signature);
            yield return AccessTools.Method(typeof(Verb_ShootBeam), nameof(Verb.TryStartCastOn), signature);
            yield return AccessTools.Method(typeof(Verb_CastAbility), nameof(Verb.TryStartCastOn), signature);
        }

        public static bool Prefix(Verb __instance, LocalTargetInfo castTarg, ref bool __result)
        {
            if (!SpareMarkedEnemyUtility.ShouldBlockVerb(__instance, castTarg))
            {
                return true;
            }

            __instance.Reset();
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Verb), "TryCastNextBurstShot")]
    public static class VerbTryCastNextBurstShotPatch
    {
        public static bool Prefix(Verb __instance, LocalTargetInfo ___currentTarget)
        {
            if (!SpareMarkedEnemyUtility.ShouldBlockVerb(__instance, ___currentTarget))
            {
                return true;
            }

            __instance.Reset();
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class PawnPreApplyDamagePatch
    {
        public static bool Prefix(Pawn __instance, DamageInfo dinfo, ref bool absorbed)
        {
            if (!SpareMarkedEnemyUtility.ShouldProtectFrom(dinfo.Instigator as Pawn, __instance))
            {
                return true;
            }

            absorbed = true;
            return false;
        }
    }
}
