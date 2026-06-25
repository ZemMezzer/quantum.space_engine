using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Galaxy;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Runtime.Streaming
{
    /// <summary>
    /// Exact player or camera position in one galaxy-local reference frame.
    /// Configure it from a known CoordinatesData before enabling a
    /// GalaxySpaceStreamer.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GalaxySpaceAnchor : MonoBehaviour
    {
        private CoordinatesData _coordinates;
        private GalaxyData _galaxy;
        private double3 _galaxyLocalPositionLightYears;
        private bool _hasResolvedGalaxy;

        public CoordinatesData Coordinates => _coordinates;

        public GalaxyData Galaxy => _galaxy;

        public bool HasResolvedGalaxy => _hasResolvedGalaxy;

        public double3 GalaxyLocalPositionLightYears =>
            _galaxyLocalPositionLightYears;

        /// <summary>
        /// Resolves the selected galaxy and sets an exact local position.
        /// </summary>
        public void Configure(
            CoordinatesData coordinates,
            double3 galaxyLocalPositionLightYears)
        {
            _coordinates = coordinates;

            _galaxy = GalaxyGenerator.Generate(
                coordinates.UniverseID,
                coordinates.GalaxyID);

            _galaxyLocalPositionLightYears =
                galaxyLocalPositionLightYears;

            _hasResolvedGalaxy = true;
        }

        /// <summary>
        /// Resolves the selected galaxy and places the anchor at a known
        /// solar system. The supplied SolarSystemID should originate from
        /// GalaxySectorGenerator, a scanner, a map or saved coordinates.
        /// </summary>
        public void ConfigureAtSolarSystem(
            CoordinatesData coordinates)
        {
            _coordinates = coordinates;

            _galaxy = GalaxyGenerator.Generate(
                coordinates.UniverseID,
                coordinates.GalaxyID);

            var location = SolarSystemLocationGenerator.Generate(
                _galaxy,
                coordinates.SolarSystemID);

            _galaxyLocalPositionLightYears =
                location.GalaxyLocalPositionLightYears;

            _hasResolvedGalaxy = true;
        }

        public void SetGalaxyLocalPosition(
            double3 galaxyLocalPositionLightYears)
        {
            _galaxyLocalPositionLightYears =
                galaxyLocalPositionLightYears;
        }

        public void MoveByLightYears(
            double3 deltaLightYears)
        {
            _galaxyLocalPositionLightYears +=
                deltaLightYears;
        }
    }
}
