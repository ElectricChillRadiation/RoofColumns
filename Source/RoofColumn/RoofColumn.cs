using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using UnityEngine;
using Verse;

namespace RoofColumn
{
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

			yield break;
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
			var currentFilled = CellRect.CenteredOn(this.InteractionCell, currentFilledRadius);
			// Total possible cells that could be filled with our roof
			var maximumFilled = CellRect.CenteredOn(this.InteractionCell, radius);

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

			if (settings.overwriteExistingRoofs || !roofGrid.Roofed(this.InteractionCell)) {
				roofGrid.SetRoof(this.InteractionCell, roofDef);
			}

			foreach (IntVec3 cell in CellRect.CenteredOn(this.InteractionCell, currentFilledRadius)) {
				if (settings.overwriteExistingRoofs || !roofGrid.Roofed(cell)) {
					roofGrid.SetRoof(cell, roofDef);
				}
			}

			return currentFilledRadius >= radius;
		}

		public override void Tick()
		{
			base.Tick();

			if (!initialized) {
				var roofGrid = Find.CurrentMap.roofGrid;
				var roofDef = GetRoofTypeDef();
				foreach (IntVec3 cell in CellRect.CenteredOn(this.InteractionCell, GetRadius())) {
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

		// false = no power
		// true = power
		private bool previousPowerState = false;

		private int timer = -1;

		public bool isExpanded = false;

		public bool toggleExpandRoofWithPower = false;

		private bool initialized = false;

		private CompPowerTrader Power;

		private static Texture2D UI_EXPAND;

		private static Texture2D UI_RETRACT;

		private static Texture2D UI_TOGGLE_IS_OFF;

		private static Texture2D UI_TOGGLE_IS_ON;
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
