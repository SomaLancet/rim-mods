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
        // CompUsable initially validates the pawn receiving the order. The actual
        // recipient is chosen afterwards, so recipient validation lives on the
        // targetable comp instead.
        public override AcceptanceReport CanBeUsedBy(Pawn pawn)
        {
            return true;
        }

        public AcceptanceReport CanApplyTo(Pawn pawn)
        {
            return base.CanBeUsedBy(pawn);
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

    public sealed class CompProperties_TargetableColonist : CompProperties_Targetable
    {
        public CompProperties_TargetableColonist()
        {
            compClass = typeof(CompTargetableColonist);
        }
    }

    public sealed class CompTargetableColonist : CompTargetable
    {
        protected override bool PlayerChoosesTarget => true;

        protected override TargetingParameters GetTargetingParameters()
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

        public override IEnumerable<Thing> GetTargets(Thing targetChosenByPlayer = null)
        {
            if (targetChosenByPlayer != null)
            {
                yield return targetChosenByPlayer;
            }
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
            CompUsable usable = Genepack?.TryGetComp<CompUsable>();
            useDuration = usable?.Props.useDuration ?? 60;
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, 1, null, errorOnFailed) &&
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
            CompTargetableColonist targetable = genepack?.TryGetComp<CompTargetableColonist>();
            CompUsable usable = genepack?.TryGetComp<CompUsable>();

            if (targetable == null || usable == null || !targetable.IsValidRecipient(recipient))
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
