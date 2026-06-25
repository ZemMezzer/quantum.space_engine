using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Universe;
using SpaceEngine.Runtime.Generation.Coordinates;
using SpaceEngine.Runtime.Utils;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Generation.Galaxy
{
    /// <summary>
    /// Generates one galaxy from an UniverseID-scoped logical GalaxyID.
    /// Hidden universe-sector placement is resolved internally and is never
    /// encoded into gameplay-facing CoordinatesData.
    /// </summary>
    public static class GalaxyGenerator
    {
        private const double SolarMassKg = 1.98847e30;

        /// <summary>
        /// Generates a galaxy from a gameplay-facing logical address.
        /// The address is never decoded as a packed universe-sector value.
        /// </summary>
        public static GalaxyData Generate(
            long universeID,
            long galaxyID)
        {
            return GenerateAtPosition(
                universeID,
                galaxyID,
                LogicalCoordinatesResolver.ResolveGalaxyUniversePosition(
                    universeID,
                    galaxyID));
        }

        /// <summary>
        /// Creates a universe-map record for a hidden, sector-issued galaxy.
        /// This path belongs to internal map streaming only.
        /// </summary>
        internal static GalaxyLocationData GenerateLocationAtUniversePosition(
            long universeID,
            long galaxyID,
            double3 universePosition)
        {
            var galaxy = GenerateAtPosition(
                universeID,
                galaxyID,
                universePosition);

            return new GalaxyLocationData(
                galaxy.GalaxyID,
                galaxy.Type,
                galaxy.UniversePositionLightYears,
                galaxy.RadiusLightYears);
        }

        private static GalaxyData GenerateAtPosition(
            long universeID,
            long galaxyID,
            double3 universePosition)
        {
            var seed = GalaxyIDUtility.GetGalaxySeed(
                universeID,
                galaxyID);

            var random = new QuantumRandom(seed);
            var type = SelectType(ref random);
            var radius = GenerateRadiusLightYears(type, ref random);

            var diskThickness = GenerateDiskThickness(
                type,
                radius,
                ref random);

            var coreRadius = GenerateCoreRadius(
                type,
                radius,
                ref random);

            var ellipticity = GenerateEllipticity(
                type,
                ref random);

            var armCount = GenerateArmCount(
                type,
                ref random);

            var armTightness = GenerateArmTightness(
                type,
                ref random);

            var barLength = GenerateBarLength(
                type,
                radius,
                ref random);

            var ringRadius = GenerateRingRadius(
                type,
                radius,
                ref random);

            var ringWidth = GenerateRingWidth(
                type,
                radius,
                ref random);

            var irregularity = GenerateIrregularity(
                type,
                ref random);

            var massKg = GenerateMassKg(
                type,
                radius,
                ref random);

            var systemDensity = GenerateSystemDensity(
                type,
                ref random);

            var gasDensity = GenerateGasDensity(
                type,
                ref random);

            var metallicity = random.NextDouble(0.002, 0.040);
            var rotation = random.NextDouble(0.0, math.PI * 2.0);

            return new GalaxyData(
                universeID,
                galaxyID,
                seed,
                type,
                universePosition,
                rotation,
                radius,
                coreRadius,
                diskThickness,
                massKg,
                systemDensity,
                gasDensity,
                metallicity,
                armCount,
                armTightness,
                barLength,
                ellipticity,
                ringRadius,
                ringWidth,
                irregularity);
        }

        private static GalaxyType SelectType(ref QuantumRandom random)
        {
            var value = random.NextDouble();

            if (value < 0.30)
                return GalaxyType.Spiral;
            if (value < 0.50)
                return GalaxyType.BarredSpiral;
            if (value < 0.66)
                return GalaxyType.Elliptical;
            if (value < 0.76)
                return GalaxyType.Lenticular;
            if (value < 0.87)
                return GalaxyType.Irregular;
            if (value < 0.92)
                return GalaxyType.Ring;

            return GalaxyType.Dwarf;
        }

        private static double GenerateRadiusLightYears(
            GalaxyType type,
            ref QuantumRandom random)
        {
            switch (type)
            {
                case GalaxyType.Spiral:
                case GalaxyType.BarredSpiral:
                    return random.NextDouble(25_000.0, 95_000.0);

                case GalaxyType.Elliptical:
                    return random.NextDouble(15_000.0, 140_000.0);

                case GalaxyType.Lenticular:
                    return random.NextDouble(18_000.0, 75_000.0);

                case GalaxyType.Irregular:
                    return random.NextDouble(4_000.0, 32_000.0);

                case GalaxyType.Ring:
                    return random.NextDouble(18_000.0, 75_000.0);

                case GalaxyType.Dwarf:
                    return random.NextDouble(700.0, 12_000.0);

                default:
                    return 10_000.0;
            }
        }

        private static double GenerateDiskThickness(
            GalaxyType type,
            double radius,
            ref QuantumRandom random)
        {
            switch (type)
            {
                case GalaxyType.Spiral:
                case GalaxyType.BarredSpiral:
                case GalaxyType.Ring:
                    return radius * random.NextDouble(0.006, 0.025);

                case GalaxyType.Lenticular:
                    return radius * random.NextDouble(0.020, 0.060);

                case GalaxyType.Irregular:
                case GalaxyType.Dwarf:
                    return radius * random.NextDouble(0.15, 0.45);

                case GalaxyType.Elliptical:
                    return radius * random.NextDouble(0.35, 0.95);

                default:
                    return radius * 0.03;
            }
        }

        private static double GenerateCoreRadius(
            GalaxyType type,
            double radius,
            ref QuantumRandom random)
        {
            var multiplier = type == GalaxyType.Elliptical
                ? random.NextDouble(0.16, 0.38)
                : random.NextDouble(0.035, 0.16);

            return radius * multiplier;
        }

        private static double GenerateEllipticity(
            GalaxyType type,
            ref QuantumRandom random)
        {
            switch (type)
            {
                case GalaxyType.Elliptical:
                    return random.NextDouble(0.35, 1.0);

                case GalaxyType.Dwarf:
                    return random.NextDouble(0.45, 1.0);

                default:
                    return 1.0;
            }
        }

        private static byte GenerateArmCount(
            GalaxyType type,
            ref QuantumRandom random)
        {
            return type == GalaxyType.Spiral ||
                   type == GalaxyType.BarredSpiral
                ? (byte)random.NextInt(2, 6)
                : (byte)0;
        }

        private static double GenerateArmTightness(
            GalaxyType type,
            ref QuantumRandom random)
        {
            return type == GalaxyType.Spiral ||
                   type == GalaxyType.BarredSpiral
                ? random.NextDouble(1.2, 4.8)
                : 0.0;
        }

        private static double GenerateBarLength(
            GalaxyType type,
            double radius,
            ref QuantumRandom random)
        {
            return type == GalaxyType.BarredSpiral
                ? radius * random.NextDouble(0.25, 0.65)
                : 0.0;
        }

        private static double GenerateRingRadius(
            GalaxyType type,
            double radius,
            ref QuantumRandom random)
        {
            return type == GalaxyType.Ring
                ? radius * random.NextDouble(0.35, 0.75)
                : 0.0;
        }

        private static double GenerateRingWidth(
            GalaxyType type,
            double radius,
            ref QuantumRandom random)
        {
            return type == GalaxyType.Ring
                ? radius * random.NextDouble(0.04, 0.16)
                : 0.0;
        }

        private static double GenerateIrregularity(
            GalaxyType type,
            ref QuantumRandom random)
        {
            return type == GalaxyType.Irregular
                ? random.NextDouble(0.55, 1.0)
                : type == GalaxyType.Dwarf
                    ? random.NextDouble(0.20, 0.85)
                    : random.NextDouble(0.0, 0.12);
        }

        private static double GenerateMassKg(
            GalaxyType type,
            double radius,
            ref QuantumRandom random)
        {
            var radiusFactor = radius / 10_000.0;
            var morphologyFactor = type == GalaxyType.Dwarf
                ? 0.08
                : type == GalaxyType.Elliptical
                    ? 2.2
                    : type == GalaxyType.Irregular
                        ? 0.35
                        : 1.0;

            var solarMasses = radiusFactor * radiusFactor *
                              random.NextDouble(1.5e9, 9.0e9) *
                              morphologyFactor;

            return solarMasses * SolarMassKg;
        }

        private static double GenerateSystemDensity(
            GalaxyType type,
            ref QuantumRandom random)
        {
            switch (type)
            {
                case GalaxyType.Elliptical:
                    return random.NextDouble(0.0025, 0.0100);

                case GalaxyType.Spiral:
                case GalaxyType.BarredSpiral:
                    return random.NextDouble(0.0015, 0.0060);

                case GalaxyType.Lenticular:
                    return random.NextDouble(0.0012, 0.0045);

                case GalaxyType.Ring:
                    return random.NextDouble(0.0015, 0.0055);

                case GalaxyType.Irregular:
                    return random.NextDouble(0.0005, 0.0030);

                case GalaxyType.Dwarf:
                    return random.NextDouble(0.0002, 0.0020);

                default:
                    return 0.001;
            }
        }

        private static double GenerateGasDensity(
            GalaxyType type,
            ref QuantumRandom random)
        {
            switch (type)
            {
                case GalaxyType.Spiral:
                case GalaxyType.BarredSpiral:
                case GalaxyType.Irregular:
                    return random.NextDouble(0.45, 1.0);

                case GalaxyType.Ring:
                    return random.NextDouble(0.30, 0.85);

                case GalaxyType.Lenticular:
                    return random.NextDouble(0.03, 0.22);

                case GalaxyType.Elliptical:
                    return random.NextDouble(0.001, 0.06);

                case GalaxyType.Dwarf:
                    return random.NextDouble(0.10, 0.75);

                default:
                    return 0.0;
            }
        }
    }
}
