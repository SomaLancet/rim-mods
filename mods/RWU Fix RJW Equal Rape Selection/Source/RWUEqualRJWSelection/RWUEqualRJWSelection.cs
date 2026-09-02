using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using rjw;

namespace RWUEqualRJWSelection
{
    public sealed class JobGiver_EqualRapeSelection : ThinkNode_JobGiver
    {
        private enum Branch
        {
            Comfort,
            RandomRape,
            Necrophilia,
            Bestiality
        }

        private sealed class BestialityJobGiver : JobGiver_Bestiality
        {
            public Job TryFor(Pawn pawn)
            {
                return base.TryGiveJob(pawn);
            }
        }

        private static readonly BestialityJobGiver Bestiality = new BestialityJobGiver();

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null || pawn.IsAnimal())
            {
                return null;
            }

            bool isRapist = xxx.is_rapist(pawn);
            bool isNecrophiliac = xxx.is_necrophiliac(pawn);
            bool isZoophile = xxx.is_zoophile(pawn);

            // Leave ordinary pawns on RJW's original CP cadence. The router is for
            // pawns whose traits currently make multiple forced-sex branches compete.
            if (!isRapist && !isNecrophiliac && !isZoophile)
            {
                return null;
            }

            var remaining = new List<Branch>(4)
            {
                Branch.Comfort,
                Branch.RandomRape,
                Branch.Necrophilia,
                Branch.Bestiality
            };

            while (remaining.Count > 0)
            {
                int index = Rand.Range(0, remaining.Count);
                Branch selected = remaining[index];
                remaining.RemoveAt(index);

                Job job = TryBranch(pawn, selected, isRapist, isNecrophiliac, isZoophile);
                if (job != null)
                {
                    DebugMessage(pawn, selected, job);
                    return job;
                }

                DebugUnavailable(pawn, selected, remaining.Count);
            }

            return null;
        }

        private static Job TryBranch(
            Pawn pawn,
            Branch branch,
            bool isRapist,
            bool isNecrophiliac,
            bool isZoophile)
        {
            switch (branch)
            {
                case Branch.Comfort:
                    return TryComfort(pawn);
                case Branch.RandomRape:
                    return isRapist ? TryRandomRape(pawn) : null;
                case Branch.Necrophilia:
                    return isNecrophiliac ? TryNecrophilia(pawn) : null;
                case Branch.Bestiality:
                    return isZoophile ? TryBestiality(pawn) : null;
                default:
                    return null;
            }
        }

        private static bool HasFreeWill(Pawn pawn)
        {
            return RJWSettings.designated_freewill
                || (!pawn.IsDesignatedComfort() && !pawn.IsDesignatedBreeding());
        }

        private static Job TryComfort(Pawn pawn)
        {
            if (!RJWSettings.rape_enabled || !HasFreeWill(pawn))
            {
                return null;
            }

            if (pawn.HostileTo(Faction.OfPlayer))
            {
                return null;
            }

            if (!RJWSettings.colonist_CP_rape && pawn.IsColonist && xxx.is_human(pawn))
            {
                return null;
            }

            if (!RJWSettings.visitor_CP_rape && pawn.Faction != null
                && !pawn.Faction.IsPlayer && xxx.is_human(pawn))
            {
                return null;
            }

            if (!pawn.HasParts() || pawn.Drafted || !xxx.can_rape(pawn))
            {
                return null;
            }

            if (!RJWSettings.WildMode)
            {
                if (!xxx.is_healthy(pawn) || pawn.IsDesignatedComfort()
                    || (!SexUtility.ReadyForLovin(pawn) && !xxx.is_frustrated(pawn)))
                {
                    return null;
                }
            }

            if (pawn.Faction == null || (!pawn.Faction.IsPlayer && !pawn.IsPrisonerOfColony))
            {
                return null;
            }

            Pawn target = JobGiver_ComfortPrisonerRape.FindComfortPrisoner(pawn);
            if (target == null)
            {
                return null;
            }

            JobDef jobDef = xxx.is_animal(target) ? xxx.bestiality : xxx.RapeCP;
            SexUtility.IncreaseTicksToNextHookup(pawn);
            return JobMaker.MakeJob(jobDef, target);
        }

        private static Job TryRandomRape(Pawn pawn)
        {
            if (!RJWSettings.rape_enabled || !HasFreeWill(pawn)
                || !xxx.IsSingleOrPartnersNotHere(pawn) || !xxx.can_rape(pawn))
            {
                return null;
            }

            HediffDef cooldown = HediffDef.Named("Hediff_RapeEnemyCD");
            if (pawn.health.hediffSet.HasHediff(cooldown))
            {
                return null;
            }

            Pawn target = new JobGiver_RandomRape().FindVictim(pawn);
            if (target == null)
            {
                return null;
            }

            // Preserve RJW's cooldown, but only after a real target was found.
            pawn.health.AddHediff(cooldown, null, null, null);
            SexUtility.IncreaseTicksToNextHookup(pawn);
            return JobMaker.MakeJob(xxx.RapeRandom, target);
        }

        private static Job TryNecrophilia(Pawn pawn)
        {
            if (!RJWSettings.necrophilia_enabled || !HasFreeWill(pawn)
                || pawn.Drafted || !pawn.HasParts())
            {
                return null;
            }

            if (!SexUtility.ReadyForLovin(pawn) && !xxx.is_hornyorfrustrated(pawn))
            {
                return null;
            }

            if (!xxx.can_rape(pawn))
            {
                return null;
            }

            Corpse target = JobGiver_ViolateCorpse.find_corpse(pawn, pawn.Map);
            if (target == null)
            {
                return null;
            }

            SexUtility.IncreaseTicksToNextHookup(pawn);
            return JobMaker.MakeJob(xxx.RapeCorpse, target);
        }

        private static Job TryBestiality(Pawn pawn)
        {
            if (!RJWSettings.bestiality_enabled || !HasFreeWill(pawn))
            {
                return null;
            }

            return Bestiality.TryFor(pawn);
        }

        private static void DebugMessage(Pawn pawn, Branch branch, Job job)
        {
            if (!RJWSettings.DebugRape)
            {
                return;
            }

            Log.Message("[RWU Equal Rape Selection] " + xxx.get_pawnname(pawn)
                + ": selected " + branch + ", job=" + job.def.defName);
        }

        private static void DebugUnavailable(Pawn pawn, Branch branch, int remaining)
        {
            if (!RJWSettings.DebugRape)
            {
                return;
            }

            Log.Message("[RWU Equal Rape Selection] " + xxx.get_pawnname(pawn)
                + ": " + branch + " unavailable, rerolling between " + remaining + " branches");
        }
    }
}
