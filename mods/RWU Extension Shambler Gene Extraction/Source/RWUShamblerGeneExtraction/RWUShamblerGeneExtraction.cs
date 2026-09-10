using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RWUShamblerGeneExtraction
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        private static readonly string[] ExtractorTypeNames =
        {
            "als.BfG.Building_BfG_GeneExtractor",
            "GeneRipper.Building_GeneRipper"
        };

        static Bootstrap()
        {
            var harmony = new Harmony("dieruki.rwu.shamblergeneextraction");
            harmony.PatchAll();

            int patched = 0;
            foreach (string typeName in ExtractorTypeNames)
            {
                Type type = AccessTools.TypeByName(typeName);
                MethodInfo method = type == null ? null : AccessTools.Method(type, nameof(Building_Enterable.CanAcceptPawn));
                if (method == null)
                {
                    Log.Warning("[RWU Extension Shambler Gene Extraction] Could not find " + typeName + ".CanAcceptPawn.");
                    continue;
                }

                harmony.Patch(method, postfix: new HarmonyMethod(typeof(ExtractorAcceptancePatch), nameof(ExtractorAcceptancePatch.Postfix)));
                patched++;
            }

            Log.Message("[RWU Extension Shambler Gene Extraction] Loaded support for " + patched + " gene extractor types.");
        }
    }

    [DefOf]
    public static class RWUJobDefOf
    {
        public static JobDef RWU_CarryContainedShamblerToExtractor;

        static RWUJobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RWUJobDefOf));
        }
    }

    internal static class ShamblerExtractionUtility
    {
        internal const string CompactExtractorTypeName = "als.BfG.Building_BfG_GeneExtractor";
        internal const string GeneRipperTypeName = "GeneRipper.Building_GeneRipper";

        private static readonly FieldInfo GeneRipperSelectedGene = AccessTools.Field(AccessTools.TypeByName(GeneRipperTypeName), "selectedGene");
        private static readonly Type GeneRipperDialogType = AccessTools.TypeByName("GeneRipper.Dialog_SelectGene");
        private static readonly ConstructorInfo GeneRipperDialogConstructor = GeneRipperDialogType == null
            ? null
            : AccessTools.Constructor(GeneRipperDialogType, new[] { typeof(Pawn), typeof(Action<Pawn, GeneDef>), typeof(Action) });

        internal static bool IsLivingHumanlikeShambler(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
            {
                return false;
            }

            if (pawn.mutant != null && pawn.mutant.Def == MutantDefOf.Shambler)
            {
                return true;
            }

            string kindDefName = pawn.kindDef?.defName;
            return kindDefName == "ShamblerSwarmer" ||
                   kindDefName == "ShamblerSoldier" ||
                   kindDefName == "ShamblerGorehulk";
        }

        internal static bool IsSupportedExtractor(Building building)
        {
            string typeName = building?.GetType().FullName;
            return typeName == CompactExtractorTypeName || typeName == GeneRipperTypeName;
        }

        internal static bool IsGeneRipper(Building building)
        {
            return building?.GetType().FullName == GeneRipperTypeName;
        }

        internal static bool IsAuthorizedFor(Building_Enterable extractor, Pawn pawn)
        {
            if (extractor?.Map == null || pawn == null || extractor.SelectedPawn != pawn)
            {
                return false;
            }

            foreach (Pawn carrier in extractor.Map.mapPawns.FreeColonistsSpawned)
            {
                Job job = carrier.CurJob;
                if (carrier != pawn && job?.def == RWUJobDefOf.RWU_CarryContainedShamblerToExtractor &&
                    job.targetA.Thing == extractor && job.targetC.Thing == pawn)
                {
                    return true;
                }
            }

            return false;
        }

        internal static AcceptanceReport CanUseExtractor(Building_Enterable extractor, Pawn shambler)
        {
            if (extractor == null || extractor.Destroyed || !extractor.Spawned || !IsSupportedExtractor(extractor))
            {
                return false;
            }

            if (!IsLivingHumanlikeShambler(shambler) || shambler.genes == null)
            {
                return false;
            }

            if (extractor.SelectedPawn != null && extractor.SelectedPawn != shambler)
            {
                return "Occupied".Translate();
            }

            if (extractor.GetDirectlyHeldThings().Count > 0)
            {
                return "Occupied".Translate();
            }

            CompPowerTrader power = extractor.TryGetComp<CompPowerTrader>();
            if (power != null && !power.PowerOn)
            {
                return "NoPower".Translate().CapitalizeFirst();
            }

            if (QuestUtility.IsQuestLodger(shambler))
            {
                return false;
            }

            List<Gene> genes = shambler.genes.GenesListForReading;
            bool hasUsableGene = IsGeneRipper(extractor) ? genes.Any() : genes.Any(gene => gene.Active);
            if (!hasUsableGene)
            {
                return "PawnHasNoGenes".Translate(shambler.Named("PAWN"));
            }

            if (IsGeneRipper(extractor))
            {
                if (!shambler.ageTracker.Adult)
                {
                    return "GeneRipper_CantExtractFromChildren".Translate(shambler.Named("PAWN"));
                }

                if (shambler.health.hediffSet.HasHediff(HediffDefOf.XenogermReplicating))
                {
                    return "GeneRipper_CurrentlyRegenerating".Translate();
                }
            }
            else if (shambler.health.hediffSet.HasHediff(HediffDefOf.XenogerminationComa))
            {
                return "InXenogerminationComa".Translate();
            }

            return true;
        }

        internal static Pawn FindCarrier(Building_HoldingPlatform platform, Building_Enterable extractor)
        {
            Pawn target = platform?.HeldPawn;
            if (platform?.Map == null || target == null)
            {
                return null;
            }

            return platform.Map.mapPawns.FreeColonistsSpawned
                .Where(candidate => candidate != target && !candidate.Dead && !candidate.Downed &&
                    candidate.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) &&
                    candidate.CanReserveAndReach(platform, PathEndMode.InteractionCell, Danger.Deadly) &&
                    candidate.CanReserveAndReach(extractor, PathEndMode.InteractionCell, Danger.Deadly))
                .OrderBy(candidate => candidate.Position.DistanceToSquared(platform.Position))
                .FirstOrDefault();
        }

        internal static void BeginTransfer(Building_HoldingPlatform platform, Building_Enterable extractor, Pawn carrier)
        {
            Pawn shambler = platform?.HeldPawn;
            if (shambler == null || carrier == null || carrier == shambler)
            {
                return;
            }

            if (IsGeneRipper(extractor))
            {
                OpenGeneRipperDialog(platform, extractor, carrier, shambler);
                return;
            }

            StartTransferJob(platform, extractor, carrier, shambler);
        }

        private static void OpenGeneRipperDialog(Building_HoldingPlatform platform, Building_Enterable extractor, Pawn carrier, Pawn shambler)
        {
            if (GeneRipperDialogType == null || GeneRipperDialogConstructor == null || GeneRipperSelectedGene == null)
            {
                Log.Error("[RWU Extension Shambler Gene Extraction] Gene Ripper selection dialog was not found.");
                return;
            }

            Action<Pawn, GeneDef> accept = (pawn, gene) =>
            {
                if (pawn != shambler || gene == null || platform.HeldPawn != shambler)
                {
                    return;
                }

                GeneRipperSelectedGene.SetValue(extractor, gene);
                StartTransferJob(platform, extractor, carrier, shambler);
            };

            var dialog = GeneRipperDialogConstructor.Invoke(new object[] { shambler, accept, null }) as Window;
            if (dialog == null)
            {
                Log.Error("[RWU Extension Shambler Gene Extraction] Could not create the Gene Ripper selection dialog.");
                return;
            }

            Find.WindowStack.Add(dialog);
        }

        private static void StartTransferJob(Building_HoldingPlatform platform, Building_Enterable extractor, Pawn carrier, Pawn shambler)
        {
            AcceptanceReport report = CanUseExtractor(extractor, shambler);
            if (!report.Accepted || platform.HeldPawn != shambler || carrier == shambler)
            {
                if (extractor.SelectedPawn == shambler)
                {
                    extractor.SelectedPawn = null;
                }
                ClearGeneRipperSelection(extractor);
                Messages.Message("RWU_ShamblerTransferFailed".Translate(extractor.LabelCap), extractor, MessageTypeDefOf.RejectInput, false);
                return;
            }

            extractor.SelectedPawn = shambler;
            Job job = JobMaker.MakeJob(RWUJobDefOf.RWU_CarryContainedShamblerToExtractor, extractor, platform, shambler);
            job.playerForced = true;
            if (!carrier.jobs.TryTakeOrderedJob(job, JobTag.Misc))
            {
                extractor.SelectedPawn = null;
                ClearGeneRipperSelection(extractor);
                Messages.Message("RWU_ShamblerTransferFailed".Translate(extractor.LabelCap), extractor, MessageTypeDefOf.RejectInput, false);
            }
        }

        internal static void ClearGeneRipperSelection(Building_Enterable extractor)
        {
            if (IsGeneRipper(extractor))
            {
                GeneRipperSelectedGene?.SetValue(extractor, null);
            }
        }
    }

    public static class ExtractorAcceptancePatch
    {
        public static void Postfix(Building_Enterable __instance, Pawn pawn, ref AcceptanceReport __result)
        {
            if (__result.Accepted || !ShamblerExtractionUtility.IsAuthorizedFor(__instance, pawn))
            {
                return;
            }

            __result = ShamblerExtractionUtility.CanUseExtractor(__instance, pawn);
        }
    }

    [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.GetGizmos))]
    public static class HoldingPlatformGizmosPatch
    {
        public static void Postfix(ThingWithComps __instance, ref IEnumerable<Gizmo> __result)
        {
            var platform = __instance as Building_HoldingPlatform;
            if (platform == null || !ShamblerExtractionUtility.IsLivingHumanlikeShambler(platform.HeldPawn))
            {
                return;
            }

            __result = AppendCommand(__result, platform);
        }

        private static IEnumerable<Gizmo> AppendCommand(IEnumerable<Gizmo> original, Building_HoldingPlatform platform)
        {
            if (original != null)
            {
                foreach (Gizmo gizmo in original)
                {
                    yield return gizmo;
                }
            }

            var command = new Command_Action
            {
                defaultLabel = "RWU_ExtractShamblerGenes".Translate(),
                defaultDesc = "RWU_ExtractShamblerGenesDesc".Translate(),
                icon = TexCommand.ForbidOff,
                action = () => OpenExtractorMenu(platform)
            };

            if (!AvailableExtractors(platform).Any())
            {
                command.Disable("RWU_NoSupportedExtractor".Translate());
            }

            yield return command;
        }

        private static IEnumerable<Building_Enterable> AvailableExtractors(Building_HoldingPlatform platform)
        {
            if (platform?.Map == null)
            {
                yield break;
            }

            foreach (Building building in platform.Map.listerBuildings.allBuildingsColonist)
            {
                var extractor = building as Building_Enterable;
                if (extractor != null && ShamblerExtractionUtility.IsSupportedExtractor(extractor))
                {
                    yield return extractor;
                }
            }
        }

        private static void OpenExtractorMenu(Building_HoldingPlatform platform)
        {
            Pawn shambler = platform.HeldPawn;
            var options = new List<FloatMenuOption>();
            foreach (Building_Enterable extractor in AvailableExtractors(platform).OrderBy(building => building.LabelCap.ToString()))
            {
                AcceptanceReport report = ShamblerExtractionUtility.CanUseExtractor(extractor, shambler);
                Pawn carrier = report.Accepted ? ShamblerExtractionUtility.FindCarrier(platform, extractor) : null;
                string carrierLabel = carrier != null ? carrier.LabelShortCap : "-";
                string label = "RWU_CarryShamblerTo".Translate(extractor.LabelCap, carrierLabel);

                if (!report.Accepted)
                {
                    string reason = report.Reason.NullOrEmpty() ? "RWU_ExtractorUnavailable".Translate().ToString() : report.Reason;
                    options.Add(new FloatMenuOption(label + ": " + reason, null));
                    continue;
                }

                if (carrier == null)
                {
                    options.Add(new FloatMenuOption(label + ": " + "RWU_NoShamblerCarrier".Translate(extractor.LabelCap), null));
                    continue;
                }

                Building_Enterable chosenExtractor = extractor;
                Pawn chosenCarrier = carrier;
                options.Add(new FloatMenuOption(label, () => ShamblerExtractionUtility.BeginTransfer(platform, chosenExtractor, chosenCarrier), MenuOptionPriority.Default, null, extractor));
            }

            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption("RWU_NoSupportedExtractor".Translate(), null));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }
    }

    public sealed class JobDriver_CarryContainedShamblerToExtractor : JobDriver
    {
        private const TargetIndex ExtractorIndex = TargetIndex.A;
        private const TargetIndex PlatformIndex = TargetIndex.B;
        private const TargetIndex ShamblerIndex = TargetIndex.C;

        private Building_Enterable Extractor => job.GetTarget(ExtractorIndex).Thing as Building_Enterable;
        private Building_HoldingPlatform Platform => job.GetTarget(PlatformIndex).Thing as Building_HoldingPlatform;
        private Pawn Shambler => job.GetTarget(ShamblerIndex).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn != Shambler &&
                   pawn.Reserve(Platform, job, 1, -1, null, errorOnFailed) &&
                   pawn.Reserve(Extractor, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(_ => CleanupTransferState());
            this.FailOnDestroyedOrNull(ExtractorIndex);
            this.FailOnDestroyedOrNull(PlatformIndex);
            this.FailOn(() => pawn == Shambler || Shambler == null || Shambler.Dead ||
                !ShamblerExtractionUtility.CanUseExtractor(Extractor, Shambler).Accepted);

            yield return Toils_Goto.GotoThing(PlatformIndex, PathEndMode.InteractionCell);
            yield return Toils_General.Do(() =>
            {
                if (Platform.HeldPawn != Shambler)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                Platform.EjectContents();
                if (!Shambler.Spawned || !pawn.Reserve(Shambler, job, 1, -1, null, false))
                {
                    EndJobWith(JobCondition.Incompletable);
                }
            });
            yield return Toils_Goto.GotoThing(ShamblerIndex, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(ShamblerIndex, false, false, false, true, false);
            yield return Toils_Goto.GotoThing(ExtractorIndex, PathEndMode.InteractionCell);
            yield return Toils_General.WaitWith(ExtractorIndex, 60, true, false, false, ShamblerIndex);
            yield return Toils_General.Do(() =>
            {
                Extractor.TryAcceptPawn(Shambler);
                if (!Extractor.GetDirectlyHeldThings().Contains(Shambler))
                {
                    Messages.Message("RWU_ShamblerTransferFailed".Translate(Extractor.LabelCap), Extractor, MessageTypeDefOf.RejectInput, false);
                    EndJobWith(JobCondition.Incompletable);
                }
            });
        }

        private void CleanupTransferState()
        {
            Building_Enterable extractor = Extractor;
            Pawn shambler = Shambler;
            bool accepted = extractor != null && extractor.GetDirectlyHeldThings().Contains(shambler);
            if (!accepted && extractor != null && extractor.SelectedPawn == shambler)
            {
                extractor.SelectedPawn = null;
                ShamblerExtractionUtility.ClearGeneRipperSelection(extractor);
            }

        }
    }
}
