using System;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Galaxies
{
    /// <summary>
    /// Shared URP implementation for the built-in galaxy renderer assets.
    /// Concrete assets own their palette, shader shape constant and external
    /// star distribution; this base only forwards their authored values to the
    /// generic draw pipeline.
    /// </summary>
    public abstract class ProceduralGalaxyRenderer : GalaxyRenderer
    {
        [Header("Galaxy colours")]
        [SerializeField] private Color coreColor;
        [SerializeField] private Color diskColor;
        [SerializeField] private Color nebulaColor;
        [SerializeField] private Color haloColor;
        [SerializeField] private Color externalFogColor;
        [SerializeField] private Color externalStarfieldColor;

        [Header("Gas volume")]
        [SerializeField, Range(0.0f, 1.0f)] private float gasDensity = 0.8f;
        [SerializeField, Range(0.0f, 8.0f)] private float gasBrightness = 1.0f;
        [SerializeField, Range(0.0f, 4.0f)] private float gasOpacity = 1.25f;
        [SerializeField, Range(0.0f, 2.0f)] private float gasDustStrength = 0.9f;
        [SerializeField, Range(0.5f, 2.0f)] private float gasDiskRadiusMultiplier = 1.0f;
        [SerializeField, Range(0.5f, 3.0f)] private float gasDiskThicknessMultiplier = 1.0f;

        protected abstract float ShaderMorphologyValue { get; }
        protected abstract GalaxyRendererDefaults Defaults { get; }

        protected abstract double3 GenerateExternalStarPosition(
            GalaxyData galaxy,
            ref QuantumRandom random);

        protected void ApplyDefaultPaletteForNewAsset()
        {
            ApplyDefaultPalette();
        }

        private void OnValidate()
        {
            // Migration from the previous renderer assets, where all palette
            // fields defaulted to pure white. Any authored non-white palette is
            // preserved.
            if (IsLegacyWhitePalette() || IsUnsetPalette())
                ApplyDefaultPalette();
        }

        public override GalaxyVisualData GetVisualData(GalaxyData galaxy)
        {
            var defaults = Defaults;

            return new GalaxyVisualData(
                ShaderMorphologyValue,
                ResolveColor(coreColor, defaults.CoreColor),
                ResolveColor(diskColor, defaults.DiskColor),
                ResolveColor(nebulaColor, defaults.NebulaColor),
                ResolveColor(haloColor, defaults.HaloColor),
                ResolveColor(externalFogColor, defaults.ExternalFogColor),
                ResolveColor(
                    externalStarfieldColor,
                    defaults.ExternalStarfieldColor),
                gasDensity,
                gasBrightness,
                gasOpacity,
                gasDustStrength,
                gasDiskRadiusMultiplier,
                gasDiskThicknessMultiplier,
                galaxy == null ? 0.0f : galaxy.SpiralArmCount,
                galaxy == null ? 0.0f : (float)galaxy.SpiralArmTightness,
                galaxy == null || galaxy.RadiusLightYears <= 0.0
                    ? 0.0f
                    : (float)(galaxy.BarLengthLightYears /
                        galaxy.RadiusLightYears),
                galaxy == null ? 1.0f : (float)galaxy.Ellipticity,
                galaxy == null || galaxy.RadiusLightYears <= 0.0
                    ? 0.0f
                    : (float)(galaxy.RingRadiusLightYears /
                        galaxy.RadiusLightYears),
                galaxy == null || galaxy.RadiusLightYears <= 0.0
                    ? 0.0f
                    : (float)(galaxy.RingWidthLightYears /
                        galaxy.RadiusLightYears),
                galaxy == null ? 0.0f : (float)galaxy.Irregularity);
        }

        public override bool TryCreateExternalStarSample(
            GalaxyData galaxy,
            ref QuantumRandom random,
            out GalaxyExternalStarSample sample)
        {
            if (galaxy == null)
            {
                sample = default;
                return false;
            }

            sample = new GalaxyExternalStarSample(
                GenerateExternalStarPosition(galaxy, ref random),
                (float)random.NextDouble(0.45, 1.15));
            return true;
        }

        protected static double3 SampleDisk(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            var radius = Math.Max(1.0, galaxy.RadiusLightYears);
            var radial = radius * Math.Sqrt(random.NextDouble());
            var angle = random.NextDouble(0.0, math.PI * 2.0);
            var thickness = Math.Max(
                100.0,
                galaxy.DiskThicknessLightYears * 2.0);

            return new double3(
                Math.Cos(angle) * radial,
                (random.NextDouble(-1.0, 1.0) +
                 random.NextDouble(-1.0, 1.0)) * thickness * 0.25,
                Math.Sin(angle) * radial);
        }

        protected static double3 SampleEllipsoid(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            var radius = Math.Max(1.0, galaxy.RadiusLightYears);
            var direction = math.normalizesafe(new double3(
                random.NextDouble(-1.0, 1.0),
                random.NextDouble(-1.0, 1.0),
                random.NextDouble(-1.0, 1.0)),
                new double3(1.0, 0.0, 0.0));
            var scale = Math.Pow(random.NextDouble(), 1.0 / 3.0);
            var vertical = Math.Max(
                galaxy.DiskThicknessLightYears,
                radius * Math.Max(0.15, galaxy.Ellipticity));

            return new double3(
                direction.x * radius * scale,
                direction.y * vertical * scale,
                direction.z * radius * scale);
        }

        protected static double3 SampleRing(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            var radius = Math.Max(1.0, galaxy.RadiusLightYears);
            var ringRadius = galaxy.RingRadiusLightYears > 0.0
                ? galaxy.RingRadiusLightYears
                : radius * 0.65;
            var width = Math.Max(
                1.0,
                galaxy.RingWidthLightYears > 0.0
                    ? galaxy.RingWidthLightYears
                    : radius * 0.12);
            var radial = Math.Max(
                0.0,
                ringRadius + random.NextDouble(-width, width));
            var angle = random.NextDouble(0.0, math.PI * 2.0);

            return new double3(
                Math.Cos(angle) * radial,
                random.NextDouble(-1.0, 1.0) *
                Math.Max(100.0, galaxy.DiskThicknessLightYears),
                Math.Sin(angle) * radial);
        }

        protected static double3 SampleIrregular(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            var radius = Math.Max(1.0, galaxy.RadiusLightYears);
            var lobe = random.NextDouble(-0.35, 0.35) * radius;
            var localRadius = radius * random.NextDouble(0.1, 1.0);
            var angle = random.NextDouble(0.0, math.PI * 2.0);
            var thickness = Math.Max(
                radius * 0.25,
                galaxy.DiskThicknessLightYears);

            return new double3(
                lobe + Math.Cos(angle) * localRadius,
                random.NextDouble(-1.0, 1.0) * thickness,
                Math.Sin(angle) * localRadius);
        }

        private void ApplyDefaultPalette()
        {
            var defaults = Defaults;
            coreColor = defaults.CoreColor;
            diskColor = defaults.DiskColor;
            nebulaColor = defaults.NebulaColor;
            haloColor = defaults.HaloColor;
            externalFogColor = defaults.ExternalFogColor;
            externalStarfieldColor = defaults.ExternalStarfieldColor;
        }

        private bool IsUnsetPalette()
        {
            return coreColor.a <= 0.0f &&
                   diskColor.a <= 0.0f &&
                   nebulaColor.a <= 0.0f &&
                   haloColor.a <= 0.0f &&
                   externalFogColor.a <= 0.0f &&
                   externalStarfieldColor.a <= 0.0f;
        }

        private bool IsLegacyWhitePalette()
        {
            return IsWhite(coreColor) &&
                   IsWhite(diskColor) &&
                   IsWhite(nebulaColor) &&
                   IsWhite(haloColor) &&
                   IsWhite(externalFogColor) &&
                   IsWhite(externalStarfieldColor);
        }

        private static bool IsWhite(Color color)
        {
            return Mathf.Approximately(color.r, 1.0f) &&
                   Mathf.Approximately(color.g, 1.0f) &&
                   Mathf.Approximately(color.b, 1.0f) &&
                   Mathf.Approximately(color.a, 1.0f);
        }

        private static Color ResolveColor(Color value, Color fallback)
        {
            return value.a <= 0.0f ? fallback : value;
        }
    }

    public readonly struct GalaxyRendererDefaults
    {
        public readonly Color CoreColor;
        public readonly Color DiskColor;
        public readonly Color NebulaColor;
        public readonly Color HaloColor;
        public readonly Color ExternalFogColor;
        public readonly Color ExternalStarfieldColor;

        public GalaxyRendererDefaults(
            Color coreColor,
            Color diskColor,
            Color nebulaColor,
            Color haloColor,
            Color externalFogColor,
            Color externalStarfieldColor)
        {
            CoreColor = coreColor;
            DiskColor = diskColor;
            NebulaColor = nebulaColor;
            HaloColor = haloColor;
            ExternalFogColor = externalFogColor;
            ExternalStarfieldColor = externalStarfieldColor;
        }
    }
}
