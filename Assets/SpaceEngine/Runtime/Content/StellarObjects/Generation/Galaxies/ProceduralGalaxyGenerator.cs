using System;
using SpaceEngine.Runtime.Content.Generation;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Galaxy;
using SpaceEngine.Runtime.Utils;
using Unity.Collections;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Content.StellarObjects.Generation.Galaxies
{
    /// <summary>
    /// Shared deterministic sector infrastructure for the built-in galaxy
    /// generators. It deliberately contains no morphology, galaxy-type enum,
    /// shape selection, or physical parameter generation.
    ///
    /// Each concrete generator owns its own GalaxyData creation, density field
    /// and system-position distribution. This base only turns those rules into
    /// repeatable sector and solar-system queries.
    /// </summary>
    public abstract class ProceduralGalaxyGenerator : GalaxyGenerator
    {
        private const int MAXIMUM_LOCATION_ATTEMPTS = 32;
        private const double GALAXY_SECTOR_SIZE_LIGHT_YEARS = 10.0;
        private const ulong SOLAR_SYSTEM_POSITION_SALT = 0x4C4F4749435F5353UL;
        private const ulong SOLAR_SYSTEM_MASS_SALT = 0x4C4F4749435F4D41UL;

        /// <summary>
        /// Relative availability for this concrete generator. The engine calls
        /// GetWeight; this class only provides the common deterministic sampling
        /// convention used by the built-in content.
        /// </summary>
        protected abstract float RelativeWeight { get; }

        /// <summary>
        /// Evaluates this concrete generator's normalized system density in its
        /// own shape-space. A value of zero means the point cannot contain a
        /// solar system.
        /// </summary>
        protected abstract double GetShapeDensity(
            in GalaxyData galaxy,
            double3 shapeLocalPositionLightYears);

        /// <summary>
        /// Produces a candidate position using this concrete generator's own
        /// geometry. The returned point is in shape-space, before galaxy
        /// rotation is applied.
        /// </summary>
        protected abstract double3 GenerateShapeLocalPosition(
            in GalaxyData galaxy,
            ref QuantumRandom random);

        public override float GetWeight(in GalaxyGenerationContext context)
        {
            return context.GetSelectionWeight(RelativeWeight);
        }

        public override GalaxySectorData GenerateSector(
            in GalaxyData galaxy,
            int3 sectorCoordinates)
        {
            var seed = SolarSystemIDUtility.GetGalaxySectorSeed(
                galaxy.Seed,
                sectorCoordinates);

            var solarSystems =
                new FixedList4096Bytes<SolarSystemLocationData>();

            for (var candidateIndex = 0;
                 candidateIndex < GalaxySectorData.MAXIMUM_SOLAR_SYSTEMS;
                 candidateIndex++)
            {
                var candidate = GenerateCandidate(
                    galaxy,
                    sectorCoordinates,
                    (byte)candidateIndex);

                if (candidate.IsPresent)
                    solarSystems.Add(candidate.Location);
            }

            return new GalaxySectorData(
                seed,
                galaxy.GalaxyID,
                sectorCoordinates,
                solarSystems);
        }

        public override SolarSystemLocationData GenerateSolarSystemLocation(
            in GalaxyData galaxy,
            long solarSystemID)
        {
            var positionSeed = GalaxyIDUtility.Combine(
                galaxy.Seed,
                CoordinatesData.ToUnsigned(solarSystemID));
            positionSeed = GalaxyIDUtility.Combine(
                positionSeed,
                SOLAR_SYSTEM_POSITION_SALT);

            var random = new QuantumRandom(positionSeed);
            var shapePosition = double3.zero;

            for (var attempt = 0;
                 attempt < MAXIMUM_LOCATION_ATTEMPTS;
                 attempt++)
            {
                shapePosition = GenerateShapeLocalPosition(
                    galaxy,
                    ref random);

                if (GetShapeDensity(galaxy, shapePosition) >= 0.025)
                    break;
            }

            var massRandom = new QuantumRandom(GalaxyIDUtility.Combine(
                positionSeed,
                SOLAR_SYSTEM_MASS_SALT));

            return new SolarSystemLocationData(
                solarSystemID,
                FromShapeLocalPosition(galaxy, shapePosition),
                massRandom.NextDouble(0.08, 4.0));
        }

        public override bool IsInside(
            in GalaxyData galaxy,
            double3 localPositionLightYears)
        {
            return GetDensity(galaxy, localPositionLightYears) > 0.0001;
        }

        private GalaxySectorCandidateData GenerateCandidate(
            in GalaxyData galaxy,
            int3 sectorCoordinates,
            byte localSolarSystemIndex)
        {
            var sectorSeed = SolarSystemIDUtility.GetGalaxySectorSeed(
                galaxy.Seed,
                sectorCoordinates);
            var candidateSeed = GalaxyIDUtility.Combine(
                sectorSeed,
                localSolarSystemIndex);

            var random = new QuantumRandom(candidateSeed);
            var sectorOrigin = GalaxySectorUtility.GetOriginLightYears(
                sectorCoordinates);

            var galaxyLocalPosition = sectorOrigin + new double3(
                random.NextDouble(0.0, GALAXY_SECTOR_SIZE_LIGHT_YEARS),
                random.NextDouble(0.0, GALAXY_SECTOR_SIZE_LIGHT_YEARS),
                random.NextDouble(0.0, GALAXY_SECTOR_SIZE_LIGHT_YEARS));

            var density = GetDensity(galaxy, galaxyLocalPosition);
            var expectedSystems = density *
                                  galaxy.BaseSystemDensityPerCubicLightYear *
                                  GALAXY_SECTOR_SIZE_LIGHT_YEARS *
                                  GALAXY_SECTOR_SIZE_LIGHT_YEARS *
                                  GALAXY_SECTOR_SIZE_LIGHT_YEARS;

            var probability = math.clamp(
                expectedSystems / GalaxySectorData.MAXIMUM_SOLAR_SYSTEMS,
                0.0,
                1.0);

            var solarSystemID = SolarSystemIDUtility.CreateSolarSystemID(
                sectorCoordinates,
                localSolarSystemIndex);

            return new GalaxySectorCandidateData(
                random.NextDouble() <= probability,
                new SolarSystemLocationData(
                    solarSystemID,
                    galaxyLocalPosition,
                    random.NextDouble(0.08, 4.0)));
        }

        private double GetDensity(
            in GalaxyData galaxy,
            double3 localPositionLightYears)
        {
            return GetShapeDensity(
                galaxy,
                ToShapeLocalPosition(galaxy, localPositionLightYears));
        }

        protected static double3 ToShapeLocalPosition(
            in GalaxyData galaxy,
            double3 localPositionLightYears)
        {
            var cosine = Math.Cos(-galaxy.RotationRadians);
            var sine = Math.Sin(-galaxy.RotationRadians);

            return new double3(
                localPositionLightYears.x * cosine -
                localPositionLightYears.z * sine,
                localPositionLightYears.y,
                localPositionLightYears.x * sine +
                localPositionLightYears.z * cosine);
        }

        protected static double3 FromShapeLocalPosition(
            in GalaxyData galaxy,
            double3 shapeLocalPositionLightYears)
        {
            var cosine = Math.Cos(galaxy.RotationRadians);
            var sine = Math.Sin(galaxy.RotationRadians);

            return new double3(
                shapeLocalPositionLightYears.x * cosine -
                shapeLocalPositionLightYears.z * sine,
                shapeLocalPositionLightYears.y,
                shapeLocalPositionLightYears.x * sine +
                shapeLocalPositionLightYears.z * cosine);
        }

        protected static double Gaussian(double distance, double width)
        {
            if (width <= 0.0)
                return 0.0;

            var normalized = distance / width;
            return Math.Exp(-0.5 * normalized * normalized);
        }

        protected static double Exponential(double distance, double scale)
        {
            return scale <= 0.0
                ? 0.0
                : Math.Exp(-Math.Max(0.0, distance) / scale);
        }

        protected static double Clamp01(double value)
        {
            return math.clamp(value, 0.0, 1.0);
        }

        protected static double HashNoise3D(ulong seed, double3 position)
        {
            var floor = math.floor(position);
            var hash = GalaxyIDUtility.Combine(
                seed,
                unchecked((ulong)(long)floor.x));
            hash = GalaxyIDUtility.Combine(
                hash,
                unchecked((ulong)(long)floor.y));
            hash = GalaxyIDUtility.Combine(
                hash,
                unchecked((ulong)(long)floor.z));

            return (hash >> 11) * (1.0 / (1UL << 53));
        }
    }
}
