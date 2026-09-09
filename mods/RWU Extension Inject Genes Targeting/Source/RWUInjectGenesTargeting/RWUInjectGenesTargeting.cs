using System.Collections.Generic;
using GeneInjector;
using RimWorld;
using Verse;
using Verse.AI;

namespace RWUInjectGenesTargeting
{
    public sealed class CompProperties_TargetEffect_GeneInjectionTargeted : CompProperties_TargetEffect_GeneInjectionT
    {
        public CompProperties_TargetEffect_GeneInjectionTargeted()
        {
            compClass = typeof(CompTargetEffect_GeneInjectionTargeted);
        }
    }

    public sealed class CompTargetEffect_GeneInjectionTargeted : CompTargetEffect_GeneInjector
    {
        public AcceptanceReport CanApplyTo(Pawn pawn)
        {
            if (pawn == null || pawn.genes == null)
            {
                return false;
            }

            if (pawn.IsColonistPlayerControlled)
            {
                return base.CanBeUsedBy(pawn);
            }

            if (!pawn.IsPrisonerOfColony && !pawn.IsSlaveOfColony)
            {
                return false;
            }

            Genepack genepack = parent as Genepack;
            if (genepack?.GeneSet == null)
            {
                return false;
            }

            foreach (GeneDef geneDef in genepack.GeneSet.GenesListForReading)
            {
#pragma warning disable CS0618 // Match Inject Genes' duplicate-gene check on RimWorld 1.6.
                if (pawn.genes.HasGene(geneDef))
#pragma warning restore CS0618
                {
                    return false;
                }
            }

            return true;
        }

        public override void DoEffect(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            // Keep the upstream implementation unchanged for its original case.
            if (pawn.IsColonistPlayerControlled)
            {
                base.DoEffect(pawn);
                return;
            }

            // Inject Genes exits immediately for prisoners and slaves. Reproduce
            // its remaining logic only for player-owned recipients added here.
            if (!pawn.IsPrisonerOfColony && !pawn.IsSlaveOfColony)
            {
                return;
            }

            Genepack genepack = parent as Genepack;
            if (genepack?.GeneSet == null || pawn.genes == null)
            {
                return;
            }

            foreach (GeneDef geneDef in genepack.GeneSet.GenesListForReading)
            {
#pragma warning disable CS0618 // Match Inject Genes' own duplicate-gene check on RimWorld 1.6.
                if (!pawn.genes.HasGene(geneDef))
#pragma warning restore CS0618
                {
                    pawn.genes.AddGene(geneDef, !global::Settings.useEndogenes);
                }
            }

            pawn.health.AddHediff(HediffDefOf.XenogerminationComa);
        }
    }

    public sealed class CompProperties_ApplyGenepackToTarget : CompProperties
    {
        public int useDuration = 60;

        public CompProperties_ApplyGenepackToTarget()
        {
            compClass = typeof(CompApplyGenepackToTarget);
        }
    }

    public sealed class CompApplyGenepackToTarget : ThingComp
    {
        public CompProperties_ApplyGenepackToTarget Props =>
            (CompProperties_ApplyGenepackToTarget)props;

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selectedPawn)
        {
            if (selectedPawn == null || !selectedPawn.IsColonistPlayerControlled)
            {
                yield break;
            }

            string label = "RWU_ApplyGenepackToTarget".Translate();
            if (!selectedPawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) ||
                !selectedPawn.CanReserveAndReach(parent, PathEndMode.ClosestTouch, Danger.Deadly, 1, 1))
            {
                yield return new FloatMenuOption(
                    "RWU_ApplyGenepackToTargetUnavailable".Translate(),
                    null);
                yield break;
            }

