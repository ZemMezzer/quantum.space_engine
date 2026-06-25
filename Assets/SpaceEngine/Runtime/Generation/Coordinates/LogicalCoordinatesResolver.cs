using System;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Galaxy;
using SpaceEngine.Runtime.Generation.Universe;
using SpaceEngine.Runtime.Utils;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Generation.Coordinates
{
    /// <summary>
    /// Converts gameplay-facing logical coordinates into hidden spatial data.
    ///
    /// It intentionally never decodes CoordinatesData as a packed sector ID:
    /// <c>new CoordinatesData(0, 0, 0)</c> is a normal address, not a point at
    /// an edge of a streaming range. All calculations stay inside the engine.
    /// </summary>
    internal static class LogicalCoordinatesResolver
    {
        private const int LogicalUniverseRadiusInSectors = 8_192;
        private const int MaximumLocationAttempts = 32;

        private const ulong GalaxyPositionSalt =
            0x4C4F4749435F4741UL;

        private const ulong SolarSystemPositionSalt =
            0x4C4F4749435F5353UL;

        private const ulong SolarSystemMassSalt =
            0x4C4F4749435F4D41UL;

        public static double3 ResolveGalaxyUniversePosition(
            long universeID,
            long galaxyID)
        {
            // The common default must be intuitive and visibly central.
            if (galaxyID == 0)
                return double3.zero;

            var seed = GalaxyIDUtility.Combine(
                GalaxyIDUtility.GetGalaxySeed(universeID, galaxyID),
                GalaxyPositionSalt);

            var random = new QuantumRandom(seed);
            var sector = new int3(
                random.NextInt(
                    -LogicalUniverseRadiusInSectors,
                    LogicalUniverseRadiusInSectors + 1),
                random.NextInt(
                    -LogicalUniverseRadiusInSectors,
                    LogicalUniverseRadiusInSectors + 1),
                random.NextInt(
                    -LogicalUniverseRadiusInSectors,
                    LogicalUniverseRadiusInSectors + 1));

            var origin = UniverseSectorUtility.GetOriginLightYears(sector);
            var size = UniverseSectorGenerator.SECTOR_SIZE_LIGHT_YEARS;

            return origin + new double3(
                random.NextDouble(0.0, size),
                random.NextDouble(0.0, size),
                random.NextDouble(0.0, size));
        }

        public static SolarSystemLocationData ResolveSolarSystemLocation(
            in GalaxyData galaxy,
            long solarSystemID)
        {
            var positionSeed = GalaxyIDUtility.Combine(
                galaxy.Seed,
                CoordinatesData.ToUnsigned(solarSystemID));

            positionSeed = GalaxyIDUtility.Combine(
                positionSeed,
                SolarSystemPositionSalt);

            var random = new QuantumRandom(positionSeed);
            var position = double3.zero;

            for (var attempt = 0;
                 attempt < MaximumLocationAttempts;
                 attempt++)
            {
                var shapeLocal = GenerateShapeLocalPosition(
                    galaxy,
                    ref random);

                position = FromShapeLocalPosition(galaxy, shapeLocal);

                // The rejection step keeps any raw ID inside meaningful galaxy
                // structure, including a ring or spiral arm, instead of in a
                // blank outer streaming sector.
                var density = GalaxyDensityUtility.GetDensity(
                    galaxy,
                    position);

                if (density >= 0.025)
                    break;
            }

            var massSeed = GalaxyIDUtility.Combine(
                positionSeed,
                SolarSystemMassSalt);

            var massRandom = new QuantumRandom(massSeed);

            return new SolarSystemLocationData(
                solarSystemID,
                position,
                massRandom.NextDouble(0.08, 4.0));
        }

        private static double3 GenerateShapeLocalPosition(
            in GalaxyData galaxy,
            ref QuantumRandom random)
        {
            switch (galaxy.Type)
            {
                case GalaxyType.Ring:
                    return GenerateRingPosition(galaxy, ref random);

                case GalaxyType.Elliptical:
                    return GenerateEllipticalPosition(galaxy, ref random);

                case GalaxyType.Irregular:
                case GalaxyType.Dwarf:
                    return GenerateIrregularPosition(galaxy, ref random);

                case GalaxyType.BarredSpiral:
                    if (galaxy.BarLengthLightYears > 0.0 &&
                        random.NextDouble() < 0.28)
                    {
                        return GenerateBarPosition(galaxy, ref random);
                    }

                    return GenerateDiskPosition(
                        galaxy,
                        ref random,
                        preferSpiralArms: true);

                case GalaxyType.Spiral:
                    return GenerateDiskPosition(
                        galaxy,
                        ref random,
                        preferSpiralArms: true);

                case GalaxyType.Lenticular:
                default:
                    return GenerateDiskPosition(
                        galaxy,
                        ref random,
                        preferSpiralArms: false);
            }
        }

        private static double3 GenerateDiskPosition(
            in GalaxyData galaxy,
            ref QuantumRandom random,
            bool preferSpiralArms)
        {
            var radius = galaxy.RadiusLightYears * Math.Pow(
                random.NextDouble(),
                1.65);

            radius = Math.Max(
                galaxy.CoreRadiusLightYears * 0.08,
                Math.Min(
                    radius,
                    galaxy.RadiusLightYears * 0.94));

            var angle = random.NextDouble(0.0, math.PI * 2.0);

            if (preferSpiralArms &&
                galaxy.SpiralArmCount > 0 &&
                random.NextDouble() < 0.72)
            {
                var armIndex = random.NextInt(0, galaxy.SpiralArmCount);
                var coreRadius = Math.Max(
                    1.0,
                    galaxy.CoreRadiusLightYears);

                angle = math.PI * 2.0 * armIndex /
                        galaxy.SpiralArmCount +
                        galaxy.SpiralArmTightness * Math.Log(
                            Math.Max(1.0, radius / coreRadius)) +
                        Bell(ref random) * 0.28;
            }

            var halfThickness = Math.Max(
                1.0,
                galaxy.DiskThicknessLightYears * 0.42);

            return new double3(
                Math.Cos(angle) * radius,
                math.clamp(
                    Bell(ref random) * halfThickness,
                    -halfThickness,
                    halfThickness),
                Math.Sin(angle) * radius);
        }

        private static double3 GenerateBarPosition(
            in GalaxyData galaxy,
            ref QuantumRandom random)
        {
            var halfLength = Math.Max(
                1.0,
                galaxy.BarLengthLightYears * 0.5);

            var halfThickness = Math.Max(
                1.0,
                galaxy.CoreRadiusLightYears * 0.16);

            var verticalThickness = Math.Max(
                1.0,
                galaxy.DiskThicknessLightYears * 0.45);

            return new double3(
                random.NextDouble(-halfLength, halfLength),
                math.clamp(
                    Bell(ref random) * verticalThickness,
                    -verticalThickness,
                    verticalThickness),
                math.clamp(
                    Bell(ref random) * halfThickness,
                    -halfThickness,
                    halfThickness));
        }

        private static double3 GenerateRingPosition(
            in GalaxyData galaxy,
            ref QuantumRandom random)
        {
            var width = Math.Max(1.0, galaxy.RingWidthLightYears);
            var radius = math.clamp(
                galaxy.RingRadiusLightYears + Bell(ref random) * width,
                Math.Max(
                    galaxy.CoreRadiusLightYears * 0.1,
                    galaxy.RingRadiusLightYears - width * 2.5),
                Math.Min(
                    galaxy.RadiusLightYears * 0.96,
                    galaxy.RingRadiusLightYears + width * 2.5));

            var angle = random.NextDouble(0.0, math.PI * 2.0);
            var halfThickness = Math.Max(
                1.0,
                galaxy.DiskThicknessLightYears * 0.45);

            return new double3(
                Math.Cos(angle) * radius,
                math.clamp(
                    Bell(ref random) * halfThickness,
                    -halfThickness,
                    halfThickness),
                Math.Sin(angle) * radius);
        }

        private static double3 GenerateEllipticalPosition(
            in GalaxyData galaxy,
            ref QuantumRandom random)
        {
            var direction = SampleUnitVector(ref random);
            var radius = galaxy.RadiusLightYears * Math.Pow(
                random.NextDouble(),
                1.85);

            var verticalScale = math.clamp(
                galaxy.Ellipticity,
                0.25,
                1.0);

            return new double3(
                direction.x * radius,
                direction.y * radius * verticalScale,
                direction.z * radius);
        }

        private static double3 GenerateIrregularPosition(
            in GalaxyData galaxy,
            ref QuantumRandom random)
        {
            var direction = SampleUnitVector(ref random);
            var radius = galaxy.RadiusLightYears * Math.Pow(
                random.NextDouble(),
                1.45);

            var verticalScale = math.clamp(
                galaxy.DiskThicknessLightYears /
                Math.Max(1.0, galaxy.RadiusLightYears),
                0.25,
                1.0);

            return new double3(
                direction.x * radius,
                direction.y * radius * verticalScale,
                direction.z * radius);
        }

        private static double3 FromShapeLocalPosition(
            in GalaxyData galaxy,
            double3 shapeLocalPosition)
        {
            var cos = Math.Cos(galaxy.RotationRadians);
            var sin = Math.Sin(galaxy.RotationRadians);

            return new double3(
                shapeLocalPosition.x * cos -
                shapeLocalPosition.z * sin,
                shapeLocalPosition.y,
                shapeLocalPosition.x * sin +
                shapeLocalPosition.z * cos);
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
    }
}
