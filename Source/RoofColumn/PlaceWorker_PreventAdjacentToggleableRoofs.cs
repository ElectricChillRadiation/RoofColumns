using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RoofColumn
{
	public class PlaceWorker_PreventAdjacentRoofColumns : PlaceWorker
	{
        public CellRect SizeOfDef(RoofColumn_ThingDef def, IntVec3 loc) {
            if (def == null) {
                Log.Error("Cannot calcualte SizeOfDef when def is NPE.");
            }

            return CellRect.CenteredOn(loc, def.roofRadius);
        }

        // Optimize by avoiding reallocating the entire list between renders/ticks
        // Should only grow in size but in general be the same == speed up
        private static List<Thing> RoofColumns = new List<Thing>();
        private IEnumerable<Thing> GetAllRoofColumnsOnMap() {
            // Find all Roof Columns on the map
			RoofColumns.AddRange(
                Find.CurrentMap.listerBuildings.AllBuildingsColonistOfClass<BaseRoofColumn>()
            );
            // Add our blueprint to the list of Roof Columns
			RoofColumns.AddRange(
                from t in Find.CurrentMap.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)
                where t.def.entityDefToBuild is RoofColumn_ThingDef
                select t
            );
            // Add all to-be-built Roof Columns
			RoofColumns.AddRange(
                from t in Find.CurrentMap.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame)
                where t.def.entityDefToBuild is RoofColumn_ThingDef
                select t
            );

            return RoofColumns;
        }

        private RoofColumn_ThingDef RoofColumn_ThingDefFromThing(Thing thing) {
            RoofColumn_ThingDef thingDef = thing.def as RoofColumn_ThingDef;
            if (thingDef == null && thing.def != null) {
                thingDef = thing.def.entityDefToBuild as RoofColumn_ThingDef;
            }
            if (thingDef == null) {
                Log.Error("Could not convert thing " + thing + "/" + thing.def + " to RoofColumn_ThingDef");
            }

            return thingDef;
        }

		public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
		{
            var RoofColumnToBePlaced = SizeOfDef(checkingDef as RoofColumn_ThingDef, loc);

            // Check the cells of all Roof Columns
            bool overlaps = false;
			foreach (Thing thing2 in GetAllRoofColumnsOnMap())
			{
                var other = SizeOfDef(RoofColumn_ThingDefFromThing(thing2), thing2.Position);
				if (RoofColumnToBePlaced.Overlaps(other))
				{
					overlaps = true;
                    break;
				}
			}
            
            // Clear the list now that we're done with it
			RoofColumns.Clear();

            if (overlaps) {
                return new AcceptanceReport("RC_CannotOverlapOtherRoofColumns".Translate());
            }

            return true;
		}

		public override void DrawGhost(ThingDef def, IntVec3 loc, Rot4 rot, Color ghostCol, Thing thing = null)
		{
            RoofColumn_ThingDef myThingDef = null;

            if (def != null) {
                myThingDef = def as RoofColumn_ThingDef;
                if (myThingDef == null && def != null) {
                    myThingDef = def.entityDefToBuild as RoofColumn_ThingDef;
                }
                if (myThingDef == null) {
                    Log.Error("Could not convert thingdef " + def + " to RoofColumn_ThingDef");
                }
            }
            if (myThingDef == null && thing != null) {
                myThingDef = RoofColumn_ThingDefFromThing(thing);
            }
            if (myThingDef == null) {
                Log.Error("Could not find RoofColumn_ThingDef");
            }

            var cellRect = SizeOfDef(myThingDef, loc);

			GenDraw.DrawFieldEdges(cellRect.ToList<IntVec3>(), Color.white, null);

            // Draw the cells of all Roof Columns
            bool overlaps = false;
			foreach (Thing thing2 in GetAllRoofColumnsOnMap())
			{
                var other = SizeOfDef(RoofColumn_ThingDefFromThing(thing2), thing2.Position);
				GenDraw.DrawFieldEdges(other.ToList<IntVec3>(), new Color(0.2f, 0.2f, 1f), null);
				if (cellRect.Overlaps(other))
				{
					overlaps = true;
				}
			}
            
            // Clear the list now that we're done with it
			RoofColumns.Clear();

			Color edgeColor = overlaps ? Designator_Place.CannotPlaceColor.ToOpaque() : Designator_Place.CanPlaceColor.ToOpaque();
			GenDraw.DrawFieldEdges(cellRect.ToList<IntVec3>(), edgeColor, null);
		}
    }
}