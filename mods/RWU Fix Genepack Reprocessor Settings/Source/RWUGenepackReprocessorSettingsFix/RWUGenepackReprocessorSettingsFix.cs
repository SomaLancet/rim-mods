using System;
using GenepackReprocessor;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RWUGenepackReprocessorSettingsFix
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        private const string TargetPackageId = "anonemous2.genepackreprocessor";

        static Bootstrap()
        {
            if (!ModsConfig.IsActive(TargetPackageId))
            {
                Log.Message("[RWU Fix Genepack Reprocessor Settings] Target mod is not active; skipping.");
                return;
            }

            var target = AccessTools.Method(
                typeof(GenepackImprovMod),
                "DrawOptions_Consumption",
                new[]
                {
                    typeof(Listing_Custom),
                    typeof(Rect),
                    typeof(int).MakeByRefType(),
                    typeof(string).MakeByRefType(),
                    typeof(int).MakeByRefType(),
                    typeof(string).MakeByRefType(),
                    typeof(bool),
                    typeof(bool).MakeByRefType(),
                    typeof(bool),
                    typeof(bool).MakeByRefType()
                });

            if (target == null)
            {
                Log.Warning("[RWU Fix Genepack Reprocessor Settings] DrawOptions_Consumption was not found; skipping.");
                return;
            }

            var prefix = AccessTools.Method(
                typeof(SettingsLayoutPatch),
                nameof(SettingsLayoutPatch.DrawOptionsConsumptionPrefix));

            new Harmony("dieruki.rwu.genepackreprocessorsettingsfix").Patch(
                target,
                prefix: new HarmonyMethod(prefix));

            Log.Message("[RWU Fix Genepack Reprocessor Settings] Patched consumption settings layout.");
        }
    }

    public static class SettingsLayoutPatch
    {
        private const float LabelPart = 0.7f;
        private const float FieldOffset = 0.1f;
        private const float OneRowHeight = 1.1f;
        private const float TwoRowsHeight = 2.133f;
        private const float ThreeRowsHeight = 3.3f;

        public static bool DrawOptionsConsumptionPrefix(
            Listing_Custom parent,
            Rect inRect,
            ref int neutroAmount,
            ref string bufferNeutroAmount,
            ref int neutroComplexity,
            ref string bufferNeutroComplexity,
            bool canRequireArchite,
            ref bool consumesArchite,
            bool canConsumePacks,
            ref bool consumesPacks)
        {
            float size = OneRowHeight;
            if (canRequireArchite || canConsumePacks)
            {
                size = TwoRowsHeight;
            }
            if (canRequireArchite && canConsumePacks)
            {
                size = ThreeRowsHeight;
            }

            var subSection = parent.BeginSection(Text.LineHeight * size, width: inRect.width);
            var line = subSection.GetRectLine();

            subSection.NGTextFieldNumericLabeled(
                line,
                3,
                0,
                "GeneR_NeutroamineBase".Translate(),
                ref neutroAmount,
                ref bufferNeutroAmount,
                0f,
                150f,
                LabelPart,
                FieldOffset,
                "GeneR_NeutroamineBaseHelp".Translate());

            subSection.NGTextFieldNumericLabeled(
                line,
                3,
                1,
                "GeneR_NeutroamineComp".Translate(),
                ref neutroComplexity,
                ref bufferNeutroComplexity,
                0f,
                150f,
                LabelPart,
                FieldOffset,
                "GeneR_NeutroamineCompHelp".Translate());

            if (canRequireArchite)
            {
                subSection.Gap();
                line = subSection.GetRectLine();
                subSection.NGCheckboxLabeled(
                    line,
                    3,
                    0,
                    "GeneR_ArchiteCapsulesSet".Translate(),
                    ref consumesArchite,
                    FieldOffset,
                    "GeneR_ArchiteCapsulesSetHelp".Translate());
            }

            if (canConsumePacks)
            {
                subSection.Gap();
                line = subSection.GetRectLine();
                subSection.NGCheckboxLabeled(
                    line,
                    3,
                    0,
                    "GeneR_GenepackConsume".Translate(),
                    ref consumesPacks,
                    FieldOffset,
                    "GeneR_GenepackConsumeHelp".Translate());
            }

            parent.EndSection(subSection);
            return false;
        }
    }
}
