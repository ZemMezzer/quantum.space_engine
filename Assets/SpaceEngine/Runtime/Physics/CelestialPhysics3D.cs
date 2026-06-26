using System;
using SpaceEngine.Runtime.Content.StellarObjects.Generation.Galaxies;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Generation.Coordinates;
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
            var configuration = Engine.Configuration;
            if (!SpaceEngine.Runtime.Generation.SolarSystem.SolarSystemGeneration
                    .TryGenerate(
                        coordinates,
                        configuration.SolarSystemGenerators,
                        configuration.StellarObjectGenerators,
                        configuration.PlanetGenerators,
                        out var solarSystem))
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

            var totalSystemMassKg = SpaceEngine.Runtime.Generation.SolarSystem.SolarSystemGeneration
                .GetTotalSystemMassKg(solarSystem);
            if (totalSystemMassKg <= 0.0)
            {
                positionData = default;
                return false;
            }

            var body = solarSystem.StellarObjects[(int)bodyIndex];
            var positionMeters = SolarSystemOrbitUtility.GetPositionMeters(
                body.Orbit,
                SolarSystemOrbitUtility.GravitationalConstant * totalSystemMassKg,
                _simulationTimeSeconds);

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

        private GalaxyGenerator
            ResolveGalaxyGenerator(
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
