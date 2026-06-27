using System;
using SpaceEngine.Runtime.Content.StellarObjects.Generation.Galaxies;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Generation.Coordinates;
using SpaceEngine.Runtime.Generation.SolarSystem;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Runtime.Physics
{
    /// <summary>
    /// Current 3D simulation implementation. It owns only time and physics
    /// coordinates; generated galaxy/system content stays in ScriptableObject
    /// generators selected by their own GetWeight contracts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CelestialPhysics3D : CelestialPhysics
    {
        private const double StefanBoltzmannConstant = 5.670374419e-8;
        private const double StandardGravityMetersPerSecondSquared = 9.80665;
        private const double MinimumDistanceSquaredMeters = 0.000001;

        [Header("Simulation clock")]
        [SerializeField] private double simulationTimeScale = 1.0;
        [SerializeField] private double initialSimulationTimeSeconds;

        private double _simulationTimeSeconds;

        public override double SimulationTimeSeconds => _simulationTimeSeconds;

        private void OnValidate()
        {
            simulationTimeScale = Math.Max(0.0, simulationTimeScale);
        }

        protected override void OnInitialize()
        {
            _simulationTimeSeconds = initialSimulationTimeSeconds;
        }

        protected override void OnTick(float unscaledDeltaTime)
        {
            _simulationTimeSeconds += unscaledDeltaTime * simulationTimeScale;
        }

        public override CelestialPositionData GetMoveData(
            CoordinatesData coordinates)
        {
            var galaxy = GenerateGalaxy(coordinates);
            var location = ResolveGalaxyGenerator(galaxy)
                .GenerateSolarSystemLocation(galaxy, coordinates.SolarSystemID);

            return CelestialPositionData.FromSolarSystem(
                coordinates,
                location,
                double3.zero);
        }

        public override bool TryGetMoveData(
            CelestialBodyCoordinatesData bodyCoordinates,
            out CelestialPositionData positionData)
        {
            var coordinates = bodyCoordinates.SolarSystemCoordinates;
            if (!TryGetSolarSystem(
                    coordinates,
                    out var solarSystem,
                    out var totalSystemMassKg))
            {
                positionData = default;
                return false;
            }

            var bodyIndex = bodyCoordinates.CelestialBodyID;
            if (bodyIndex < 0 || bodyIndex >= solarSystem.StellarObjects.Length)
            {
                positionData = default;
                return false;
            }

            var body = solarSystem.StellarObjects[(int)bodyIndex];
            var positionMeters = GetObjectPositionMeters(
                body,
                totalSystemMassKg);

            var galaxy = GenerateGalaxy(coordinates);
            var location = ResolveGalaxyGenerator(galaxy)
                .GenerateSolarSystemLocation(galaxy, coordinates.SolarSystemID);

            positionData = new CelestialPositionData(
                bodyCoordinates,
                location.GalaxyLocalPositionLightYears,
                positionMeters,
                body.RadiusMeters);
            return true;
        }

        internal override double GetTemperatureLocal(
            in CoordinatesData coordinates,
            double3 solarSystemLocalPositionMeters)
        {
            if (!TryGetSolarSystem(
                    coordinates,
                    out var solarSystem,
                    out var totalSystemMassKg))
            {
                return StellarObjectData.CosmicBackgroundTemperatureKelvin;
            }

            var localTemperatureKelvin =
                StellarObjectData.CosmicBackgroundTemperatureKelvin;
            var totalFluxWattsPerSquareMeter = 0.0;
            var objects = solarSystem.StellarObjects;

            for (var index = 0; index < objects.Length; index++)
            {
                var source = objects[index];
                if (source == null)
                    continue;

                var sourcePosition = GetObjectPositionMeters(
                    source,
                    totalSystemMassKg);
                var delta = sourcePosition - solarSystemLocalPositionMeters;
                var distanceSquared = math.lengthsq(delta);
                var distanceMeters = Math.Sqrt(Math.Max(
                    distanceSquared,
                    MinimumDistanceSquaredMeters));
                var radiatingRadiusMeters = Math.Max(
                    source.RadiatingRadiusMeters,
                    0.0);

                // A local query inside an emitting body's generated thermal
                // extent is inside its material or disk. The source's own
                // characteristic temperature is the meaningful answer there;
                // the inverse-square approximation alone is not valid.
                if (radiatingRadiusMeters > 0.0 &&
                    distanceMeters <= radiatingRadiusMeters)
                {
                    localTemperatureKelvin = Math.Max(
                        localTemperatureKelvin,
                        source.TemperatureKelvin);
                }

                if (source.LuminosityWatts <= 0.0)
                    continue;

                var clampedDistanceSquared = Math.Max(
                    distanceSquared,
                    Math.Max(
                        radiatingRadiusMeters * radiatingRadiusMeters,
                        MinimumDistanceSquaredMeters));

                totalFluxWattsPerSquareMeter += source.LuminosityWatts /
                    (4.0 * Math.PI * clampedDistanceSquared);
            }

            if (totalFluxWattsPerSquareMeter <= 0.0)
                return localTemperatureKelvin;

            // A uniformly reradiating black body intercepts a disc but emits
            // over its entire surface, hence the factor of four.
            var equilibriumTemperatureKelvin = Math.Pow(
                totalFluxWattsPerSquareMeter /
                (4.0 * StefanBoltzmannConstant),
                0.25);

            return Math.Max(
                localTemperatureKelvin,
                equilibriumTemperatureKelvin);
        }

        internal override double3 GetGravitationVector(
            in CoordinatesData coordinates,
            double3 solarSystemLocalPositionMeters)
        {
            if (!TryGetSolarSystem(
                    coordinates,
                    out var solarSystem,
                    out var totalSystemMassKg))
            {
                return double3.zero;
            }

            var accelerationMetersPerSecondSquared = double3.zero;
            var objects = solarSystem.StellarObjects;
            for (var index = 0; index < objects.Length; index++)
            {
                var source = objects[index];
                if (source == null || source.MassKg <= 0.0)
                    continue;

                var sourcePosition = GetObjectPositionMeters(
                    source,
                    totalSystemMassKg);
                var sourceToObserver = sourcePosition -
                                       solarSystemLocalPositionMeters;
                var distanceSquared = math.lengthsq(sourceToObserver);
                if (distanceSquared <= MinimumDistanceSquaredMeters)
                    continue;

                var distanceMeters = Math.Sqrt(distanceSquared);
                var sourceRadiusMeters = Math.Max(source.RadiusMeters, 0.0);
                if (sourceRadiusMeters > 0.0 &&
                    distanceMeters < sourceRadiusMeters)
                {
                    // A uniform-density sphere keeps the query finite inside
                    // an object and gives zero acceleration at its centre.
                    accelerationMetersPerSecondSquared +=
                        sourceToObserver *
                        (SolarSystemOrbitUtility.GravitationalConstant *
                         source.MassKg /
                         (sourceRadiusMeters * sourceRadiusMeters *
                          sourceRadiusMeters));
                    continue;
                }

                accelerationMetersPerSecondSquared += sourceToObserver *
                    (SolarSystemOrbitUtility.GravitationalConstant *
                     source.MassKg /
                     (distanceSquared * distanceMeters));
            }

            return accelerationMetersPerSecondSquared;
        }

        internal override double GetGravitationForce(
            in CoordinatesData coordinates,
            double3 solarSystemLocalPositionMeters)
        {
            return math.length(GetGravitationVector(
                       coordinates,
                       solarSystemLocalPositionMeters)) /
                   StandardGravityMetersPerSecondSquared;
        }

        private bool TryGetSolarSystem(
            in CoordinatesData coordinates,
            out SolarSystemData solarSystem,
            out double totalSystemMassKg)
        {
            var configuration = Engine.Configuration;
            if (!SolarSystemGeneration.TryGenerate(
                    coordinates,
                    configuration.SolarSystemGenerators,
                    configuration.StellarObjectGenerators,
                    configuration.PlanetGenerators,
                    out solarSystem))
            {
                totalSystemMassKg = 0.0;
                return false;
            }

            totalSystemMassKg = SolarSystemGeneration.GetTotalSystemMassKg(
                solarSystem);
            return totalSystemMassKg > 0.0;
        }

        private double3 GetObjectPositionMeters(
            StellarObjectData data,
            double totalSystemMassKg)
        {
            return SolarSystemOrbitUtility.GetPositionMeters(
                data.Orbit,
                SolarSystemOrbitUtility.GravitationalConstant *
                totalSystemMassKg,
                _simulationTimeSeconds);
        }

        private SpaceEngine.Runtime.Data.Galaxy.GalaxyData GenerateGalaxy(
            in CoordinatesData coordinates)
        {
            var configuration = Engine.Configuration;
            var universePosition = LogicalCoordinatesResolver
                .ResolveGalaxyUniversePosition(
                    coordinates.UniverseID,
                    coordinates.GalaxyID);
            return SpaceEngine.Runtime.Generation.Universe.UniverseGeneration.GenerateGalaxy(
                configuration.GalaxyGenerators,
                coordinates.UniverseID,
                coordinates.GalaxyID,
                universePosition);
        }

        private GalaxyGenerator ResolveGalaxyGenerator(
            SpaceEngine.Runtime.Data.Galaxy.GalaxyData galaxy)
        {
            var configuration = Engine.Configuration;
            return SpaceEngine.Runtime.Generation.Universe.UniverseGeneration.ResolveGalaxyGenerator(
                configuration.GalaxyGenerators,
                galaxy.UniverseID,
                galaxy.GalaxyID,
                galaxy.UniversePositionLightYears);
        }
    }
}
