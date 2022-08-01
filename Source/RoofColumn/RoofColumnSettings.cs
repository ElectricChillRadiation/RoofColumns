
using UnityEngine;
using Verse;

namespace RoofColumn
{
    public class RoofColumnSettings : ModSettings
    {
        public bool overwriteExistingRoofs;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref overwriteExistingRoofs, "RC_overwriteExistingRoofs", false);

            base.ExposeData();
        }
    }

    public class RoofColumnMod : Mod
    {
        RoofColumnSettings settings;

        public RoofColumnMod(ModContentPack content) : base(content)
        {
            this.settings = GetSettings<RoofColumnSettings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);
            listingStandard.CheckboxLabeled("RC_OverwriteLabel".Translate(), ref settings.overwriteExistingRoofs, "RC_OverwriteTooltip".Translate());
            listingStandard.End();
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "RC_RoofColumnName".Translate();
        }
    }
}
