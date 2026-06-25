using System;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Generation.Galaxy;
using SpaceEngine.Runtime.Generation.SolarSystem;
using SpaceEngine.Runtime.Streaming;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Runtime.Physics
{
    /// <summary>
    /// Current 3D simulation implementation. It owns the common simulation
    /// clock and resolves deterministic positions for anchors. It deliberately
    /// does not move Unity transforms: motion goes through CelestialAnchor,
    /// while the renderer reads the same anchor afterwards.
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
            var galaxy = GalaxyGenerator.Generate(
                coordinates.UniverseID,
                coordinates.GalaxyID);

            var location = SolarSystemLocationGenerator.Generate(
                galaxy,
                coordinates.SolarSystemID);

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
            var solarSystem = SolarSystemGenerator.Generate(coordinates);
            var bodyIndex = bodyCoordinates.CelestialBodyID;

            // The current generator has concrete physical data for planets.
            // Moons, asteroids and stations can extend this branch later
            // without changing the anchor API.
            if (bodyIndex < 0 ||
                bodyIndex >= solarSystem.PlanetCount)
            {
                positionData = default;
                return false;
            }

            var totalStarMassKg = 0.0;

            for (var starIndex = 0;
                 starIndex < solarSystem.StarCount;
                 starIndex++)
            {
                totalStarMassKg += solarSystem.Stars[starIndex].MassKg;
            }

            if (totalStarMassKg <= 0.0)
            {
                positionData = default;
                return false;
            }

            var planet = solarSystem.Planets[(int)bodyIndex];
            var positionMeters = SolarSystemOrbitUtility.GetPositionMeters(
                planet.Orbit,
                SolarSystemOrbitUtility.GravitationalConstant *
                totalStarMassKg,
                _simulationTimeSeconds);

            var galaxy = GalaxyGenerator.Generate(
                coordinates.UniverseID,
                coordinates.GalaxyID);

            var location = SolarSystemLocationGenerator.Generate(
                galaxy,
                coordinates.SolarSystemID);

            positionData = new CelestialPositionData(
                bodyCoordinates,
                location.GalaxyLocalPositionLightYears,
                positionMeters,
                planet.RadiusMeters);

            return true;
        }
    }
}
