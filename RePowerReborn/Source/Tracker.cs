using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RePower
{
    public class Tracker
    {
        private readonly RePower RePower;

        public static HashSet<Building> BuildingsInUse = new HashSet<Building>();
        public HashSet<Building> BuildingsUsedLastTick = new HashSet<Building>();
        public HashSet<Building> BuildingsToModify = new HashSet<Building>();

        public HashSet<ThingDef> BuildingDefsReservable = new HashSet<ThingDef>();
        public HashSet<Building> ReservableBuildings = new HashSet<Building>();

        public HashSet<ThingDef> ScheduledBuildingsDefs = new HashSet<ThingDef>();
        public HashSet<Building> ScheduledBuildings = new HashSet<Building>();

        public HashSet<Building_Bed> MedicalBeds = new HashSet<Building_Bed>();
        public HashSet<Building> HiTechResearchBenches = new HashSet<Building>();

        public HashSet<Building_Door> Autodoors = new HashSet<Building_Door>();
        public HashSet<Building> DeepDrills = new HashSet<Building>();
        public HashSet<Building> HydroponcsBasins = new HashSet<Building>();

        private ThingDef MedicalBedDef;
        private ThingDef HiTechResearchBenchDef;
        private ThingDef AutodoorDef;
        private ThingDef DeepDrillDef;
        private ThingDef HydroponicsBasinDef;

        public Tracker(RePower rePower)
        {
            RePower = rePower;
        }

        public void LoadThingDefs()
        {
            MedicalBedDef = ThingDef.Named("HospitalBed");
            HiTechResearchBenchDef = ThingDef.Named("HiTechResearchBench");
            AutodoorDef = ThingDef.Named("Autodoor");
            DeepDrillDef = ThingDef.Named("DeepDrill");
            HydroponicsBasinDef = ThingDef.Named("HydroponicsBasin");
        }

        public static void AddBuildingUsed(Building building)
        {
            BuildingsInUse.Add(building);
        }

        public void EvalAll()
        {
            EvalBeds();
            EvalResearchTables();
            EvalAutodoors();
            EvalDeepDrills();
            EvalHydroponicsBasins();
            EvalScheduledBuildings();
        }

        public void ScanExternalReservable()
        {
            ReservableBuildings.Clear();
            foreach (ThingDef def in BuildingDefsReservable)
            {
                foreach (var map in Find.Maps)
                {
                    if (map == null) continue;
                    var buildings = map.listerBuildings.AllBuildingsColonistOfDef(def);
                    foreach (var building in buildings)
                    {
                        if (building == null) continue;
                        ReservableBuildings.Add(building);
                    }
                }
            }
        }

        public void EvalExternalReservable()
        {
            foreach (var building in ReservableBuildings)
            {
                // Cache misses
                if (building == null) continue;
                if (building.Map == null) continue;

                if (building.Map.reservationManager.IsReservedByAnyoneOf(building, building.Faction))
                {
                    BuildingsInUse.Add(building);
                }
            }
        }

        public void ScanScheduledBuildings()
        {
            ScheduledBuildings.Clear();
            foreach (ThingDef def in ScheduledBuildingsDefs)
            {
                foreach (var map in Find.Maps)
                {
                    if (map == null) continue;
                    var buildings = map.listerBuildings.AllBuildingsColonistOfDef(def);
                    foreach (var building in buildings)
                    {
                        if (building == null) continue;
                        ScheduledBuildings.Add(building);
                    }
                }
            }
        }

        public void EvalScheduledBuildings()
        {
            foreach (var building in ScheduledBuildings)
            {
                if (building == null) continue;
                if (building.Map == null) continue;

                var comp = building.GetComp<CompSchedule>();
                if (comp == null) continue; // Doesn't actually have a schedule

                if (comp.Allowed)
                {
                    BuildingsInUse.Add(building);
                }
            }
        }

        // Evaluate medical beds for medical beds in use, to register that the vitals monitors should be in high power mode
        public void EvalBeds()
        {
            foreach (var mediBed in MedicalBeds)
            {
                if (mediBed == null) continue; // Skip null beds (out of date cache)
                if (mediBed.Map == null) continue;

                bool occupied = mediBed.CurOccupants.Count() > 0;

                if (occupied)
                {
                    var facilityAffector = mediBed.GetComp<CompAffectedByFacilities>();
                    foreach (var facility in facilityAffector.LinkedFacilitiesListForReading)
                    {
                        BuildingsInUse.Add(facility as Building);
                    }
                }
            }
        }

        public void EvalDeepDrills()
        {
            foreach (var deepDrill in DeepDrills)
            {
                if (deepDrill == null) continue;
                if (deepDrill.Map == null) continue;

                var inUse = deepDrill.Map.reservationManager.IsReservedByAnyoneOf(deepDrill, deepDrill.Faction);

                if (!inUse) continue;

                BuildingsInUse.Add(deepDrill);
            }
        }

        // How to tell if a research table is in use?
        // I can't figure it out. Instead let's base it on being reserved for use
        public void EvalResearchTables()
        {
            foreach (var researchTable in HiTechResearchBenches)
            {
                if (researchTable == null) continue;
                if (researchTable.Map == null) continue;

                // Determine if we are reserved:
                var inUse = researchTable.Map.reservationManager.IsReservedByAnyoneOf(researchTable, researchTable.Faction);

                if (!inUse) continue;

                BuildingsInUse.Add(researchTable);
                var facilityAffector = researchTable.GetComp<CompAffectedByFacilities>();
                foreach (var facility in facilityAffector.LinkedFacilitiesListForReading)
                {
                    BuildingsInUse.Add(facility as Building);
                }
            }
        }

        public void EvalAutodoors()
        {
            foreach (var autodoor in Autodoors)
            {
                if (autodoor == null) continue;
                if (autodoor.Map == null) continue;

                // If the door allows passage and isn't blocked by an object
                if (autodoor.Open && (!autodoor.BlockedOpenMomentary))
                {
                    BuildingsInUse.Add(autodoor);
                }
            }
        }

        public void EvalHydroponicsBasins()
        {
            foreach (var basin in HydroponcsBasins)
            {
                if (basin == null) continue;
                if (basin.Map == null) continue;

                CellRect.CellRectIterator cri = basin.OccupiedRect().GetIterator();
                while (!cri.Done())
                {
                    var thingsOnTile = basin.Map.thingGrid.ThingsListAt(cri.Current);
                    foreach (var thing in thingsOnTile)
                    {
                        if (thing is Plant)
                        {
                            BuildingsInUse.Add(basin);
                            break;
                        }
                    }
                    cri.MoveNext();
                }
            }
        }

        public HashSet<ThingDef> thingDefsToLookFor;
        public void ScanForThings()
        {
            // Build the set of def names to look for if we don't have it
            if (thingDefsToLookFor == null)
            {
                thingDefsToLookFor = new HashSet<ThingDef>();
                var defNames = RePower.PowerLevels.Keys;
                foreach (var defName in defNames)
                {
                    thingDefsToLookFor.Add(ThingDef.Named(defName));
                }
            }

            ScanExternalReservable(); // Handle the scanning of external reservable objects
            ScanScheduledBuildings(); // Search for buildings with scheduled activation

            BuildingsToModify.Clear();
            MedicalBeds.Clear();
            HiTechResearchBenches.Clear();
            Autodoors.Clear();
            DeepDrills.Clear();
            HydroponcsBasins.Clear();

            var maps = Find.Maps;
            foreach (Map map in maps)
            {
                foreach (ThingDef def in thingDefsToLookFor)
                {
                    var matchingThings = map.listerBuildings.AllBuildingsColonistOfDef(def);
                    // Merge in all matching things
                    BuildingsToModify.UnionWith(matchingThings);
                }

                // Register the medical beds in the watch list
                var mediBeds = map.listerBuildings.AllBuildingsColonistOfDef(MedicalBedDef);
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

                var hydroponicsBasins = map.listerBuildings.AllBuildingsColonistOfDef(HydroponicsBasinDef);
                HydroponcsBasins.UnionWith(hydroponicsBasins);
            }
        }

        public void UpdateBuildingsToTick()
        {
            BuildingsUsedLastTick.Clear();
            BuildingsUsedLastTick.UnionWith(BuildingsInUse);
            BuildingsInUse.Clear();
        }
    }
}
