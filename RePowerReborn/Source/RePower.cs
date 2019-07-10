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
            RePower.AddBuildingUsed(__instance);
        }
    }

    [HarmonyPatch(typeof(JobDriver_WatchBuilding), "WatchTickAction", new Type[] { })]
    public static class JobDriver_WatchBuilding_WatchTickAction_Patch
    {
        [HarmonyPrefix]
        public static void WatchTickAction(JobDriver_WatchBuilding __instance)
        {
            // The Hook for tracking things used:
            RePower.AddBuildingUsed(__instance.job.targetA.Thing as Building);
        }
    }

    /**
    static class RepowerHook
    {

        [DetourMethod(typeof(Building_WorkTable), "UsedThisTick")]
        private static void _UsedThisTick(this Building_WorkTable self)
        {
            //RePower.Log ("Building Used Request");
            // Rather inefficient, since we're getting it each Tick instead of caching, but the cached copy is inaccessible
            // And quite frankly reflecting to it proved slower.
            // Possibly staticly caching it may suffice
            var refuelable = self.TryGetComp<CompRefuelable>();
            if (refuelable != null)
            {
                refuelable.Notify_UsedThisTick();
            }
            // The Hook for tracking things used:
            RePower.AddBuildingUsed(self);
        }

        [DetourMethod(typeof(JobDriver_WatchBuilding), "WatchTickAction")]
        private static void _WatchTickAction(this JobDriver_WatchBuilding self)
        {
            var targetA = self.pawn.jobs.curJob.targetA;
            self.pawn.Drawer.rotator.FaceCell(targetA.Cell);
            self.pawn.GainComfortFromCellIfPossible();

            float statValue = targetA.Thing.GetStatValue(StatDefOf.EntertainmentStrengthFactor, true);
            float extraJoyGainFactor = statValue; // What the heck Tynan?
            JoyUtility.JoyTickCheckEnd(self.pawn, JoyTickFullJoyAction.EndJob, extraJoyGainFactor);

            RePower.AddBuildingUsed(targetA.Thing as Building);
        }
    }
        **/

    public class RePower : ModBase
    {
        #region hugslib
        public override string ModIdentifier
        {
            get
            {
                return "RePower";
            }
        }

        // Track the number of buildings on the map
        // When this changes, rescan now instead of delayed
        // (This seems to be the best way of figuring out when a new building is placed)
        // For simplicity, cheese it and only care about the visible map
        int lastVisibleBuildings = 0;

        int ticksToRescan = 0; // Tick tracker for rescanning
        public override void Tick(int currentTick)
        {
            if (inUseTick != currentTick)
            {
                inUseTick = currentTick;

                buildingsThatWereUsedLastTick.Clear();
                buildingsThatWereUsedLastTick.UnionWith(buildingsInUseThisTick);
                buildingsInUseThisTick.Clear();
            }

            EvalBeds();
            EvalResearchTables();
            EvalAutodoors();
            EvalDeepDrills();

            foreach (Thing thing in buildingsToModifyPowerOn)
            {
                if (thing == null)
                {
                    Logger.Message("Tried to modify power level for thing which no longer exists");
                    continue;
                }

                var powerComp = thing.TryGetComp<CompPowerTrader>();
                if (powerComp != null)
                {
                    // Set the power requirement 
                    powerComp.PowerOutput = powerLevels[thing.def.defName][0];
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
                ScanForThings();
            }

            foreach (Building building in buildingsThatWereUsedLastTick)
            {
                // Skip modifying power on things we're not supposed to modify power on
                if (!buildingsToModifyPowerOn.Contains(building)) continue;

                var powerComp = building.TryGetComp<CompPowerTrader>();
                if (powerComp != null)
                {
                    // Set the power requirement to high if the building is in use
                    powerComp.PowerOutput = powerLevels[building.def.defName][1];
                }
            }
        }

        public static RePower instance;
        public static void Log(string log)
        {
            if (instance == null) return;
            instance.Logger.Message(log);
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
                    RegisterWorkTable(namedDef.defName, def.lowPower, def.highPower);

                if (def.poweredReservable)
                    RegisterExternalReservable(namedDef.defName, def.lowPower, def.highPower);

            }
            Logger.Message(string.Format("Loaded {1} of {0} mod support defs.", num, loaded));

            medicalBedDef = ThingDef.Named("HospitalBed");
            HiTechResearchBenchDef = ThingDef.Named("HiTechResearchBench");
            AutodoorDef = ThingDef.Named("Autodoor");
            DeepDrillDef = ThingDef.Named("DeepDrill");
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

            instance = this;

            Logger.Message("Registered instance");
        }

        #endregion
        // Power levels pairs as Vector2's, X = Idling, Y = In Use
        static Dictionary<string, Vector2> powerLevels = new Dictionary<string, Vector2>();
        //static HashSet<ThingDef> workTablesRegistered = new HashSet<ThingDef> ();
        static void RegisterWorkTable(string defName, float idlePower, float activePower)
        {
            powerLevels.Add(defName, new Vector2(idlePower, activePower));
            //workTablesRegistered.Add (ThingDef.Named (defName));
        }

        static void RegisterSpecialPowerTrader(string defName, float idlePower, float activePower)
        {
            powerLevels.Add(defName, new Vector2(idlePower, activePower));
        }

        static public float PowerFactor(CompPowerTrader trader, Building building)
        {
            var defName = building.def.defName;

            //instance.Logger.Message (defName + " checked for power factor");

            if (powerLevels.ContainsKey(defName))
            {
                bool inUse = buildingsThatWereUsedLastTick.Contains(building);

                instance.Logger.Message(string.Format("{0} ({1}) power adjusted", building.ThingID, defName));

                // Return the idle power if not in use, otherwise, return the active power
                return powerLevels[defName][inUse ? 1 : 0];
            }

            return 1;
        }

        #region tracking
        public static int inUseTick = 0;
        public static HashSet<Building> buildingsThatWereUsedLastTick = new HashSet<Building>();
        public static HashSet<Building> buildingsInUseThisTick = new HashSet<Building>();
        public static HashSet<Building> buildingsToModifyPowerOn = new HashSet<Building>();

        public static HashSet<ThingDef> buildingDefsReservable = new HashSet<ThingDef>();
        public static HashSet<Building> reservableBuildings = new HashSet<Building>();

        public static HashSet<Building_Bed> MedicalBeds = new HashSet<Building_Bed>();
        public static HashSet<Building> HiTechResearchBenches = new HashSet<Building>();

        public static HashSet<Building_Door> Autodoors = new HashSet<Building_Door>();
        public static HashSet<Building> DeepDrills = new HashSet<Building>();

        private static ThingDef medicalBedDef;
        private static ThingDef HiTechResearchBenchDef;
        private static ThingDef AutodoorDef;
        private static ThingDef DeepDrillDef;

        public static void AddBuildingUsed(Building building)
        {
            buildingsInUseThisTick.Add(building);
        }

        public static void RegisterExternalReservable(string defName, int lowPower, int highPower)
        {
            try
            {
                var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);

                if (defName == null)
                {
                    instance.Logger.Message(string.Format("Def Named {0} could not be found, it's respective mod probably isn't loaded", defName));
                    return;
                }
                else
                {
                    instance.Logger.Message(string.Format("Attempting to register def named {0}", defName));
                }

                RegisterWorkTable(defName, lowPower, highPower);
                buildingDefsReservable.Add(def);
            }
            catch (System.Exception e)
            {
                instance.Logger.Message(e.Message);
            }
        }

        public static void ScanExternalReservable()
        {
            reservableBuildings.Clear();
            foreach (ThingDef def in buildingDefsReservable)
            {
                foreach (var map in Find.Maps)
                {
                    if (map == null) continue;
                    var buildings = map.listerBuildings.AllBuildingsColonistOfDef(def);
                    foreach (var building in buildings)
                    {
                        if (building == null) continue;
                        reservableBuildings.Add(building);
                    }
                }
            }
        }

        public static void EvalExternalReservable()
        {
            foreach (var building in reservableBuildings)
            {
                // Cache misses
                if (building == null) continue;
                if (building.Map == null) continue;

                if (building.Map.reservationManager.IsReservedByAnyoneOf(building, building.Faction))
                {
                    buildingsInUseThisTick.Add(building);
                }
            }
        }

        // Evaluate medical beds for medical beds in use, to register that the vitals monitors should be in high power mode
        public static void EvalBeds()
        {
            foreach (var mediBed in MedicalBeds)
            {
                if (mediBed == null) continue; // Skip null beds (out of date cache)
                if (mediBed.Map == null) continue;

                bool occupied = false;
                foreach (var occupant in mediBed.CurOccupants)
                {
                    occupied = true;
                }

                if (occupied)
                {
                    var facilityAffector = mediBed.GetComp<CompAffectedByFacilities>();
                    foreach (var facility in facilityAffector.LinkedFacilitiesListForReading)
                    {
                        buildingsInUseThisTick.Add(facility as Building);
                    }
                }
            }
        }

        public static void EvalDeepDrills()
        {
            foreach (var deepDrill in DeepDrills)
            {
                if (deepDrill == null) continue;
                if (deepDrill.Map == null) continue;

                var inUse = deepDrill.Map.reservationManager.IsReservedByAnyoneOf(deepDrill, deepDrill.Faction);

                if (!inUse) continue;

                buildingsInUseThisTick.Add(deepDrill);
            }
        }

        // How to tell if a research table is in use?
        // I can't figure it out. Instead let's base it on being reserved for use
        public static void EvalResearchTables()
        {
            foreach (var researchTable in HiTechResearchBenches)
            {
                if (researchTable == null) continue;
                if (researchTable.Map == null) continue;

                // Determine if we are reserved:
                var inUse = researchTable.Map.reservationManager.IsReservedByAnyoneOf(researchTable, researchTable.Faction);

                if (!inUse) continue;

                buildingsInUseThisTick.Add(researchTable);
                var facilityAffector = researchTable.GetComp<CompAffectedByFacilities>();
                foreach (var facility in facilityAffector.LinkedFacilitiesListForReading)
                {
                    buildingsInUseThisTick.Add(facility as Building);
                }
            }
        }

        public static void EvalAutodoors()
        {
            foreach (var autodoor in Autodoors)
            {
                if (autodoor == null) continue;
                if (autodoor.Map == null) continue;

                // If the door allows passage and isn't blocked by an object
                var inUse = autodoor.Open && (!autodoor.BlockedOpenMomentary);
                if (inUse) buildingsInUseThisTick.Add(autodoor);
            }
        }

        public static HashSet<ThingDef> thingDefsToLookFor;
        public static void ScanForThings()
        {
            // Build the set of def names to look for if we don't have it
            if (thingDefsToLookFor == null)
            {
                thingDefsToLookFor = new HashSet<ThingDef>();
                var defNames = powerLevels.Keys;
                foreach (var defName in defNames)
                {
                    thingDefsToLookFor.Add(ThingDef.Named(defName));
                }
            }

            ScanExternalReservable(); // Handle the scanning of external reservable objects

            buildingsToModifyPowerOn.Clear();
            MedicalBeds.Clear();
            HiTechResearchBenches.Clear();
            Autodoors.Clear();
            DeepDrills.Clear();

            var maps = Find.Maps;
            foreach (Map map in maps)
            {
                foreach (ThingDef def in thingDefsToLookFor)
                {
                    var matchingThings = map.listerBuildings.AllBuildingsColonistOfDef(def);
                    // Merge in all matching things
                    buildingsToModifyPowerOn.UnionWith(matchingThings);
                }

                // Register the medical beds in the watch list
                var mediBeds = map.listerBuildings.AllBuildingsColonistOfDef(medicalBedDef);
                foreach (var mediBed in mediBeds)
                {
                    var medicalBed = mediBed as Building_Bed;
                    MedicalBeds.Add(medicalBed);
                }

                // Register Hightech research tables too
                var researchTables = map.listerBuildings.AllBuildingsColonistOfDef(HiTechResearchBenchDef);
                HiTechResearchBenches.UnionWith(researchTables);

                var doors = map.listerBuildings.AllBuildingsColonistOfDef(AutodoorDef);
                foreach (var door in doors)
                {
                    var autodoor = door as Building_Door;
                    Autodoors.Add(autodoor);
                }

                var deepDrills = map.listerBuildings.AllBuildingsColonistOfDef(DeepDrillDef);
                DeepDrills.UnionWith(deepDrills);
            }
        }
        #endregion
    }
}