using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RoofColumn
{
	// TODO: Refactor all size computation into its own static class to enable everything
	// to be sane and have less places to copy-paste.

	public class RoofColumn_ThingDef : ThingDef
	{
		public int roofRadius = 5;
		public int ticksPerAnimation = 5;
	}

	[StaticConstructorOnStartup]
	public abstract class BaseRoofColumn : Building
	{	
		static BaseRoofColumn() {
			UI_EXPAND = ContentFinder<Texture2D>.Get("UI/Expand", true);
			UI_RETRACT = ContentFinder<Texture2D>.Get("UI/Retract", true);
			UI_TOGGLE_IS_OFF = ContentFinder<Texture2D>.Get("UI/Toggle_IsOff", true);
			UI_TOGGLE_IS_ON = ContentFinder<Texture2D>.Get("UI/Toggle_IsOn", true);
			UI_OVERWRITE_ON = ContentFinder<Texture2D>.Get("UI/Overwrite_IsOn", true);
			UI_OVERWRITE_OFF = ContentFinder<Texture2D>.Get("UI/Overwrite_IsOff", true);
			UI_PARTIAL_EXPANSION_OFF = ContentFinder<Texture2D>.Get("UI/PartialExpansion_IsOff", true);
			UI_PARTIAL_EXPANSION_ON = ContentFinder<Texture2D>.Get("UI/PartialExpansion_IsOn", true);
		}

		private RoofColumnSettings settings;
		protected BaseRoofColumn() {
			settings = LoadedModManager.GetMod<RoofColumnMod>().GetSettings<RoofColumnSettings>();
		}

		protected abstract int GetRadius();
		protected abstract int TicksPerAnimation();

		protected abstract RoofDef GetRoofTypeDef();

		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);
			if (this.def.HasComp(typeof(CompPowerTrader)))
			{
				this.Power = base.GetComp<CompPowerTrader>();
			}
		}

		public override void ExposeData()
		{
			Scribe_Values.Look(ref toggleExpandRoofWithPower, "RC_toggleExpandRoofWithPower", false);
			Scribe_Values.Look(ref overwriteExistingRoofs, "RC_overwriteExistingRoofs", false);

			base.ExposeData();
		}

		public override IEnumerable<Gizmo> GetGizmos()
		{
			foreach (Gizmo gizmo in base.GetGizmos())
			{
				yield return gizmo;
			}

			if (!toggleExpandRoofWithPower) {
				if (!isExpanded)
				{
					yield return new Command_Action
					{
						action = new Action(() => this.ExpandRoof(true)),
						defaultLabel = "Expand",
						defaultDesc = "Expand the roof",
						icon = BaseRoofColumn.UI_EXPAND
					};
				}
				else {
					yield return new Command_Action
					{
						action = new Action(() => this.RetractRoof(true)),
						defaultLabel = "Retract",
						defaultDesc = "Retract the roof",
						icon = BaseRoofColumn.UI_RETRACT
					};
				}
				yield return new Command_Action
				{
					action = new Action(this.ToggleExpandRoofWithPowerOption),
					defaultLabel = "Power-based Expansion",
					defaultDesc = "Toggle expanding the roof with power",
					icon = BaseRoofColumn.UI_TOGGLE_IS_OFF
				};
			}
			else {
				yield return new Command_Action
				{
					action = new Action(this.ToggleExpandRoofWithPowerOption),
					defaultLabel = "Power-based Expansion",
					defaultDesc = "Toggle expanding the roof with power",
					icon = BaseRoofColumn.UI_TOGGLE_IS_ON
				};
			}

			yield return new Command_Action
			{
				action = new Action(this.ToggleOverwriteExistingRoofs),
				defaultLabel = "Overwrite Roofs",
				defaultDesc = "Ovewrite existing roofs when expanding",
				icon = this.overwriteExistingRoofs ? BaseRoofColumn.UI_OVERWRITE_ON : BaseRoofColumn.UI_OVERWRITE_OFF
			};

			// Only allow partial expansion toggle when completely retracted
			if (!isExpanded && this.timer < 0) {
				yield return new Command_Action
				{
					action = new Action(this.TogglePartialExpansion),
					defaultLabel = "Partial Expansion",
					defaultDesc = "Only expand a corner enabling unique designs (such as a landing zone door).",
					icon = partialExpansion ? BaseRoofColumn.UI_PARTIAL_EXPANSION_ON : BaseRoofColumn.UI_PARTIAL_EXPANSION_OFF
				};
			}

			yield break;
		}

		private bool checkIfOverlap(string message, bool usePartialExpansionState = false)
		{
			// Filter out us
			var roofColumns =
				from r in PlaceWorker_PreventAdjacentRoofColumns.GetAllRoofColumnsOnMap()
				where r != this
				select r;
			
			// Check for overlap against our maximum expansion
			var maximum = expandedCells(GetRadius(), base.Rotation, usePartialExpansionState);
			foreach (Thing thing in roofColumns)
			{
				var other = PlaceWorker_PreventAdjacentRoofColumns.SizeOfThing(thing);
				if (maximum.Overlaps(other))
				{
					Messages.Message(message.Translate(), MessageTypeDefOf.RejectInput, true);
					return true;
				}
			}

			return false;
		}

		private void TogglePartialExpansion()
		{
			// TODO: check if overlaps other roof defs when changing to non-partial expansion from
			// partial expansion
			if (this.partialExpansion) {
				// Changing to non-partial expansion, need to ensure no overlap

				if (checkIfOverlap("RC_CannotExpandOverExistingRoofColumn")) {
					return;
				}
			}

			this.partialExpansion = !this.partialExpansion;
		}

		private void ToggleOverwriteExistingRoofs()
        {
            this.overwriteExistingRoofs = !this.overwriteExistingRoofs;
        }

        private void ToggleExpandRoofWithPowerOption()
        {
            this.toggleExpandRoofWithPower = !this.toggleExpandRoofWithPower;

			// Check if we're turned on now, if so, force expansion based on
			// current power
			if (this.toggleExpandRoofWithPower) {
				// If we currently have power, should expand
				if (this.Power.PowerOn) {
					this.ExpandRoof(false);
				}
				// If we don't have power, don't do anything.
				// This should "self correct" when the power is flipped again
			}
        }

        public void RetractRoof(bool checkForPower) {
			if (checkForPower && !this.Power.PowerOn) {
				Messages.Message("RC_NeedPowerRetract".Translate(), MessageTypeDefOf.RejectInput, true);
				return;
			}
			else {
				
			}

			this.isExpanded = false;
			this.timer = 0;
		}

		public void ExpandRoof(bool checkForPower) {
			if (checkForPower && !this.Power.PowerOn) {
				Messages.Message("RC_NeedPowerExpand".Translate(), MessageTypeDefOf.RejectInput, true);
				return;
			}

			this.isExpanded = true;
			this.timer = 0;
		}

		// Returns whether the animation is done
		private bool AnimateToRetractedRoof()
		{
			int radius = GetRadius();

			// Only retract every `TicksPerAnimation`
			if (this.timer % TicksPerAnimation() != 0) {
				return false;
			}

			int currentFilledRadius = -this.timer / TicksPerAnimation() + radius;

			// Number of cells that should still be filled with our roof after this tick
			var currentFilled = expandedCells(currentFilledRadius, base.Rotation);
			// Total possible cells that could be filled with our roof
			var maximumFilled = expandedCells(radius, base.Rotation);

			// Inverse of 'currentFilled' with respect to 'maximumFilled' should be empty of our roof

			var roofGrid = Find.CurrentMap.roofGrid;
			var roofDef = GetRoofTypeDef();
			foreach (IntVec3 cell in maximumFilled) {
				if (!currentFilled.Contains(cell)
					&& roofGrid.Roofed(cell)
					&& roofGrid.RoofAt(cell).defName == roofDef.defName) {
					roofGrid.SetRoof(cell, null);
					FloodFillerFog.FloodUnfog(cell, Find.CurrentMap);
				}
			}

			if (currentFilledRadius <= 0) {
				roofGrid.SetRoof(this.InteractionCell, null);
				FloodFillerFog.FloodUnfog(this.InteractionCell, Find.CurrentMap);
			}

			return currentFilledRadius <= 0;
		}

		// Returns whether the animation is done
		private bool AnimateToExpandedRoof()
		{
			int radius = GetRadius();

			// Only expand every `TicksPerAnimation`
			if (this.timer % TicksPerAnimation() != 0) {
				return false;
			}

			int currentFilledRadius = this.timer / TicksPerAnimation();
			var roofGrid = Find.CurrentMap.roofGrid;
			var roofDef = GetRoofTypeDef();

			if (settings.overwriteExistingRoofs
				|| this.overwriteExistingRoofs
				|| !roofGrid.Roofed(this.InteractionCell)) {
				roofGrid.SetRoof(this.InteractionCell, roofDef);
			}

			foreach (IntVec3 cell in expandedCells(currentFilledRadius, base.Rotation)) {
				if (settings.overwriteExistingRoofs
					|| this.overwriteExistingRoofs
					|| !roofGrid.Roofed(cell)) {
					roofGrid.SetRoof(cell, roofDef);
				}
			}

			return currentFilledRadius >= radius;
		}

		CellRect expandedCells(int radius, Rot4 rotation, bool usePartialExpansionState = true) {
			CellRect cells;
			if (this.partialExpansion && usePartialExpansionState) {
				cells = new CellRect
				{
					minX = this.InteractionCell.x,
					maxX = this.InteractionCell.x + radius,
					minZ = this.InteractionCell.z,
					maxZ = this.InteractionCell.z + radius
				};

				if (rotation == Rot4.North) {
					// Nothing to do
				}
				else if (rotation == Rot4.East) {
					cells = cells.MovedBy(new IntVec2 { x = 0, z = -radius });
				}
				else if (rotation == Rot4.South) {
					cells = cells.MovedBy(new IntVec2 { x = -radius, z = -radius });
				}
				else if (rotation == Rot4.West) {
					cells = cells.MovedBy(new IntVec2 { x = -radius, z = 0 });
				}
			}
			else {
				cells = CellRect.CenteredOn(this.InteractionCell, radius);
			}

			return cells;
		}

		public CellRect MaximumExpansion() {
			return expandedCells(GetRadius(), base.Rotation);
		}

		public override void Tick()
		{
			base.Tick();

			if (!initialized) {
				var roofGrid = Find.CurrentMap.roofGrid;
				var roofDef = GetRoofTypeDef();
				foreach (IntVec3 cell in expandedCells(GetRadius(), base.Rotation)) {
					if (roofGrid.Roofed(cell) && roofGrid.RoofAt(cell).defName == roofDef.defName) {
						isExpanded = true;
						break;
					}
				}
				
				initialized = true;
			}

			if (this.toggleExpandRoofWithPower) {
				// If we currently have power
				// and we previously didn't, we toggled 'on' and therefore should expand
				if (this.Power.PowerOn && !previousPowerState) {
					this.ExpandRoof(false);
				}
				// If we don't have power but we previously did
				// we should toggle 'off' and therefore should retract
				else if (!this.Power.PowerOn && previousPowerState) {
					this.RetractRoof(false);
				}
			}
			previousPowerState = this.Power.PowerOn;

			if (this.partialExpansion && this.previousRotation != base.Rotation) {
				bool isInvalidRotation = checkIfOverlap("RC_InvalidRotation", true);

				// Is this a valid rotation?
				if (!isInvalidRotation) {
					// Rotation changed, instantly move roofs
					var currentFilledRadius = 0;

					// If we're currently animating, find the radius we're expanded to
					// within the animation
					if (this.timer > 0) {
						// Grab the timer
						var currentTimer = this.timer;
						// Compensate for ticks per animation
						// in case we rotated between animation frames
						if (currentTimer % TicksPerAnimation() != 0) {
							currentTimer -= currentTimer % TicksPerAnimation();
						}

						// If we're expanded, use the expansion animation formula
						if (this.isExpanded) {
							currentFilledRadius = currentTimer / TicksPerAnimation();
						}
						// Otherwise use the retraction formula
						else {
							currentFilledRadius = -currentTimer / TicksPerAnimation() + GetRadius();
						}
					}
					// If we're not currently animating, use the maximum expansion radius
					// for computation
					else if (this.isExpanded) {
						currentFilledRadius = GetRadius();
					}

					// If we're expanded in some manner
					if (currentFilledRadius > 0) {
						// Remove the roof based on our old rotation
						this.InstantlyRemoveRoof(this.previousRotation);
						// Add the roof with our new rotation
						this.InstantlyExpandRoof(currentFilledRadius);
					}
				}
				else {
					// Invalid rotation, undo
					base.Rotation = this.previousRotation;
				}
			}
			this.previousRotation = base.Rotation;

			if (this.timer < 0) {
				// Nothing to do
				return;
			}
			
			if (this.isExpanded) {
				bool done = this.AnimateToExpandedRoof();
				this.timer++;

				if (done) {
					this.timer = -1;
				}
			}
			else {
				bool done = this.AnimateToRetractedRoof();
				this.timer++;

				if (done) {
					this.timer = -1;
				}
			}
		}

        private void InstantlyExpandRoof(int currentFilledRadius)
        {
			var roofGrid = Find.CurrentMap.roofGrid;
			var roofDef = GetRoofTypeDef();

			if (settings.overwriteExistingRoofs
				|| this.overwriteExistingRoofs
				|| !roofGrid.Roofed(this.InteractionCell)) {
				roofGrid.SetRoof(this.InteractionCell, roofDef);
			}

			foreach (IntVec3 cell in expandedCells(currentFilledRadius, base.Rotation)) {
				if (settings.overwriteExistingRoofs
					|| this.overwriteExistingRoofs
					|| !roofGrid.Roofed(cell)) {
					roofGrid.SetRoof(cell, roofDef);
				}
			}
        }

        private void InstantlyRemoveRoof(Rot4 rotation)
        {
            // Total possible cells that could be filled with our roof
			var maximumFilled = expandedCells(GetRadius(), rotation);

			var roofGrid = Find.CurrentMap.roofGrid;
			var roofDef = GetRoofTypeDef();
			foreach (IntVec3 cell in maximumFilled) {
				if (roofGrid.Roofed(cell)
					&& roofGrid.RoofAt(cell).defName == roofDef.defName) {
					roofGrid.SetRoof(cell, null);
					FloodFillerFog.FloodUnfog(cell, Find.CurrentMap);
				}
			}
        }

        // false = no power
        // true = power
        private bool previousPowerState = false;
	
		private Rot4 previousRotation = Rot4.North;

		private int timer = -1;

		public bool isExpanded = false;

		public bool toggleExpandRoofWithPower = false;

		public bool overwriteExistingRoofs = false;

		public bool partialExpansion = false;

		private bool initialized = false;

		private CompPowerTrader Power;

		private static Texture2D UI_EXPAND;

		private static Texture2D UI_RETRACT;

		private static Texture2D UI_TOGGLE_IS_OFF;

		private static Texture2D UI_TOGGLE_IS_ON;

		private static Texture2D UI_OVERWRITE_ON;

		private static Texture2D UI_OVERWRITE_OFF;

		private static Texture2D UI_PARTIAL_EXPANSION_OFF;

		private static Texture2D UI_PARTIAL_EXPANSION_ON;
	}

	[StaticConstructorOnStartup]
	public class ThinRoofColumn : BaseRoofColumn
	{
		private static RoofDef roofDef;
		private static RoofColumn_ThingDef roofThingDef;

		static ThinRoofColumn() {
			roofDef = DefDatabase<RoofDef>.GetNamed("RoofColumnInstanceThinRoof", true);
			roofThingDef = DefDatabase<ThingDef>.GetNamed("ThinRoofColumn", true) as RoofColumn_ThingDef;
		}

		protected override RoofDef GetRoofTypeDef() {
			return roofDef;
		}

		protected override int GetRadius() => roofThingDef.roofRadius;
		protected override int TicksPerAnimation() => roofThingDef.ticksPerAnimation;
	}

	[StaticConstructorOnStartup]
	public class ConstructedRoofColumn : BaseRoofColumn
	{
		private static RoofDef roofDef;
		private static RoofColumn_ThingDef roofThingDef;

		static ConstructedRoofColumn() {
			roofDef = DefDatabase<RoofDef>.GetNamed("RoofColumnInstanceConstructedRoof", true);
			roofThingDef = DefDatabase<ThingDef>.GetNamed("ConstructedRoofColumn", true) as RoofColumn_ThingDef;
		}

		protected override RoofDef GetRoofTypeDef() {
			return roofDef;
		}

		protected override int GetRadius() => roofThingDef.roofRadius;
		protected override int TicksPerAnimation() => roofThingDef.ticksPerAnimation;
	}

	[StaticConstructorOnStartup]
	public class MountainRoofColumn : BaseRoofColumn
	{
		private static RoofDef roofDef;
		private static RoofColumn_ThingDef roofThingDef;

		static MountainRoofColumn() {
			roofDef = DefDatabase<RoofDef>.GetNamed("RoofColumnInstanceMountainRoof", true);
			roofThingDef = DefDatabase<ThingDef>.GetNamed("MountainRoofColumn", true) as RoofColumn_ThingDef;
		}

		protected override RoofDef GetRoofTypeDef() {
			return roofDef;
		}

		protected override int GetRadius() => roofThingDef.roofRadius;
		protected override int TicksPerAnimation() => roofThingDef.ticksPerAnimation;
	}
}
