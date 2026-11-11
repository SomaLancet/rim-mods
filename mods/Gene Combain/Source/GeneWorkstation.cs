using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RWUGeneWorkstation
{
    public enum Procedure { Random, Rip, Remove }

    public sealed class Building_GeneWorkstation : Building_Enterable, IThingHolderWithDrawnPawn
    {
        private const int ProcedureTicks = 30000;
        private static readonly float[] GeneCountWeights = { 0.70f, 0.20f, 0.08f, 0.02f };
        private static readonly Procedure[] Procedures = { Procedure.Random, Procedure.Rip, Procedure.Remove };

        private Thing donor;
        private GeneDef geneDef;
        private bool xenogene;
        private Procedure procedure;
        private Pawn executor;
        private int ticksRemaining;
        private int unpoweredTicks;
        private Effecter progressBar;
        private MaterialPropertyBlock silhouetteProperties;

        private bool Powered => this.TryGetComp<CompPowerTrader>().PowerOn;
        private bool Loaded => donor != null && innerContainer.Contains(donor);
        public override bool IsContentsSuspended => false;
        public override Vector3 PawnDrawOffset => Vector3.zero;
        public float HeldPawnDrawPos_Y => DrawPos.y + 0.03658537f;
        public float HeldPawnBodyAngle => Rotation.Opposite.AsAngle;
        public PawnPosture HeldPawnPosture => PawnPosture.LayingOnGroundFaceUp;

        private static bool Researched(string defName) =>
            DefDatabase<ResearchProjectDef>.GetNamedSilentFail(defName)?.IsFinished == true;

        private static bool Unlocked(Procedure mode) =>
            mode == Procedure.Random || Researched(mode == Procedure.Rip ? "GeneProcessor" : "Archogenetics");

        private static Pawn DonorPawn(Thing target) => (target as Corpse)?.InnerPawn ?? target as Pawn;

        private static bool IsShambler(Pawn pawn)
        {
            string kind = pawn.kindDef?.defName;
            return pawn.mutant?.Def?.defName == "Shambler" ||
                kind == "ShamblerSwarmer" || kind == "ShamblerSoldier" || kind == "ShamblerGorehulk";
        }

        private static bool IsColonyPawn(Pawn pawn) =>
            pawn.IsColonist || pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony;

        private static bool InMetabolicRange(int value) =>
            value >= GeneTuning.BiostatRange.min && value <= GeneTuning.BiostatRange.max;

        private static IEnumerable<Gene> Genes(Pawn pawn, Procedure mode) =>
            pawn.genes.GenesListForReading.Where(gene => mode != Procedure.Random ||
                (gene.def.biostatArc == 0 && gene.def.endogeneCategory != EndogeneCategory.Melanin &&
                    InMetabolicRange(gene.def.biostatMet)));

        private static string RejectReason(Thing target, Procedure mode, Building_HoldingPlatform platform = null)
        {
            Pawn pawn = DonorPawn(target);
            if (target == null || target.Destroyed || pawn?.genes == null || !pawn.RaceProps.Humanlike)
                return "RWUGW_NoGenes";
            if (target is Corpse corpse)
            {
                if (mode != Procedure.Rip || corpse.GetRotStage() != RotStage.Fresh)
                    return "RWUGW_NotFresh";
                if (Current.Game.GetComponent<SampledCorpses>().Contains(corpse))
                    return "RWUGW_Sampled";
            }
            else
            {
                if (pawn.Dead || QuestUtility.IsQuestLodger(pawn))
                    return "RWUGW_Unavailable";
                if (IsShambler(pawn))
                {
                    if (!ModsConfig.AnomalyActive || !Researched("EntityContainment") || mode == Procedure.Remove)
                        return "RWUGW_Unavailable";
                }
                else if (!IsColonyPawn(pawn) || platform != null)
                    return "RWUGW_Unavailable";

                if (mode == Procedure.Rip && !pawn.ageTracker.Adult)
                    return "RWUGW_Adult";
                if (pawn.health.hediffSet.HasHediff(HediffDefOf.XenogerminationComa) ||
                    (mode != Procedure.Random && pawn.health.hediffSet.HasHediff(HediffDefOf.XenogermReplicating)))
                    return "RWUGW_Recovery";
            }
            return Genes(pawn, mode).Any() ? null : "RWUGW_NoGenes";
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
                yield return gizmo;
            if (Faction != Faction.OfPlayer)
                yield break;
            if (donor != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "RWUGW_Cancel".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel"),
                    action = Cancel
                };
                yield break;
            }
            foreach (Procedure mode in Procedures.Where(Unlocked))
            {
                var command = new Command_Action
                {
                    defaultLabel = ("RWUGW_" + mode).Translate(),
                    defaultDesc = ("RWUGW_" + mode + "Desc").Translate(),
                    icon = def.uiIcon,
                    action = () => OpenDonors(mode)
                };
                if (!Powered)
                    command.Disable("NoPower".Translate());
                yield return command;
            }
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn pawn)
        {
            foreach (FloatMenuOption option in base.GetFloatMenuOptions(pawn))
                yield return option;
            if (Faction != Faction.OfPlayer || !IsColonyPawn(pawn) || pawn.Downed || IsShambler(pawn))
                yield break;
            foreach (Procedure mode in Procedures.Where(Unlocked))
            {
                string reason = donor != null ? "Occupied" : !Powered ? "NoPower" : RejectReason(pawn, mode);
                if (reason == null && !pawn.CanReserveAndReach(this, PathEndMode.InteractionCell, Danger.Deadly))
                    reason = "NoPath";
                string label = ("RWUGW_Enter" + mode).Translate();
                yield return reason == null ? new FloatMenuOption(label, () => ChooseGene(pawn, mode, null, pawn), def)
                    : new FloatMenuOption(label + " — " + reason.Translate(), null);
            }
        }

        private void OpenDonors(Procedure mode)
        {
            IEnumerable<(Thing target, Building_HoldingPlatform platform)> donors = Map.mapPawns.AllPawnsSpawned
                .Where(pawn => IsColonyPawn(pawn) && !IsShambler(pawn))
                .Select(pawn => (target: (Thing)pawn, platform: (Building_HoldingPlatform)null));

            if (mode == Procedure.Rip)
                donors = donors.Concat(Map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse)
                    .OfType<Corpse>().Where(body => body.Spawned && body.InnerPawn.RaceProps.Humanlike &&
                        body.GetRotStage() == RotStage.Fresh)
                    .Select(body => (target: (Thing)body, platform: (Building_HoldingPlatform)null)));

            if (mode != Procedure.Remove && ModsConfig.AnomalyActive && Researched("EntityContainment"))
                donors = donors.Concat(Map.listerBuildings.allBuildingsColonist
                    .OfType<Building_HoldingPlatform>().Where(platform => platform.HeldPawn != null &&
                        IsShambler(platform.HeldPawn))
                    .Select(platform => (target: (Thing)platform.HeldPawn, platform)));

            var options = new List<FloatMenuOption>();
            foreach (var entry in donors.OrderBy(entry => DonorPawn(entry.target).LabelShort))
            {
                Pawn pawn = DonorPawn(entry.target);
                string name = pawn.LabelShortCap + ", " + pawn.genes?.XenotypeLabel;
                if (entry.target is Corpse)
                    name = "RWUGW_CorpseLabel".Translate(name);
                else if (entry.platform != null)
                    name = "RWUGW_ShamblerLabel".Translate(name);

                string reason = RejectReason(entry.target, mode, entry.platform);
                if (reason == null && FindExecutor(entry.target, entry.platform) == null)
                    reason = "RWUGW_NoCarrier";
                options.Add(new FloatMenuOption(reason == null ? name : name + " — " + reason.Translate().ToString(),
                    reason == null ? (Action)(() => ChooseGene(entry.target, mode, entry.platform, null)) : null,
                    pawn, Color.white));
            }

            if (options.Count == 0)
                options.Add(new FloatMenuOption("RWUGW_NoDonors".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ChooseGene(Thing target, Procedure mode, Building_HoldingPlatform platform, Pawn self)
        {
            Pawn pawn = DonorPawn(target);
            string reason = RejectReason(target, mode, platform);
            if (reason != null)
            {
                Reject(reason);
                return;
            }
            if (mode == Procedure.Random)
            {
                if (pawn.health.hediffSet.HasHediff(HediffDefOf.XenogermReplicating))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "RWUGW_ConfirmRecoveryRisk".Translate(pawn.LabelShort),
                        () => Queue(target, mode, null, platform, self), true));
                else
                    Queue(target, mode, null, platform, self);
                return;
            }

            var options = new List<FloatMenuOption>();
            foreach (var group in Genes(pawn, mode).GroupBy(gene => pawn.genes.Xenogenes.Contains(gene)))
            {
                options.Add(new FloatMenuOption((group.Key ? "RWUGW_Xenogenes" : "RWUGW_Endogenes").Translate(), null));
                foreach (Gene gene in group.OrderBy(gene => gene.def.label))
                {
                    options.Add(new FloatMenuOption(gene.def.LabelCap, () =>
                    {
                        if (mode == Procedure.Rip && target is Pawn)
                            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                                "RWUGW_ConfirmDeath".Translate(pawn.LabelShort),
                                () => Queue(target, mode, gene, platform, self), true));
                        else
                            Queue(target, mode, gene, platform, self);
                    }, gene.def.Icon, Color.white));
                }
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private Pawn FindExecutor(Thing target, Building_HoldingPlatform platform)
        {
            if (platform == null && target is Pawn pawn && !pawn.Downed &&
                (pawn.IsColonist || pawn.IsSlaveOfColony) &&
                pawn.CanReserveAndReach(this, PathEndMode.InteractionCell, Danger.Deadly))
                return pawn;

            Thing pickup = (Thing)platform ?? target;
            PathEndMode pathEnd = platform != null ? PathEndMode.InteractionCell : PathEndMode.ClosestTouch;
            return Map.mapPawns.FreeColonistsSpawned.FirstOrDefault(carrier =>
                carrier != target && !carrier.Downed && !carrier.InMentalState &&
                carrier.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) &&
                carrier.CanReserveAndReach(pickup, pathEnd, Danger.Deadly) &&
                carrier.CanReserveAndReach(this, PathEndMode.InteractionCell, Danger.Deadly));
        }

        private void Queue(Thing target, Procedure mode, Gene gene, Building_HoldingPlatform platform, Pawn self)
        {
            if (!Spawned || donor != null || innerContainer.Count != 0)
            {
                Reject("Occupied");
                return;
            }
            if (!Powered)
            {
                Reject("NoPower");
                return;
            }
            string reason = RejectReason(target, mode, platform);
            if (reason == null && (!Unlocked(mode) ||
                (platform == null ? target.Map != Map : platform.Map != Map || platform.HeldPawn != target) ||
                (mode != Procedure.Random && !DonorPawn(target).genes.GenesListForReading.Contains(gene))))
                reason = "RWUGW_Unavailable";
            if (reason != null)
            {
                Reject(reason);
                return;
            }
            Pawn worker = self ?? FindExecutor(target, platform);
            if (worker == null || worker.Downed || !worker.CanReserveAndReach(this, PathEndMode.InteractionCell, Danger.Deadly))
            {
                Reject("RWUGW_NoCarrier");
                return;
            }

            donor = target;
            procedure = mode;
            geneDef = gene?.def;
            xenogene = gene != null && DonorPawn(target).genes.Xenogenes.Contains(gene);
            executor = worker;
            SelectedPawn = target as Pawn;
            Job job = worker == target
                ? JobMaker.MakeJob(JobDefOf.EnterBuilding, this)
                : JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("RWU_LoadGeneWorkstation"), this, target, platform);
            if (!worker.jobs.TryTakeOrderedJob(job))
                ClearOrder();
        }

        public override AcceptanceReport CanAcceptPawn(Pawn pawn)
        {
            if (donor != pawn || Loaded)
                return false;
            if (!Powered)
                return "NoPower".Translate();
            string reason = RejectReason(pawn, procedure);
            return reason == null ? (AcceptanceReport)true : (AcceptanceReport)reason.Translate();
        }

        public override void TryAcceptPawn(Pawn pawn) => TryLoad(pawn);

        internal bool IsOrderFor(Pawn worker, Thing target) => donor == target && executor == worker && !Loaded;

        internal bool TryLoad(Thing target)
        {
            if (!Powered || donor != target || innerContainer.Count != 0 || RejectReason(target, procedure) != null)
                return false;
            if (target.Spawned)
                target.DeSpawn();
            if (!innerContainer.TryAddOrTransfer(target))
            {
                if (target.holdingOwner == null)
                    GenPlace.TryPlaceThing(target, InteractionCell, Map, ThingPlaceMode.Near);
                return false;
            }
            startTick = Find.TickManager.TicksGame;
            ticksRemaining = ProcedureTicks;
            executor = null;
            return true;
        }

        protected override void Tick()
        {
            base.Tick();
            if (innerContainer.Count > 0 && (!Loaded || ticksRemaining == 0))
            {
                Cancel();
                return;
            }
            if (donor == null)
                return;
            if (!Loaded)
            {
                Job job = executor?.CurJob;
                if (job == null || job.targetA.Thing != this ||
                    (job.def != JobDefOf.EnterBuilding && job.def.defName != "RWU_LoadGeneWorkstation"))
                    ClearOrder();
                return;
            }
            if (RejectReason(donor, procedure) != null)
            {
                Cancel();
                Reject("RWUGW_Aborted");
                return;
            }
            TickProcedureEffects();
            if (!Powered)
            {
                if (++unpoweredTicks >= 60000)
                {
                    Cancel();
                    Reject("RWUGW_Aborted");
                }
                return;
            }
            unpoweredTicks = 0;
            if (ticksRemaining > 0 && --ticksRemaining == 0)
                FinishProcedure();
        }

        public override void DynamicDrawPhaseAt(DrawPhase phase, Vector3 drawLoc, bool flip = false)
        {
            base.DynamicDrawPhaseAt(phase, drawLoc, flip);
            if (!Loaded)
                return;

            Pawn pawn = DonorPawn(donor);
            if (pawn == null)
                return;

            Vector3 patientPosition = drawLoc + PawnDrawOffset;
            PawnRenderer renderer = pawn.Drawer.renderer;
            renderer.DynamicDrawPhaseAt(phase, patientPosition, null, true);
            if (phase == DrawPhase.Draw && Powered)
                DrawPatientPulse(pawn, renderer, patientPosition);
        }

        private void DrawPatientPulse(Pawn pawn, PawnRenderer renderer, Vector3 patientPosition)
        {
            const int pulseInterval = 160;
            ThingDef effect = ThingDefOf.Mote_Deathresting;
            float age = ((ProcedureTicks - ticksRemaining) % pulseInterval) / 60f;
            var timing = effect.mote;
            float opacity = age < timing.fadeInTime
                ? age / timing.fadeInTime
                : 1f - Mathf.Clamp01((age - timing.fadeInTime - timing.solidTime) / timing.fadeOutTime);
            if (opacity <= 0f)
                return;

            Rot4 facing = renderer.LayingFacing();
            Quaternion angle = Quaternion.AngleAxis(renderer.BodyAngle(PawnRenderFlags.None), Vector3.up);
            patientPosition.y = effect.Altitude;
            Color color = effect.graphicData.color;
            color.a *= opacity;
            DrawPulseMesh(renderer.BodyGraphic, HumanlikeMeshPoolUtility.GetHumanlikeBodySetForPawn(pawn).MeshAt(facing),
                facing, patientPosition, patientPosition, angle, color, age, effect.graphicData);
            Vector3 headPosition = patientPosition + angle * renderer.BaseHeadOffsetAt(facing);
            headPosition.y = patientPosition.y + 0.001f;
            DrawPulseMesh(renderer.HeadGraphic, HumanlikeMeshPoolUtility.GetHumanlikeHeadSetForPawn(pawn).MeshAt(facing),
                facing, headPosition, patientPosition, angle, color, age, effect.graphicData);
        }

        private void DrawPulseMesh(Graphic graphic, Mesh mesh, Rot4 facing, Vector3 position, Vector3 patientPosition,
            Quaternion angle, Color color, float age, GraphicData effectGraphic)
        {
            if (graphic == null)
                return;

            Material material = MaterialPool.MatFrom(new MaterialRequest
            {
                mainTex = graphic.MatAt(facing).mainTexture,
                shader = effectGraphic.shaderType.Shader,
                color = effectGraphic.color
            });
            if (silhouetteProperties == null)
                silhouetteProperties = new MaterialPropertyBlock();

            Vector3 size = mesh.bounds.size;
            silhouetteProperties.SetVector(ShaderPropertyIDs.PawnCenterWorld, new Vector4(patientPosition.x, patientPosition.z, 0, 0));
            silhouetteProperties.SetVector(ShaderPropertyIDs.PawnDrawSizeWorld, new Vector4(size.x, size.z, 0, 0));
            silhouetteProperties.SetFloat(ShaderPropertyIDs.AgeSecs, age);
            silhouetteProperties.SetColor(ShaderPropertyIDs.Color, color);
            GenDraw.DrawMeshNowOrLater(mesh, Matrix4x4.TRS(position, angle, Vector3.one), material, false,
                silhouetteProperties);
        }

        private void TickProcedureEffects()
        {
            if (progressBar == null)
                progressBar = EffecterDefOf.ProgressBarAlwaysVisible.Spawn();

            var barTarget = new TargetInfo(Position + IntVec3.North.RotatedBy(Rotation), Map);
            progressBar.EffectTick(barTarget, TargetInfo.Invalid);
            var bar = ((SubEffecter_ProgressBar)progressBar.children[0]).mote;
            if (bar != null)
                bar.progress = Mathf.Clamp01(1f - ticksRemaining / (float)ProcedureTicks);
        }

        private void ClearProcedureEffects()
        {
            progressBar?.Cleanup();
            progressBar = null;
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            ClearProcedureEffects();
            base.DeSpawn(mode);
        }

        private void FinishProcedure()
        {
            Pawn pawn = DonorPawn(donor);
            Corpse corpse = donor as Corpse;
            Gene selected = geneDef == null ? null : (xenogene ? pawn.genes.Xenogenes : pawn.genes.Endogenes)
                .FirstOrDefault(gene => gene.def == geneDef);
            if (procedure != Procedure.Random && selected == null)
            {
                Cancel();
                Reject("RWUGW_Aborted");
                return;
            }

            if (!ReleaseContents())
            {
                ticksRemaining = 1;
                return;
            }

            if (procedure == Procedure.Remove)
            {
                pawn.genes.RemoveGene(selected);
                Hediff recovery = HediffMaker.MakeHediff(HediffDefOf.XenogermReplicating, pawn);
                recovery.TryGetComp<HediffComp_Disappears>().ticksToDisappear =
                    Math.Max(1, 6 + 2 * geneDef.biostatMet) * 60000;
                pawn.health.AddHediff(recovery);
                Messages.Message("RWUGW_Removed".Translate(geneDef.LabelCap, pawn.LabelShort), this,
                    MessageTypeDefOf.PositiveEvent);
                Cancel();
                return;
            }

            List<GeneDef> extracted = procedure == Procedure.Random ? PickRandomGenes(pawn) : new List<GeneDef> { geneDef };
            if (extracted.Count == 0)
            {
                Cancel();
                Reject("RWUGW_Aborted");
                return;
            }

            var pack = (Genepack)ThingMaker.MakeThing(ThingDefOf.Genepack);
            pack.Initialize(extracted);
            if (!GenPlace.TryPlaceThing(pack, InteractionCell, Map, ThingPlaceMode.Near))
            {
                pack.Destroy();
                Cancel();
                Reject("RWUGW_Aborted");
                return;
            }

            if (corpse != null)
                Current.Game.GetComponent<SampledCorpses>().Mark(corpse);
            else if (procedure == Procedure.Random)
                GeneUtility.ExtractXenogerm(pawn,
                    Mathf.RoundToInt(GeneTuning.GeneExtractorRegrowingDurationDaysRange.RandomInRange * 60000f));
            else
                pawn.Kill(null);
            ClearOrder();
            Messages.Message("RWUGW_Extracted".Translate(string.Join(", ", extracted.Select(gene => gene.label))),
                pack, MessageTypeDefOf.PositiveEvent);
        }

        private List<GeneDef> PickRandomGenes(Pawn pawn)
        {
            var picked = new List<GeneDef>();
            var candidates = Genes(pawn, Procedure.Random).Select(gene => gene.def).Distinct().ToList();
            Rand.PushState(pawn.thingIDNumber ^ startTick);
            try
            {
                int count = Enumerable.Range(1, GeneCountWeights.Length)
                    .RandomElementByWeight(number => GeneCountWeights[number - 1]);
                int metabolism = 0;
                while (picked.Count < count && candidates.TryRandomElementByWeight(gene =>
                    InMetabolicRange(metabolism + gene.biostatMet)
                        ? (gene.biostatCpx > 0 ? 3f : 1f) : 0f, out GeneDef next))
                {
                    picked.Add(next);
                    candidates.Remove(next);
                    metabolism += next.biostatMet;
                }
            }
            finally
            {
                Rand.PopState();
            }
            return picked;
        }

        private void Cancel()
        {
            Pawn worker = executor;
            bool stopWorker = worker?.CurJob?.targetA.Thing == this &&
                (worker.CurJob.def == JobDefOf.EnterBuilding || worker.CurJob.def.defName == "RWU_LoadGeneWorkstation");
            ticksRemaining = 0;
            if (!ReleaseContents())
                return;
            ClearOrder();
            if (stopWorker)
                worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
        }

        private bool ReleaseContents()
        {
            if (innerContainer.Count > 0)
                innerContainer.TryDropAll(InteractionCell, Map, ThingPlaceMode.Near);
            return innerContainer.Count == 0;
        }

        private void ClearOrder()
        {
            ClearProcedureEffects();
            donor = null;
            geneDef = null;
            executor = null;
            SelectedPawn = null;
            startTick = -1;
            ticksRemaining = 0;
            unpoweredTicks = 0;
        }

        private void Reject(string key) => Messages.Message(key.Translate(), this, MessageTypeDefOf.RejectInput, false);

        public override string GetInspectString()
        {
            string text = base.GetInspectString();
            if (donor != null)
                text += (text.NullOrEmpty() ? "" : "\n") + (Loaded
                    ? (string)"RWUGW_Progress".Translate(DonorPawn(donor).LabelShort, ticksRemaining.ToStringTicksToPeriod())
                    : "RWUGW_Waiting".Translate(DonorPawn(donor).LabelShort).ToString());
            return text;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref donor, "donor");
            Scribe_References.Look(ref executor, "executor");
            Scribe_Defs.Look(ref geneDef, "geneDef");
            Scribe_Values.Look(ref procedure, "procedure");
            Scribe_Values.Look(ref xenogene, "xenogene");
            Scribe_Values.Look(ref ticksRemaining, "procedureTicks");
            Scribe_Values.Look(ref unpoweredTicks, "unpoweredTicks");
        }
    }

    public sealed class JobDriver_LoadGeneWorkstation : JobDriver
    {
        private Building_GeneWorkstation Station => job.targetA.Thing as Building_GeneWorkstation;
        private Thing Donor => job.targetB.Thing;
        private Building_HoldingPlatform Platform => job.targetC.Thing as Building_HoldingPlatform;

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn != Donor && pawn.Reserve(Station, job, 1, -1, null, errorOnFailed) &&
            pawn.Reserve((Thing)Platform ?? Donor, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);
            this.FailOn(() => !Station.IsOrderFor(pawn, Donor));
            if (Platform != null)
            {
                this.FailOnDestroyedOrNull(TargetIndex.C);
                yield return Toils_Goto.GotoThing(TargetIndex.C, PathEndMode.InteractionCell);
                yield return Toils_General.Do(() =>
                {
                    if (Platform.HeldPawn != Donor)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    Platform.EjectContents();
                    if (!Donor.Spawned || !pawn.Reserve(Donor, job))
                        EndJobWith(JobCondition.Incompletable);
                });
            }
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.B, false, false, false, true, false);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            yield return Toils_General.Do(() =>
            {
                if (pawn.carryTracker.CarriedThing != Donor || !Station.TryLoad(Donor))
                    EndJobWith(JobCondition.Incompletable);
            });
        }
    }

    public sealed class SampledCorpses : GameComponent
    {
        private HashSet<Corpse> sampled = new HashSet<Corpse>();

        public SampledCorpses(Game game) { }
        public bool Contains(Corpse corpse) => sampled.Contains(corpse);
        public void Mark(Corpse corpse) => sampled.Add(corpse);

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
                sampled.RemoveWhere(corpse => corpse == null || corpse.Destroyed);
            Scribe_Collections.Look(ref sampled, "rwuSampledCorpses", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && sampled == null)
                sampled = new HashSet<Corpse>();
        }
    }
}
