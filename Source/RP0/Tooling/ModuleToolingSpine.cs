using System.Reflection;
using UnityEngine;

namespace RP0
{
    /// <summary>
    /// Tooling for the ProceduralParts "Spine" (lofted) shape, whose cross-section varies along its length.
    /// The spine is tooled as TWO bounding cylinders -- the narrowest and the widest section, both at the
    /// full part length -- and is considered tooled only when both are. A constant-section spine has
    /// min == max (within the tooling epsilon), so the two collapse into a single tooling. For every other
    /// proc shape this behaves exactly like <see cref="ModuleToolingPTank"/> (all paths fall through to base).
    /// </summary>
    public class ModuleToolingSpine : ModuleToolingPTank
    {
        private PartModule spineShape;
        private PropertyInfo piMaxDiam, piMinDiam;
        private bool triedBind;

        // The ProceduralPart's active shape is the Spine (procTank + shapeName come from the base module).
        private bool IsSpineActive =>
            procTank != null &&
            procTank.Fields["shapeName"] is BaseField f &&
            f.GetValue<string>(procTank) == "Spine";

        private bool BindSpine()
        {
            if (!triedBind)
            {
                triedBind = true;
                spineShape = part.Modules.Contains("ProceduralShapeSpine") ? part.Modules["ProceduralShapeSpine"] : null;
                if (spineShape != null)
                {
                    System.Type t = spineShape.GetType();
                    piMaxDiam = t.GetProperty("MaxDiameter");
                    piMinDiam = t.GetProperty("MinBoundingDiameter");
                }
            }
            return spineShape != null && piMaxDiam != null && piMinDiam != null;
        }

        /// <summary>
        /// The spine's min &amp; max bounding-cylinder tooling entries. <paramref name="twoEntries"/> is false
        /// for a constant section (min and max within the tooling epsilon), in which case only the max is used.
        /// Returns false when the active shape isn't a Spine (callers then defer to base behaviour).
        /// </summary>
        private bool GetSpineEntries(out float minDiam, out float maxDiam, out float len, out bool twoEntries)
        {
            minDiam = maxDiam = len = 0f;
            twoEntries = false;
            if (!IsSpineActive || !BindSpine())
                return false;

            maxDiam = (float)piMaxDiam.GetValue(spineShape);
            minDiam = (float)piMinDiam.GetValue(spineShape);
            len = spineShape.Fields["length"].GetValue<float>(spineShape);
            twoEntries = !ToolingDatabase.IsSameSize(minDiam, len, maxDiam, len);
            return true;
        }

        // Represent the spine by its max bounding cylinder for the base paths (module cost, IsSame, etc.).
        public override void GetDimensions(out float diam, out float len)
        {
            if (GetSpineEntries(out _, out float maxD, out float l, out _))
            {
                diam = maxD;
                len = l;
                return;
            }
            base.GetDimensions(out diam, out len);
        }

        public override float GetToolingCost()
        {
            if (GetSpineEntries(out float minD, out float maxD, out float l, out bool two))
            {
                TryApplyToolingDefinition();
                float cost = SpineEntryCost(maxD, l);
                if (two)
                    cost += SpineEntryCost(minD, l);
                return cost * finalToolingCostMultiplier;
            }
            return base.GetToolingCost();
        }

        public override void PurchaseTooling()
        {
            if (GetSpineEntries(out float minD, out float maxD, out float l, out bool two))
            {
                ToolingDatabase.UnlockTooling(ToolingType, maxD, l);
                if (two)
                    ToolingDatabase.UnlockTooling(ToolingType, minD, l);
                return;
            }
            base.PurchaseTooling();
        }

        public override bool IsUnlocked()
        {
            if (GetSpineEntries(out float minD, out float maxD, out float l, out bool two))
            {
                bool maxOk = maxD < minDiameter || ToolingDatabase.GetToolingLevel(ToolingType, maxD, l) == 2;
                bool minOk = !two || minD < minDiameter || ToolingDatabase.GetToolingLevel(ToolingType, minD, l) == 2;
                return maxOk && minOk;
            }
            return base.IsUnlocked();
        }

        public override string GetToolingParameterInfo()
        {
            if (GetSpineEntries(out float minD, out float maxD, out float l, out bool two))
            {
                return two
                    ? $"{minD:F3}m x {l:F3}m + {maxD:F3}m x {l:F3}m"
                    : $"{maxD:F3}m x {l:F3}m";
            }
            return base.GetToolingParameterInfo();
        }

        // One bounding cylinder's tooling cost, mirroring ModuleToolingDiamLen.GetToolingCost for a single
        // (diam, len) but leaving finalToolingCostMultiplier to the caller (applied once over both entries).
        private float SpineEntryCost(float d, float l)
        {
            float cost = GetLengthToolingCost(d, l);
            if (ToolingDatabase.GetToolingLevel(ToolingType, d, l) == 0)
                cost += GetCostReductionFactor(d, l) * GetDiameterToolingCost(d);
            return cost;
        }

        // ModuleToolingDiamLen.GetCostReductionFactor is private; reimplement it (CostReducers is accessible).
        private float GetCostReductionFactor(float d, float l)
        {
            float factor = 1f;
            foreach (System.Collections.Generic.KeyValuePair<string, float> reducer in CostReducers)
            {
                if (ToolingDatabase.GetToolingLevel(reducer.Key, d, l) > 0)
                    factor = Mathf.Min(reducer.Value, factor);
            }
            return factor;
        }
    }
}
