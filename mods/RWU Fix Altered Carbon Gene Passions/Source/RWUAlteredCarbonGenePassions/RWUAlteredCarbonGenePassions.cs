using System;
using System.Collections.Generic;
using System.Reflection;
using AlteredCarbon;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RWUAlteredCarbonGenePassions
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        private const string LogPrefix = "[RWU Fix Altered Carbon Gene Passions] ";

        static Bootstrap()
        {
            try
            {
                var harmony = new Harmony("dieruki.rwu.alteredcarbongenepassions");
                var copyFromPawn = AccessTools.Method(
                    typeof(NeuralData),
                    nameof(NeuralData.CopyFromPawn),
                    new[] { typeof(Pawn), typeof(ThingDef), typeof(bool), typeof(bool) });
                var overwritePawn = AccessTools.Method(
                    typeof(NeuralData),
                    nameof(NeuralData.OverwritePawn),
                    new[] { typeof(Pawn), typeof(bool) });

                if (copyFromPawn == null || overwritePawn == null)
                {
                    Log.Error(LogPrefix + "Altered Carbon 1.6 methods were not found; no patches were applied.");
                    return;
                }

                harmony.Patch(
                    copyFromPawn,
                    postfix: new HarmonyMethod(typeof(GenePassionPatches), nameof(GenePassionPatches.CopyFromPawnPostfix)));
                harmony.Patch(
                    overwritePawn,
                    postfix: new HarmonyMethod(typeof(GenePassionPatches), nameof(GenePassionPatches.OverwritePawnPostfix)));

                PatchOptionalUi(harmony);
            }
            catch (Exception ex)
            {
                Log.Error(LogPrefix + "Initialization failed; no further patching was attempted: " + ex);
            }
        }

        private static void PatchOptionalUi(Harmony harmony)
        {
            var skillRow = AccessTools.Method(
                typeof(Window_StackEditor),
                "<DrawSkillsPanel>b__60_0",
                new[] { typeof(Rect), typeof(SkillRecord) });
            if (skillRow != null)
            {
                harmony.Patch(
                    skillRow,
                    prefix: new HarmonyMethod(typeof(GenePassionPatches), nameof(GenePassionPatches.StackEditorSkillRowPrefix)),
                    postfix: new HarmonyMethod(typeof(GenePassionPatches), nameof(GenePassionPatches.StackEditorSkillRowPostfix)),
                    finalizer: new HarmonyMethod(typeof(GenePassionPatches), nameof(GenePassionPatches.StackEditorSkillRowFinalizer)));
            }
            else
            {
                Log.Warning(LogPrefix + "Altered Carbon stack editor skill row was not found; the save-safe core fix remains active, but the editor will show base passions.");
            }

            var infoCard = AccessTools.Method(
                typeof(Dialog_InfoCardStack),
                nameof(Dialog_InfoCardStack.DoWindowContents),
                new[] { typeof(Rect) });
            if (infoCard != null)
            {
                harmony.Patch(
                    infoCard,
                    prefix: new HarmonyMethod(typeof(GenePassionPatches), nameof(GenePassionPatches.StackInfoCardPrefix)),
                    postfix: new HarmonyMethod(typeof(GenePassionPatches), nameof(GenePassionPatches.StackInfoCardPostfix)),
                    finalizer: new HarmonyMethod(typeof(GenePassionPatches), nameof(GenePassionPatches.StackInfoCardFinalizer)));
            }
            else
            {
                Log.Warning(LogPrefix + "Altered Carbon stack info card was not found; the save-safe core fix remains active, but the matrix card will show base passions.");
            }
        }
    }

    public static class GenePassionPatches
    {
        private const string LogPrefix = "[RWU Fix Altered Carbon Gene Passions] ";
        private static readonly HashSet<string> DuplicateWarnings = new HashSet<string>(StringComparer.Ordinal);
        private static readonly FieldInfo StackEditorNeuralDataField = AccessTools.Field(typeof(Window_StackEditor), "neuralData");
        private static readonly FieldInfo InfoCardThingField = AccessTools.Field(typeof(Dialog_InfoCard), "thing");

        public sealed class SkillRowState
        {
            public SkillRecord Skill;
            public Passion BasePassion;
            public Passion DisplayedPassion;
            public bool Restored;
        }

        public sealed class InfoCardState
        {
            public readonly List<TemporaryPassion> Passions = new List<TemporaryPassion>();
            public bool Restored;
        }

        public struct TemporaryPassion
        {
            public SkillRecord Skill;
            public Passion Passion;
        }

        public static void CopyFromPawnPostfix(NeuralData __instance, Pawn pawn)
        {
            if (!IsEligiblePlayerPawn(pawn) || __instance?.skills == null || pawn.skills?.skills == null)
            {
                return;
            }

            try
            {
                var genes = GetUniqueActivePassionGenes(pawn);
                foreach (var pair in genes)
                {
                    var skillDef = pair.Key;
                    var gene = pair.Value;
                    var liveSkill = pawn.skills.GetSkill(skillDef);
                    var storedSkill = FindSkill(__instance.skills, skillDef);
                    if (liveSkill == null || storedSkill == null)
                    {
                        continue;
                    }

                    var oldPreAdd = gene.passionPreAdd;
                    var correctedBase = ResolveBasePassion(gene, liveSkill.passion);
                    var wouldChange = storedSkill.passion != correctedBase || oldPreAdd != correctedBase;

                    if (!wouldChange)
                    {
                        continue;
                    }

                    storedSkill.passion = correctedBase;
                    gene.passionPreAdd = correctedBase;
                }
            }
            catch (Exception ex)
            {
                Log.Error(LogPrefix + "CopyFromPawn correction failed closed for " + PawnLabel(pawn) + ": " + ex);
            }
        }

        public static void OverwritePawnPostfix(NeuralData __instance, Pawn pawn)
        {
            if (!IsEligiblePlayerPawn(pawn) || __instance?.skills == null || pawn.skills?.skills == null)
            {
                return;
            }

            try
            {
                var genes = GetUniqueActivePassionGenes(pawn);
                foreach (var pair in genes)
                {
                    var skillDef = pair.Key;
                    var gene = pair.Value;
                    var liveSkill = pawn.skills.GetSkill(skillDef);
                    var storedSkill = FindSkill(__instance.skills, skillDef);
                    if (liveSkill == null || storedSkill == null)
                    {
                        continue;
                    }

                    var basePassion = storedSkill.passion;
                    var oldPreAdd = gene.passionPreAdd;
                    var expectedEffective = ApplyPassionMod(gene.def.passionMod, basePassion);
                    var wouldChange = liveSkill.passion != expectedEffective || oldPreAdd != basePassion;

                    if (!wouldChange)
                    {
                        continue;
                    }

                    gene.passionPreAdd = basePassion;
                    liveSkill.passion = expectedEffective;
                }
            }
            catch (Exception ex)
            {
                Log.Error(LogPrefix + "OverwritePawn correction failed closed for " + PawnLabel(pawn) + ": " + ex);
            }
        }

        public static void StackEditorSkillRowPrefix(
            Window_StackEditor __instance,
            SkillRecord __1,
            out SkillRowState __state)
        {
            __state = null;
            try
            {
                var data = StackEditorNeuralDataField?.GetValue(__instance) as NeuralData;
                var skill = __1;
                var dummy = data?.DummyPawn;
                if (!IsEligiblePlayerData(data, dummy) || skill?.def == null)
                {
                    return;
                }

                Gene gene;
                if (!GetUniqueActivePassionGenes(dummy).TryGetValue(skill.def, out gene)
                    || gene.def.passionMod.modType != PassionMod.PassionModType.AddOneLevel)
                {
                    return;
                }

                var basePassion = skill.passion;
                var displayedPassion = ApplyPassionMod(gene.def.passionMod, basePassion);
                __state = new SkillRowState
                {
                    Skill = skill,
                    BasePassion = basePassion,
                    DisplayedPassion = displayedPassion
                };
                skill.passion = displayedPassion;
            }
            catch (Exception ex)
            {
                RestoreSkillRow(__state);
                Log.Error(LogPrefix + "Stack editor display correction failed closed: " + ex);
            }
        }

        public static void StackEditorSkillRowPostfix(SkillRowState __state)
        {
            if (__state == null || __state.Skill == null)
            {
                return;
            }

            var selectedPassion = __state.Skill.passion;
            __state.Skill.passion = selectedPassion == __state.DisplayedPassion
                ? __state.BasePassion
                : RemoveOnePassionLevel(selectedPassion);
            __state.Restored = true;
        }

        public static Exception StackEditorSkillRowFinalizer(Exception __exception, SkillRowState __state)
        {
            RestoreSkillRow(__state);
            return __exception;
        }

        public static void StackInfoCardPrefix(Dialog_InfoCardStack __instance, out InfoCardState __state)
        {
            __state = null;
            try
            {
                var thing = InfoCardThingField?.GetValue(__instance) as ThingWithNeuralData;
                var data = thing?.NeuralData;
                var dummy = data?.DummyPawn;
                if (!IsEligiblePlayerData(data, dummy) || data.skills == null || dummy.skills?.skills == null)
                {
                    return;
                }

                var genes = GetUniqueActivePassionGenes(dummy);
                var state = new InfoCardState();
                __state = state;
                foreach (var pair in genes)
                {
                    var gene = pair.Value;
                    if (gene.def.passionMod.modType != PassionMod.PassionModType.AddOneLevel)
                    {
                        continue;
                    }

                    var storedSkill = FindSkill(data.skills, pair.Key);
                    var dummySkill = dummy.skills.GetSkill(pair.Key);
                    if (storedSkill == null || dummySkill == null)
                    {
                        continue;
                    }

                    state.Passions.Add(new TemporaryPassion
                    {
                        Skill = dummySkill,
                        Passion = dummySkill.passion
                    });
                    dummySkill.passion = ApplyPassionMod(gene.def.passionMod, storedSkill.passion);
                }

                if (state.Passions.Count == 0)
                {
                    __state = null;
                }
            }
            catch (Exception ex)
            {
                RestoreInfoCard(__state);
                Log.Error(LogPrefix + "Stack info card display correction failed closed: " + ex);
            }
        }

        public static void StackInfoCardPostfix(InfoCardState __state)
        {
            RestoreInfoCard(__state);
        }

        public static Exception StackInfoCardFinalizer(Exception __exception, InfoCardState __state)
        {
            RestoreInfoCard(__state);
            return __exception;
        }

        private static Dictionary<SkillDef, Gene> GetUniqueActivePassionGenes(Pawn pawn)
        {
            var result = new Dictionary<SkillDef, Gene>();
            var ambiguous = new HashSet<SkillDef>();
            var allGenes = pawn.genes?.GenesListForReading;
            if (allGenes == null)
            {
                return result;
            }

            foreach (var gene in allGenes)
            {
                var passionMod = gene?.def?.passionMod;
                var skill = passionMod?.skill;
                if (gene == null || !gene.Active || skill == null || ambiguous.Contains(skill))
                {
                    continue;
                }

                if (result.ContainsKey(skill))
                {
                    result.Remove(skill);
                    ambiguous.Add(skill);
                    var warningKey = pawn.thingIDNumber + ":" + skill.defName;
                    if (DuplicateWarnings.Add(warningKey))
                    {
                        Log.Warning(
                            LogPrefix + "Skipped " + PawnLabel(pawn) + "/" + skill.defName
                            + " because multiple active passion genes target the same skill.");
                    }
                    continue;
                }

                result.Add(skill, gene);
            }

            return result;
        }

        private static Passion ResolveBasePassion(Gene gene, Passion livePassion)
        {
            var mod = gene.def.passionMod;
            if (gene.passionPreAdd.HasValue
                && ApplyPassionMod(mod, gene.passionPreAdd.Value) == livePassion)
            {
                return gene.passionPreAdd.Value;
            }

            switch (mod.modType)
            {
                case PassionMod.PassionModType.AddOneLevel:
                    if (livePassion == Passion.Major)
                    {
                        return Passion.Minor;
                    }

                    if (livePassion == Passion.Minor)
                    {
                        return Passion.None;
                    }

                    return livePassion;

                case PassionMod.PassionModType.DropAll:
                    if (livePassion != Passion.None)
                    {
                        return livePassion;
                    }

                    return gene.passionPreAdd ?? Passion.None;

                default:
                    return livePassion;
            }
        }

        private static Passion ApplyPassionMod(PassionMod mod, Passion basePassion)
        {
            switch (mod.modType)
            {
                case PassionMod.PassionModType.AddOneLevel:
                    if (basePassion == Passion.None)
                    {
                        return Passion.Minor;
                    }

                    if (basePassion == Passion.Minor)
                    {
                        return Passion.Major;
                    }

                    return basePassion;

                case PassionMod.PassionModType.DropAll:
                    return Passion.None;

                default:
                    return basePassion;
            }
        }

        private static Passion RemoveOnePassionLevel(Passion effectivePassion)
        {
            if (effectivePassion == Passion.Minor)
            {
                return Passion.None;
            }

            if (effectivePassion == Passion.Major)
            {
                return Passion.Minor;
            }

            return effectivePassion;
        }

        private static void RestoreSkillRow(SkillRowState state)
        {
            if (state == null || state.Restored || state.Skill == null)
            {
                return;
            }

            state.Skill.passion = state.BasePassion;
            state.Restored = true;
        }

        private static void RestoreInfoCard(InfoCardState state)
        {
            if (state == null || state.Restored)
            {
                return;
            }

            foreach (var temporary in state.Passions)
            {
                if (temporary.Skill != null)
                {
                    temporary.Skill.passion = temporary.Passion;
                }
            }

            state.Restored = true;
        }

        private static SkillRecord FindSkill(List<SkillRecord> skills, SkillDef def)
        {
            SkillRecord found = null;
            foreach (var skill in skills)
            {
                if (skill?.def != def)
                {
                    continue;
                }

                if (found != null)
                {
                    return null;
                }

                found = skill;
            }

            return found;
        }

        private static bool IsEligiblePlayerPawn(Pawn pawn)
        {
            return pawn != null
                && !pawn.Destroyed
                && pawn.RaceProps?.Humanlike == true
                && pawn.Faction == Faction.OfPlayer;
        }

        private static bool IsEligiblePlayerData(NeuralData data, Pawn dummy)
        {
            return data != null
                && data.Faction == Faction.OfPlayer
                && dummy?.RaceProps?.Humanlike == true;
        }

        private static string PawnLabel(Pawn pawn)
        {
            return pawn == null ? "<null>" : pawn.LabelShort + " (Thing_Human" + pawn.thingIDNumber + ")";
        }
    }
}