            yield return new FloatMenuOption(label, delegate
            {
                Find.Targeter.BeginTargeting(
                    GetTargetingParameters(),
                    target => TryStartTargetedJob(selectedPawn, target.Pawn),
                    selectedPawn);
            });
        }

        private TargetingParameters GetTargetingParameters()
        {
            return new TargetingParameters
            {
                canTargetPawns = true,
                canTargetSelf = true,
                canTargetItems = false,
                canTargetBuildings = false,
                mapObjectTargetsMustBeAutoAttackable = false,
                validator = target => IsValidRecipient(target.Thing as Pawn)
            };
        }

        public bool IsValidRecipient(Pawn target)
        {
            if (target == null || !target.Spawned || target.Dead || !target.RaceProps.Humanlike ||
                target.genes == null ||
                (!target.IsColonistPlayerControlled && !target.IsPrisonerOfColony && !target.IsSlaveOfColony))
            {
                return false;
            }

            CompTargetEffect_GeneInjectionTargeted effect =
                parent.TryGetComp<CompTargetEffect_GeneInjectionTargeted>();
            return effect != null && effect.CanApplyTo(target).Accepted;
        }

        private void TryStartTargetedJob(Pawn selectedPawn, Pawn recipient)
        {
            if (!IsValidRecipient(recipient))
            {
                Messages.Message(
                    "RWU_InjectGenesTargetBecameInvalid".Translate(recipient?.LabelShortCap ?? "?"),
                    recipient,
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            Job job = JobMaker.MakeJob(
                DefDatabase<JobDef>.GetNamed("RWU_ApplyGenepackToPawn"),
                parent,
                recipient);
            job.count = 1;
            selectedPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }

    public sealed class JobDriver_ApplyGenepackToPawn : JobDriver
    {
        private const TargetIndex GenepackIndex = TargetIndex.A;
        private const TargetIndex RecipientIndex = TargetIndex.B;
        private int useDuration;

        private Thing Genepack => job.GetTarget(GenepackIndex).Thing;
        private Pawn Recipient => job.GetTarget(RecipientIndex).Pawn;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref useDuration, "useDuration", 60);
        }

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            CompApplyGenepackToTarget targetedUse =
                Genepack?.TryGetComp<CompApplyGenepackToTarget>();
            useDuration = targetedUse?.Props.useDuration ?? 60;
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(job.targetA, job, 1, 1, null, errorOnFailed))
            {
                return false;
            }

            return Recipient == pawn ||
                   pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            job.count = 1;
            this.FailOnIncapable(PawnCapacityDefOf.Manipulation);
            this.FailOnDestroyedNullOrForbidden(GenepackIndex);
            this.FailOnDestroyedOrNull(RecipientIndex);

            yield return Toils_Goto.GotoThing(GenepackIndex, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(GenepackIndex);
            yield return Toils_Haul.StartCarryThing(GenepackIndex, false, true, false, true);
            yield return Toils_Goto.GotoThing(RecipientIndex, PathEndMode.Touch)
                .FailOnDespawnedOrNull(RecipientIndex);

            Toil wait = Toils_General.Wait(useDuration, RecipientIndex)
                .WithProgressBarToilDelay(RecipientIndex)
                .FailOnDespawnedOrNull(RecipientIndex)
                .FailOnCannotTouch(RecipientIndex, PathEndMode.Touch);
            yield return wait;

            yield return Toils_General.Do(ApplyGenepack);
        }

        private void ApplyGenepack()
        {
            Thing genepack = Genepack;
            Pawn recipient = Recipient;
            CompApplyGenepackToTarget targetedUse =
                genepack?.TryGetComp<CompApplyGenepackToTarget>();
            CompUsable usable = genepack?.TryGetComp<CompUsable>();

            if (targetedUse == null || usable == null || !targetedUse.IsValidRecipient(recipient))
            {
                Messages.Message(
                    "RWU_InjectGenesTargetBecameInvalid".Translate(recipient?.LabelShortCap ?? "?"),
                    recipient,
                    MessageTypeDefOf.RejectInput,
                    false);
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            usable.UsedBy(recipient);
        }
    }
}
