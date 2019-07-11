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
            RegisterWorkTable("ElectricTailoringBench", -10, -500); // 10W Idle, 500W active
            RegisterWorkTable("ElectricSmithy", -10, -1000); // 10W Idle, 1000W Active
            RegisterWorkTable("TableMachining", -10, -1400); // 10W Idle, 1400W Active
            RegisterWorkTable("ElectricStove", -10, -1000); // 10W Idle, 1000W Active
            RegisterWorkTable("ElectricSmelter", -400, -4500); // 400W Idle, 4500W Active
            RegisterWorkTable("BiofuelRefinery", -10, -1800); // 10W Idle, 1800W Active
            RegisterWorkTable("FabricationBench", -10, -1800); // 10W Idle, 1800W Active
            RegisterWorkTable("ElectricCrematorium", -200, -750); // 200W Idle, 750W Active

            RegisterSpecialPowerTrader("MultiAnalyzer", -10, -600); // 10W Idle, 600W Active
            RegisterSpecialPowerTrader("VitalsMonitor", -10, -1000); // 10W Idle, 1000W Active
            RegisterSpecialPowerTrader("HiTechResearchBench", -100, -1000); // 100W Idle, 1000W Active
            RegisterSpecialPowerTrader("Autodoor", -5, -500); // 5W Idle, 500W Active

            // Televisions!
            RegisterSpecialPowerTrader("TubeTelevision", -10, -400); // 10W Idle, 400W Active
            RegisterSpecialPowerTrader("FlatscreenTelevision", -10, -400); // 10W Idle, 400W Active
            RegisterSpecialPowerTrader("MegascreenTelevision", -10, -400); // 10W Idle, 400W Active

            // Drill
            RegisterSpecialPowerTrader("DeepDrill", -10, -500); // 10W Idle, 500W Active

            Logger.Message("Initialized Components");

            Logger.Message("Registered instance");
        }

        public override void DefsLoaded()
        {
            var defs = DefDatabase<RePowerDef>.AllDefs;
            int num = 0, loaded = 0;
            foreach (var def in defs)
            {
                ++num;
                var target = def.targetDef;
                var namedDef = DefDatabase<ThingDef>.GetNamedSilentFail(target);
                if (namedDef == null)
                {
                    Logger.Message(string.Format("No def named {0} to load, skipping.", target));
                    continue;
                }
                else
                {
                    ++loaded;
                    Logger.Message(string.Format("Registering def named {0}", target));
                }

                if (def.poweredWorkbench)
                {
                    RegisterWorkTable(namedDef.defName, def.lowPower, def.highPower);
                }

                if (def.poweredReservable)
                {
                    RegisterExternalReservable(namedDef.defName, def.lowPower, def.highPower);
                }
            }

            Logger.Message(string.Format("Loaded {1} of {0} mod support defs.", num, loaded));

            Tracker = new Tracker(this);
        }

        void RegisterExternalReservable(string defName, int lowPower, int highPower)
        {
            try
            {
                var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);

                if (defName == null)
                {
                    Logger.Message(string.Format("Def Named {0} could not be found, it's respective mod probably isn't loaded", defName));
                    return;
                }
                else
                {
                    Logger.Message(string.Format("Attempting to register def named {0}", defName));
                }

                RegisterWorkTable(defName, lowPower, highPower);
                Tracker.BuildingDefsReservable.Add(def);
            }
            catch (Exception e)
            {
                Logger.Message(e.Message);
            }
        }

        void RegisterWorkTable(string defName, float idlePower, float activePower)
        {
            PowerLevels.Add(defName, new Vector2(idlePower, activePower));
        }

        void RegisterSpecialPowerTrader(string defName, float idlePower, float activePower)
        {
            PowerLevels.Add(defName, new Vector2(idlePower, activePower));
        }

        public float PowerFactor(CompPowerTrader trader, Building building)
        {
            var defName = building.def.defName;

            //instance.Logger.Message (defName + " checked for power factor");

            if (PowerLevels.ContainsKey(defName))
            {
                bool inUse = Tracker.BuildingsUsedLastTick.Contains(building);

                Logger.Message(string.Format("{0} ({1}) power adjusted", building.ThingID, defName));

                // Return the idle power if not in use, otherwise, return the active power
                return PowerLevels[defName][inUse ? 1 : 0];
            }

            return 1;
        }

        public HugsLib.Utils.ModLogger GetLogger()
        {
            return Logger;
        }
    }
}
