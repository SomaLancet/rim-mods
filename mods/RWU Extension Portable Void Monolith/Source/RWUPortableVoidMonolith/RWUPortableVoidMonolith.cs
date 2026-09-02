using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace RWUPortableVoidMonolith
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Bootstrap()
        {
            var harmony = new Harmony("dieruki.rwu.portablevoidmonolith");
            harmony.PatchAll();
            VoidUniverseCompatibility.Apply(harmony);
            Log.Message("[RWU Extension Portable Void Monolith] Loaded vanilla carrying, gravship, rotation, and quest continuity support.");
        }
    }

    public sealed class PortableMonolithState : GameComponent
    {
        private Building_VoidMonolith monolith;

        public PortableMonolithState(Game game)
        {
        }

        public Building_VoidMonolith Monolith => monolith != null && !monolith.Destroyed ? monolith : null;

        public void Remember(Building_VoidMonolith value)
        {
            if (value == null || value.Destroyed)
            {
                return;
            }

            if (monolith == null || monolith.Destroyed || monolith == value)
            {
                monolith = value;
            }
        }

        public override void ExposeData()
        {
            Scribe_References.Look(ref monolith, "portableVoidMonolith");
        }

        public override void LoadedGame()
        {
            LongEventHandler.ExecuteWhenFinished(CloseRedundantMigrationQuests);
        }

        private static void CloseRedundantMigrationQuests()
        {
            if (!MonolithUtility.HasPreservedMonolith() || Find.QuestManager == null)
            {
                return;
            }

            foreach (var quest in Find.QuestManager.QuestsListForReading
                .Where(candidate => candidate.root?.defName == "MonolithMigration" && !candidate.Historical)
                .ToList())
            {
                quest.End(QuestEndOutcome.Success, false, false);
                Log.Message("[RWU Extension Portable Void Monolith] Closed a redundant monolith migration quest because the original monolith is still present.");
            }
        }
    }

    internal static class MonolithUtility
    {
        private static readonly FieldInfo AttachmentsField = AccessTools.Field(typeof(Building_VoidMonolith), "monolithAttachments");
        private static readonly FieldInfo QuestMonolithField = AccessTools.Field(typeof(QuestPart_MonolithPart), "monolith");

        public static List<Thing> Attachments(Building_VoidMonolith monolith)
        {
            return AttachmentsField.GetValue(monolith) as List<Thing>;
        }

        public static Building_VoidMonolith QuestMonolith(QuestPart_MonolithPart part)
        {
            return QuestMonolithField.GetValue(part) as Building_VoidMonolith;
        }

        public static bool HasPreservedMonolith()
        {
            return FindPreservedMonolith() != null;
        }

        public static Building_VoidMonolith FindPreservedMonolith()
        {
            var state = Current.Game?.GetComponent<PortableMonolithState>();
            if (IsPortable(state?.Monolith))
            {
                return state.Monolith;
            }

            var anomalyMonolith = Find.Anomaly?.monolith;
            if (IsPortable(anomalyMonolith))
            {
                state?.Remember(anomalyMonolith);
                return anomalyMonolith;
            }

            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    foreach (var minified in map.listerThings.AllThings.OfType<MinifiedThing>())
                    {
                        if (minified.InnerThing is Building_VoidMonolith packed && !packed.Destroyed)
                        {
                            state?.Remember(packed);
                            return packed;
                        }
                    }
                }
            }

            var questMonolith = Find.QuestManager?.QuestsListForReading
                .SelectMany(quest => quest.PartsListForReading)
                .OfType<QuestPart_MonolithPart>()
                .Select(QuestMonolith)
                .FirstOrDefault(IsPortable);
            if (questMonolith != null)
            {
                state?.Remember(questMonolith);
            }
            return questMonolith;
        }

        public static bool IsPortable(Building_VoidMonolith monolith)
        {
            if (monolith == null || monolith.Destroyed)
            {
                return false;
            }

            if (monolith.Spawned || monolith.ParentHolder is MinifiedThing)
            {
                return true;
            }

            return Find.WorldObjects?.AllWorldObjects
                .OfType<Gravship>()
                .Any(gravship => gravship.ContainsThing(monolith)) == true;
        }

        public static void Remember(Building_VoidMonolith monolith)
        {
            Current.Game?.GetComponent<PortableMonolithState>()?.Remember(monolith);
        }

        public static void MarkAsDiscovered(Building_VoidMonolith monolith)
        {
            var proximityLetter = monolith?.TryGetComp<CompProximityLetter>();
            if (proximityLetter != null)
            {
                proximityLetter.letterSent = true;
            }
        }

        public static void RemoveAttachments(Building_VoidMonolith monolith)
        {
            var attachments = Attachments(monolith);
            if (attachments == null)
            {
                return;
            }

            Thing.allowDestroyNonDestroyable = true;
            try
            {
                foreach (var attachment in attachments)
                {
                    if (attachment != null && !attachment.Destroyed)
                    {
                        attachment.Destroy(DestroyMode.Vanish);
                    }
                }
                attachments.Clear();
            }
            finally
            {
                Thing.allowDestroyNonDestroyable = false;
            }
        }

        public static IEnumerable<CellRect> AttachmentRects(IntVec3 center, Rot4 rotation)
        {
            var level = Find.Anomaly?.LevelDef;
            if (level?.attachments == null)
            {
                yield break;
            }

            foreach (var attachment in level.attachments)
            {
                var offset = attachment.offset.ToIntVec3.RotatedBy(rotation);
                yield return GenAdj.OccupiedRect(center + offset, rotation, attachment.def.Size);
            }
        }
    }

    [HarmonyPatch(typeof(MinifyUtility), nameof(MinifyUtility.MakeMinified))]
    public static class MinifyUtilityMakeMinifiedPatch
    {
        public static void Prefix(Thing thing)
        {
            if (thing is Building_VoidMonolith monolith)
            {
                MonolithUtility.Remember(monolith);
                MonolithUtility.MarkAsDiscovered(monolith);
                MonolithUtility.RemoveAttachments(monolith);
            }
        }
    }

    [HarmonyPatch(typeof(Building_VoidMonolith), nameof(Building_VoidMonolith.SpawnSetup))]
    public static class BuildingVoidMonolithSpawnSetupPatch
    {
        public static void Prefix(Building_VoidMonolith __instance, out bool __state)
        {
            __state = Current.Game?.GetComponent<PortableMonolithState>()?.Monolith == __instance;
        }

        public static void Postfix(Building_VoidMonolith __instance, bool __state)
        {
            if (__state)
            {
                MonolithUtility.MarkAsDiscovered(__instance);
            }
            MonolithUtility.Remember(__instance);
        }
    }

    [HarmonyPatch(typeof(GameComponent_Anomaly), nameof(GameComponent_Anomaly.GameComponentTick))]
    public static class GameComponentAnomalyTickPatch
    {
        public static void Postfix(GameComponent_Anomaly __instance)
        {
            if (__instance.monolith == null)
            {
                __instance.monolith = MonolithUtility.FindPreservedMonolith();
            }
        }
    }

    [HarmonyPatch]
    public static class BuildingVoidMonolithUpdateAttachmentsPatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Building_VoidMonolith), "UpdateAttachments");
        }

        public static bool Prefix(Building_VoidMonolith __instance)
        {
            if (__instance.Rotation == Rot4.North)
            {
                return true;
            }

            var map = __instance.Map;
            var level = Find.Anomaly?.LevelDef;
            if (map == null || level == null)
            {
                return true;
            }

            MonolithUtility.RemoveAttachments(__instance);
            if (level.attachments == null)
            {
                return false;
            }

            var attachments = MonolithUtility.Attachments(__instance);
            var supportingTerrain = map.Biome.TerrainForAffordance(__instance.def.terrainAffordanceNeeded);
            foreach (var descriptor in level.attachments)
            {
                var offset = descriptor.offset.ToIntVec3.RotatedBy(__instance.Rotation);
                var position = __instance.Position + offset;
                var rect = GenAdj.OccupiedRect(position, __instance.Rotation, descriptor.def.Size);
                foreach (var cell in rect)
                {
                    if (!cell.GetAffordances(map).Contains(__instance.def.terrainAffordanceNeeded))
                    {
                        map.terrainGrid.RemoveTopLayer(cell, false);
                        map.terrainGrid.SetTerrain(cell, supportingTerrain);
                    }
                }

                var thing = ThingMaker.MakeThing(descriptor.def);
                thing.TryGetComp<CompSelectProxy>().thingToSelect = __instance;
                GenSpawn.Spawn(thing, position, map, __instance.Rotation, WipeMode.VanishOrMoveAside);
                thing.overrideGraphicIndex = descriptor.graphicIndex;
                thing.DirtyMapMesh(map);
                attachments.Add(thing);
            }
            return false;
        }
    }

    public sealed class PlaceWorker_PortableVoidMonolith : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            foreach (var rect in MonolithUtility.AttachmentRects(loc, rot))
            {
                foreach (var cell in rect)
                {
                    if (!cell.InBounds(map))
                    {
                        return "RWU_PVM_AttachmentsOutOfBounds".Translate();
                    }

                    foreach (var existing in cell.GetThingList(map))
                    {
                        if (existing != thingToIgnore && existing.def.category == ThingCategory.Building)
                        {
                            return "RWU_PVM_AttachmentsBlocked".Translate(existing.LabelCap);
                        }
                    }
                }
            }
            return AcceptanceReport.WasAccepted;
        }
    }

    [HarmonyPatch(typeof(QuestPart_QuestEnd), nameof(QuestPart_QuestEnd.Notify_QuestSignalReceived))]
    public static class QuestPartQuestEndPatch
    {
        public static bool Prefix(QuestPart_QuestEnd __instance, Signal signal)
        {
            if (signal.tag != __instance.inSignal || !signal.tag.EndsWith(".Destroyed", StringComparison.Ordinal))
            {
                return true;
            }

            var monolithPart = __instance.quest?.PartsListForReading.OfType<QuestPart_MonolithPart>().FirstOrDefault();
            var monolith = monolithPart == null ? null : MonolithUtility.QuestMonolith(monolithPart);
            if (monolith == null || monolith.Destroyed || monolith.Spawned || monolith != MonolithUtility.FindPreservedMonolith())
            {
                return true;
            }

            Log.Message("[RWU Extension Portable Void Monolith] Preserved the monolith quest while its previous map was destroyed.");
            return false;
        }
    }

    [HarmonyPatch(typeof(QuestNode_Root_MonolithMigration), "TestRunInt")]
    public static class QuestNodeRootMonolithMigrationPatch
    {
        public static bool Prefix(ref bool __result)
        {
            if (!MonolithUtility.HasPreservedMonolith())
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(QuestPart_SpawnMonolith), nameof(QuestPart_SpawnMonolith.Notify_QuestSignalReceived))]
    public static class QuestPartSpawnMonolithPatch
    {
        public static bool Prefix()
        {
            if (!MonolithUtility.HasPreservedMonolith())
            {
                return true;
            }

            Log.Message("[RWU Extension Portable Void Monolith] Suppressed a migration replacement because the original monolith is still packed or travelling.");
            return false;
        }
    }

    internal static class VoidUniverseCompatibility
    {
        private const string MapComponentTypeName = "VoidUniverse.MapComponent_UV";

        public static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName(MapComponentTypeName);
            if (type == null)
            {
                return;
            }

            var prefix = new HarmonyMethod(typeof(VoidUniverseCompatibility), nameof(AllowMonolithGeneration));
            PatchIfPresent(harmony, AccessTools.Method(type, "MapGenerated"), prefix);
            PatchIfPresent(harmony, AccessTools.Method(type, "SpawnMonolith"), prefix);
            Log.Message("[RWU Extension Portable Void Monolith] Void Universe compatibility enabled; transported or packed monoliths will not be duplicated.");
        }

        public static bool AllowMonolithGeneration()
        {
            return !MonolithUtility.HasPreservedMonolith();
        }

        private static void PatchIfPresent(Harmony harmony, MethodBase original, HarmonyMethod prefix)
        {
            if (original != null)
            {
                harmony.Patch(original, prefix: prefix);
            }
        }
    }
}
