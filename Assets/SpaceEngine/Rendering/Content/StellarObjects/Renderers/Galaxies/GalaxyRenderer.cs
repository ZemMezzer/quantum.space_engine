using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Galaxies
{
    /// <summary>
    /// Stateless visual definition for one GalaxyEntity in a rendering
    /// backend. The backend resolves it by entity binding; no data-type scan or
    /// morphology enum is involved.
    /// </summary>
    public abstract class GalaxyRenderer : ScriptableObject
    {
        public abstract GalaxyVisualData GetVisualData(GalaxyData galaxy);

        public abstract bool TryCreateExternalStarSample(
            GalaxyData galaxy,
            ref QuantumRandom random,
            out GalaxyExternalStarSample sample);
    }

    public readonly struct GalaxyExternalStarSample
    {
        public readonly double3 GalaxyLocalPositionLightYears;
        public readonly float Brightness;

        public GalaxyExternalStarSample(
            double3 galaxyLocalPositionLightYears,
            float brightness)
        {
            GalaxyLocalPositionLightYears = galaxyLocalPositionLightYears;
            Brightness = brightness;
        }
    }

    /// <summary>
    /// Renderer-produced values consumed by shared URP galaxy draw code. The
    /// renderer provides all shape and palette parameters; the runtime does
    /// not infer a galaxy type.
    /// </summary>
    public readonly struct GalaxyVisualData
    {
        public readonly float ShaderMorphology;
        public readonly Color CoreColor;
        public readonly Color DiskColor;
        public readonly Color NebulaColor;
        public readonly Color HaloColor;
        public readonly Color ExternalFogColor;
        public readonly Color ExternalStarfieldColor;
        public readonly float GasDensity;
        public readonly float GasBrightness;
        public readonly float GasOpacity;
        public readonly float GasDustStrength;
        public readonly float GasDiskRadiusMultiplier;
        public readonly float GasDiskThicknessMultiplier;
        public readonly float SpiralArmCount;
        public readonly float SpiralArmTightness;
        public readonly float BarLengthRadiusMultiplier;
        public readonly float Ellipticity;
        public readonly float RingRadiusMultiplier;
        public readonly float RingWidthRadiusMultiplier;
        public readonly float Irregularity;

        public GalaxyVisualData(
            float shaderMorphology,
            Color coreColor,
            Color diskColor,
            Color nebulaColor,
            Color haloColor,
            Color externalFogColor,
            Color externalStarfieldColor,
            float gasDensity,
            float gasBrightness,
            float gasOpacity,
            float gasDustStrength,
            float gasDiskRadiusMultiplier,
            float gasDiskThicknessMultiplier,
            float spiralArmCount,
            float spiralArmTightness,
            float barLengthRadiusMultiplier,
            float ellipticity,
            float ringRadiusMultiplier,
            float ringWidthRadiusMultiplier,
            float irregularity)
        {
            ShaderMorphology = shaderMorphology;
            CoreColor = coreColor;
            DiskColor = diskColor;
            NebulaColor = nebulaColor;
            HaloColor = haloColor;
            ExternalFogColor = externalFogColor;
            ExternalStarfieldColor = externalStarfieldColor;
            GasDensity = Mathf.Clamp01(gasDensity);
            GasBrightness = Mathf.Max(0.0f, gasBrightness);
            GasOpacity = Mathf.Clamp(gasOpacity, 0.0f, 4.0f);
            GasDustStrength = Mathf.Clamp(gasDustStrength, 0.0f, 2.0f);
            GasDiskRadiusMultiplier = Mathf.Clamp(gasDiskRadiusMultiplier, 0.5f, 2.0f);
            GasDiskThicknessMultiplier = Mathf.Clamp(gasDiskThicknessMultiplier, 0.5f, 3.0f);
            SpiralArmCount = Mathf.Max(0.0f, spiralArmCount);
            SpiralArmTightness = Mathf.Max(0.0f, spiralArmTightness);
            BarLengthRadiusMultiplier = Mathf.Max(0.0f, barLengthRadiusMultiplier);
            Ellipticity = Mathf.Max(0.05f, ellipticity);
            RingRadiusMultiplier = Mathf.Max(0.0f, ringRadiusMultiplier);
            RingWidthRadiusMultiplier = Mathf.Max(0.0f, ringWidthRadiusMultiplier);
            Irregularity = Mathf.Clamp01(irregularity);
        }
    }
}
