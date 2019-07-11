using RimWorld;
using System.Collections.Generic;
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

        public HashSet<Building_Bed> MedicalBeds = new HashSet<Building_Bed>();
        public HashSet<Building> HiTechResearchBenches = new HashSet<Building>();

        public HashSet<Building_Door> Autodoors = new HashSet<Building_Door>();
        public HashSet<Building> DeepDrills = new HashSet<Building>();

        private readonly ThingDef MedicalBedDef;
        private readonly ThingDef HiTechResearchBenchDef;
        private readonly ThingDef AutodoorDef;
        private readonly ThingDef DeepDrillDef;

        public Tracker(RePower rePower)
        {
            RePower = rePower;
            MedicalBedDef = ThingDef.Named("HospitalBed");
            HiTechResearchBenchDef = ThingDef.Named("HiTechResearchBench");
            AutodoorDef = ThingDef.Named("Autodoor");
            DeepDrillDef = ThingDef.Named("DeepDrill");
        }

        public static void AddBuildingUsed(Building building)
        {
            BuildingsInUse.Add(building);
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

        // Evaluate medical beds for medical beds in use, to register that the vitals monitors should be in high power mode
        public void EvalBeds()
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

            BuildingsToModify.Clear();
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
