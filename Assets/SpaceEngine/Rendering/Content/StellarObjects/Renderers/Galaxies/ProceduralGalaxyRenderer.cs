using System;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Galaxy;
using SpaceEngine.Runtime.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Galaxies
{
    /// <summary>
    /// Common deterministic implementation for built-in galaxy renderers.
    /// It has no serialized visual fields. A concrete renderer owns its full
    /// visual profile and shape sampler in code, then varies that profile from
    /// the generated galaxy seed.
    /// </summary>
    public abstract class ProceduralGalaxyRenderer : GalaxyRenderer
    {
        protected abstract GalaxyVisualProfile CreateVisualProfile(
            GalaxyData galaxy);

        /// <summary>
        /// Returns a star sample in unrotated galaxy shape space.
        /// </summary>
        protected abstract double3 GenerateShapeLocalStarPosition(
            GalaxyData galaxy,
            ref QuantumRandom random);

        public sealed override GalaxyVisualData GetVisualData(
            GalaxyData galaxy)
        {
            if (galaxy == null)
                return default;

            var profile = CreateVisualProfile(galaxy);
            var variationSeed = GalaxyIDUtility.Combine(
                galaxy.Seed,
                profile.VariationSalt);

            var hueOffset = GetSignedUnit(variationSeed) *
                            profile.HueVariation;
            var saturationScale = 1.0f +
                                  GetSignedUnit(
                                      GalaxyIDUtility.Combine(
                                          variationSeed,
                                          0x5341545552415445UL)) *
                                  profile.SaturationVariation;
            var valueScale = 1.0f +
                             GetSignedUnit(
                                 GalaxyIDUtility.Combine(
                                     variationSeed,
                                     0x56414C55455F4A49UL)) *
                             profile.ValueVariation;

            var gasDensity = Vary(
                profile.GasDensity,
                variationSeed,
                0x44454E5349545955UL,
                profile.GasVariation);
            var gasBrightness = Vary(
                profile.GasBrightness,
                variationSeed,
                0x4252494748544E53UL,
                profile.GasVariation);
            var gasOpacity = Vary(
                profile.GasOpacity,
                variationSeed,
                0x4F5041434954595FUL,
                profile.GasVariation * 0.5f);

            var radius = Math.Max(1.0, galaxy.RadiusLightYears);

            return new GalaxyVisualData(
                profile.ShaderMorphology,
                VaryColor(profile.CoreColor, hueOffset, saturationScale, valueScale),
                VaryColor(profile.DiskColor, hueOffset, saturationScale, valueScale),
                VaryColor(profile.NebulaColor, hueOffset, saturationScale, valueScale),
                VaryColor(profile.HaloColor, hueOffset, saturationScale, valueScale),
                VaryColor(profile.ExternalFogColor, hueOffset, saturationScale, valueScale),
                VaryColor(profile.ExternalStarfieldColor, hueOffset, saturationScale, valueScale),
                gasDensity,
                gasBrightness,
                gasOpacity,
                Vary(
                    profile.GasDustStrength,
                    variationSeed,
                    0x445553545F535452UL,
                    profile.GasVariation * 0.5f),
                Vary(
                    profile.GasDiskRadiusMultiplier,
                    variationSeed,
                    0x5241444955535F56UL,
                    profile.GeometryVariation),
                Vary(
                    profile.GasDiskThicknessMultiplier,
                    variationSeed,
                    0x544849434B4E4553UL,
                    profile.GeometryVariation),
                profile.UsesSpiralArms
                    ? Mathf.Max(2.0f, galaxy.SpiralArmCount)
                    : 0.0f,
                profile.UsesSpiralArms
                    ? Mathf.Max(0.25f, (float)galaxy.SpiralArmTightness)
                    : 0.0f,
                profile.UsesBar
                    ? (float)Math.Max(
                        0.0,
                        galaxy.BarLengthLightYears / radius)
                    : 0.0f,
                profile.UsesEllipticity
                    ? Mathf.Max(0.05f, (float)galaxy.Ellipticity)
                    : 1.0f,
                profile.UsesRing
                    ? (float)Math.Max(
                        0.0,
                        galaxy.RingRadiusLightYears / radius)
                    : 0.0f,
                profile.UsesRing
                    ? (float)Math.Max(
                        0.0,
                        galaxy.RingWidthLightYears / radius)
                    : 0.0f,
                profile.UsesIrregularity
                    ? Mathf.Clamp01((float)galaxy.Irregularity)
                    : 0.0f);
        }

        public sealed override bool TryCreateExternalStarSample(
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
                GenerateShapeLocalStarPosition(galaxy, ref random),
                (float)random.NextDouble(0.45, 1.15));
            return true;
        }

        protected static double3 SampleSpiral(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            var radius = SampleConcentratedRadius(
                galaxy.RadiusLightYears,
                galaxy.CoreRadiusLightYears,
                ref random);
            var angle = random.NextDouble(0.0, math.PI * 2.0);

            if (radius >= galaxy.CoreRadiusLightYears &&
                galaxy.SpiralArmCount > 0 &&
                random.NextDouble() < 0.80)
            {
                var arm = random.NextInt(0, galaxy.SpiralArmCount);
                var core = Math.Max(1.0, galaxy.CoreRadiusLightYears);
                angle = math.PI * 2.0 * arm / galaxy.SpiralArmCount +
                        galaxy.SpiralArmTightness *
                        Math.Log(Math.Max(1.0, radius / core)) +
                        Bell(ref random) * 0.22;
            }

            return new double3(
                Math.Cos(angle) * radius,
                SampleDiskHeight(galaxy, ref random),
                Math.Sin(angle) * radius);
        }

        protected static double3 SampleBarredSpiral(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            if (galaxy.BarLengthLightYears > 0.0 &&
                random.NextDouble() < 0.28)
            {
                var halfLength = Math.Max(
                    1.0,
                    galaxy.BarLengthLightYears * 0.5);
                var halfThickness = Math.Max(
                    1.0,
                    galaxy.CoreRadiusLightYears * 0.16);

                return new double3(
                    random.NextDouble(-halfLength, halfLength),
                    SampleDiskHeight(galaxy, ref random),
                    math.clamp(
                        Bell(ref random) * halfThickness,
                        -halfThickness,
                        halfThickness));
            }

            return SampleSpiral(galaxy, ref random);
        }

        protected static double3 SampleLenticularDisk(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            var radius = SampleConcentratedRadius(
                galaxy.RadiusLightYears,
                galaxy.CoreRadiusLightYears,
                ref random);
            var angle = random.NextDouble(0.0, math.PI * 2.0);

            return new double3(
                Math.Cos(angle) * radius,
                SampleDiskHeight(galaxy, ref random),
                Math.Sin(angle) * radius);
        }

        protected static double3 SampleEllipsoid(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            var direction = SampleUnitVector(ref random);
            var radius = Math.Max(1.0, galaxy.RadiusLightYears) *
                         Math.Pow(random.NextDouble(), 1.85);
            var vertical = math.clamp(
                galaxy.Ellipticity,
                0.25,
                1.0);

            return new double3(
                direction.x * radius,
                direction.y * radius * vertical,
                direction.z * radius);
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
            var radial = math.clamp(
                ringRadius + Bell(ref random) * width,
                Math.Max(0.0, ringRadius - width * 2.5),
                Math.Min(radius * 0.96, ringRadius + width * 2.5));
            var angle = random.NextDouble(0.0, math.PI * 2.0);

            return new double3(
                Math.Cos(angle) * radial,
                SampleDiskHeight(galaxy, ref random),
                Math.Sin(angle) * radial);
        }

        protected static double3 SampleIrregular(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            var radius = Math.Max(1.0, galaxy.RadiusLightYears);
            var lobe = random.NextDouble(-0.35, 0.35) * radius;
            var localRadius = radius *
                              Math.Pow(random.NextDouble(), 1.45);
            var angle = random.NextDouble(0.0, math.PI * 2.0);
            var thickness = Math.Max(
                radius * 0.25,
                galaxy.DiskThicknessLightYears);

            return new double3(
                lobe + Math.Cos(angle) * localRadius,
                math.clamp(
                    Bell(ref random) * thickness,
                    -thickness,
                    thickness),
                Math.Sin(angle) * localRadius);
        }

        protected static double3 SampleDwarf(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            return random.NextDouble() < galaxy.Irregularity
                ? SampleIrregular(galaxy, ref random)
                : SampleEllipsoid(galaxy, ref random);
        }

        private static double SampleConcentratedRadius(
            double galaxyRadius,
            double coreRadius,
            ref QuantumRandom random)
        {
            var radius = Math.Max(1.0, galaxyRadius) *
                         Math.Pow(random.NextDouble(), 1.65);
            return math.clamp(
                radius,
                Math.Max(1.0, coreRadius * 0.08),
                Math.Max(1.0, galaxyRadius * 0.94));
        }

        private static double SampleDiskHeight(
            GalaxyData galaxy,
            ref QuantumRandom random)
        {
            var thickness = Math.Max(
                1.0,
                galaxy.DiskThicknessLightYears * 0.42);
            return math.clamp(
                Bell(ref random) * thickness,
                -thickness,
                thickness);
        }

        private static double3 SampleUnitVector(ref QuantumRandom random)
        {
            var z = random.NextDouble(-1.0, 1.0);
            var angle = random.NextDouble(0.0, math.PI * 2.0);
            var radial = Math.Sqrt(Math.Max(0.0, 1.0 - z * z));

            return new double3(
                Math.Cos(angle) * radial,
                z,
                Math.Sin(angle) * radial);
        }

        private static double Bell(ref QuantumRandom random)
        {
            return random.NextDouble() +
                   random.NextDouble() +
                   random.NextDouble() - 1.5;
        }

        private static float Vary(
            float value,
            ulong seed,
            ulong salt,
            float amplitude)
        {
            return value * (1.0f +
                            GetSignedUnit(
                                GalaxyIDUtility.Combine(seed, salt)) *
                            Mathf.Max(0.0f, amplitude));
        }

        private static float GetSignedUnit(ulong seed)
        {
            var unit = (seed >> 11) * (1.0 / (1UL << 53));
            return (float)(unit * 2.0 - 1.0);
        }

        private static Color VaryColor(
            Color color,
            float hueOffset,
            float saturationScale,
            float valueScale)
        {
            Color.RGBToHSV(color, out var hue, out var saturation, out var value);

            hue = Mathf.Repeat(hue + hueOffset, 1.0f);
            saturation = Mathf.Clamp01(saturation * saturationScale);
            value = Mathf.Max(0.0f, value * valueScale);

            var varied = Color.HSVToRGB(hue, saturation, value, true);
            varied.a = color.a;
            return varied;
        }
    }

    /// <summary>
    /// Code-owned style description. No member is serialized on a renderer
    /// asset; a concrete renderer returns a deterministic profile instead.
    /// </summary>
    public readonly struct GalaxyVisualProfile
    {
        public readonly ulong VariationSalt;
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
        public readonly float HueVariation;
        public readonly float SaturationVariation;
        public readonly float ValueVariation;
        public readonly float GasVariation;
        public readonly float GeometryVariation;
        public readonly bool UsesSpiralArms;
        public readonly bool UsesBar;
        public readonly bool UsesEllipticity;
        public readonly bool UsesRing;
        public readonly bool UsesIrregularity;

        public GalaxyVisualProfile(
            ulong variationSalt,
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
            bool usesSpiralArms,
            bool usesBar,
            bool usesEllipticity,
            bool usesRing,
            bool usesIrregularity,
            float hueVariation = 0.025f,
            float saturationVariation = 0.10f,
            float valueVariation = 0.12f,
            float gasVariation = 0.10f,
            float geometryVariation = 0.06f)
        {
            VariationSalt = variationSalt;
            ShaderMorphology = shaderMorphology;
            CoreColor = coreColor;
            DiskColor = diskColor;
            NebulaColor = nebulaColor;
            HaloColor = haloColor;
            ExternalFogColor = externalFogColor;
            ExternalStarfieldColor = externalStarfieldColor;
            GasDensity = gasDensity;
            GasBrightness = gasBrightness;
            GasOpacity = gasOpacity;
            GasDustStrength = gasDustStrength;
            GasDiskRadiusMultiplier = gasDiskRadiusMultiplier;
            GasDiskThicknessMultiplier = gasDiskThicknessMultiplier;
            UsesSpiralArms = usesSpiralArms;
            UsesBar = usesBar;
            UsesEllipticity = usesEllipticity;
            UsesRing = usesRing;
            UsesIrregularity = usesIrregularity;
            HueVariation = hueVariation;
            SaturationVariation = saturationVariation;
            ValueVariation = valueVariation;
            GasVariation = gasVariation;
            GeometryVariation = geometryVariation;
        }
    }
}
