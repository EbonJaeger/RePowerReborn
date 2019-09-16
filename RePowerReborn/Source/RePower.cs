// ----------------------------------------------------------------------
// These are basic usings. Always let them be here.
// ----------------------------------------------------------------------
using System;
using System.Collections.Generic;

// ----------------------------------------------------------------------
// These are RimWorld-specific usings. Activate/Deactivate what you need:
// ----------------------------------------------------------------------
using UnityEngine;         // Always needed
using Verse;               // RimWorld universal objects are here (like 'Building')
using RimWorld;            // RimWorld specific functions are found here (like 'Building_Battery')

// ----------------------------------------------------------------------
// Hugslib
// ----------------------------------------------------------------------
using HugsLib;
using Harmony;

namespace RePower
{
    // Track the power users
    [HarmonyPatch(typeof(Building_WorkTable), "UsedThisTick", new Type[] { })]
    public static class Building_WorkTable_UsedThisTick_Patch
    {
        [HarmonyPrefix]
        public static void UsedThisTick(Building_WorkTable __instance)
        {
            // The Hook for tracking things used:            
            Tracker.AddBuildingUsed(__instance);
        }
    }

    [HarmonyPatch(typeof(JobDriver_WatchBuilding), "WatchTickAction", new Type[] { })]
    public static class JobDriver_WatchBuilding_WatchTickAction_Patch
    {
        [HarmonyPrefix]
        public static void WatchTickAction(JobDriver_WatchBuilding __instance)
        {
            // The Hook for tracking things used:
            Tracker.AddBuildingUsed(__instance.job.targetA.Thing as Building);
        }
    }

    public class RePower : ModBase
    {
        public override string ModIdentifier => "RePowerReborn";

        public Dictionary<string, Vector2> PowerLevels { get; private set; } = new Dictionary<string, Vector2>();
        public Tracker Tracker { get; private set; }

        // Track the number of buildings on the map
        // When this changes, rescan now instead of delayed
        // (This seems to be the best way of figuring out when a new building is placed)
        // For simplicity, cheese it and only care about the visible map
        int lastVisibleBuildings = 0;

        int inUseTick = 0;
        int ticksToRescan = 0; // Tick tracker for rescanning

        public override void Tick(int currentTick)
        {
            if (inUseTick != currentTick)
            {
                inUseTick = currentTick;
                Tracker.UpdateBuildingsToTick();
            }

            Tracker.EvalBeds();
            Tracker.EvalResearchTables();
            Tracker.EvalAutodoors();
            Tracker.EvalDeepDrills();
            Tracker.EvalHydroponicsBasins();

            // Set the power level to idle for 
            foreach (Thing thing in Tracker.BuildingsToModify)
            {
                if (thing == null)
                {
                    Logger.Warning(string.Format("Tried to modify power level for '{0}', but it no longer exists", thing.def.defName));
                    continue;
                }

                var powerComp = thing.TryGetComp<CompPowerTrader>();
                if (powerComp != null)
                {
                    // Set the power requirement 
                    powerComp.PowerOutput = PowerLevels[thing.def.defName][0];
                }
            }

            var visibleBuildings = Find.AnyPlayerHomeMap.listerBuildings.allBuildingsColonist.Count;
            if (visibleBuildings != lastVisibleBuildings)
            {
                lastVisibleBuildings = visibleBuildings;
                ticksToRescan = 0; // Rescan now
            }

            --ticksToRescan;
            if (ticksToRescan < 0)
            {
                ticksToRescan = 2000;
                // Destructively modifies the things to modify power on, do the state resetting first
                Tracker.ScanForThings();
            }

            foreach (Building building in Tracker.BuildingsUsedLastTick)
            {
                // Skip modifying power on things we're not supposed to modify power on
                if (!Tracker.BuildingsToModify.Contains(building)) continue;

                var powerComp = building.TryGetComp<CompPowerTrader>();
                if (powerComp != null)
                {
                    // Set the power requirement to high if the building is in use
                    powerComp.PowerOutput = PowerLevels[building.def.defName][1];
                }
            }
        }

        public override void Initialize()
        {
            Tracker = new Tracker(this);
        }

        public override void DefsLoaded()
        {
            var defs = DefDatabase<RePowerDef>.AllDefs;
            var loadedDefs = new List<string>();
            var skippedDefs = new List<string>();
            int num = 0, loaded = 0;

            foreach (var def in defs)
            {
                ++num;
                var target = def.targetDef;
                var namedDef = DefDatabase<ThingDef>.GetNamedSilentFail(target);

                if (namedDef == null)
                {
                    skippedDefs.Add(target);
                    continue;
                }

                if (def.poweredWorkbench)
                {
                    RegisterWorkTable(namedDef.defName, def.lowPower, def.highPower);
                }

                if (def.poweredReservable)
                {
                    RegisterExternalReservable(namedDef.defName, def.lowPower, def.highPower);
                }

                // Some objects might not be reservable, like workbenches.
                // e.g., HydroponicsBasins
                if (!def.poweredWorkbench && !def.poweredReservable)
                {
                    PowerLevels.Add(namedDef.defName, new Vector2(def.lowPower, def.highPower));
                }

                ++loaded;
                loadedDefs.Add(target);
            }

            var names = String.Join(", ", loadedDefs.ToArray()).Trim();
            Logger.Message(string.Format("Loaded {1} of {0} building defs: {2}", num, loaded, names));

            if (skippedDefs.Count > 0)
            {
                names = String.Join(", ", skippedDefs.ToArray()).Trim();
                Logger.Message(string.Format("Skipped {0} defs because they could not be found: {1}", skippedDefs.Count, names));
            }

            Tracker.LoadThingDefs();
        }

        void RegisterExternalReservable(string defName, int lowPower, int highPower)
        {
            if (defName == null)
            {
                Logger.Warning(string.Format("Def Named {0} could not be found, its respective mod probably isn't loaded", defName));
                return;
            }

            try
            {
                var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);

                RegisterWorkTable(defName, lowPower, highPower);
                Tracker.BuildingDefsReservable.Add(def);
            }
            catch (Exception e)
            {
                Logger.Error(string.Format("Error while registering a reservable building: {0}", e.Message));
            }
        }

        void RegisterWorkTable(string defName, float idlePower, float activePower)
        {
            PowerLevels.Add(defName, new Vector2(idlePower, activePower));
        }

        public HugsLib.Utils.ModLogger GetLogger()
        {
            return Logger;
        }
    }
}
