using System;
using SpaceEngine.Runtime.Content;
using SpaceEngine.Runtime.Content.StellarObjects.Generation.Galaxies;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Coordinates;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Streaming.Runtime.Anchors
{
    public sealed class GalaxySpaceAnchor
    {
        private readonly SpaceEngineConfiguration configuration;
        private CoordinatesData _coordinates;
        private GalaxyData _galaxy;
        private double3 _galaxyLocalPositionLightYears;
        private bool _hasResolvedGalaxy;

        public GalaxySpaceAnchor(SpaceEngineConfiguration configuration)
        {
            this.configuration = configuration ??
                throw new ArgumentNullException(nameof(configuration));
        }

        internal SpaceEngineConfiguration Configuration => configuration;

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
            Configure(
                coordinates,
                ResolveGalaxy(coordinates),
                galaxyLocalPositionLightYears);
        }

        /// <summary>
        /// Configures a galaxy frame from data whose universe position has
        /// already been resolved by universe streaming. This keeps a physical
        /// approach to a map galaxy continuous when the anchor crosses from
        /// the universe frame into that galaxy's local frame.
        /// </summary>
        public void Configure(
            CoordinatesData coordinates,
            GalaxyData galaxy,
            double3 galaxyLocalPositionLightYears)
        {
            _coordinates = coordinates;
            _galaxy = galaxy;
            _galaxyLocalPositionLightYears =
                galaxyLocalPositionLightYears;
            _hasResolvedGalaxy = true;
        }

        /// <summary>
        /// Resolves the selected galaxy and places the anchor at a known
        /// solar system. The supplied SolarSystemID should originate from
        /// the selected GalaxyGenerator, a scanner, a map or saved coordinates.
        /// </summary>
        public void ConfigureAtSolarSystem(
            CoordinatesData coordinates)
        {
            _coordinates = coordinates;

            _galaxy = ResolveGalaxy(coordinates);

            var location = ResolveGalaxyGenerator(_galaxy).GenerateSolarSystemLocation(
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

        private GalaxyData ResolveGalaxy(in CoordinatesData coordinates)
        {
            var universePosition = LogicalCoordinatesResolver
                .ResolveGalaxyUniversePosition(
                    coordinates.UniverseID,
                    coordinates.GalaxyID);
            return Generation.Universe.UniverseGeneration.GenerateGalaxy(
                configuration.GalaxyGenerators,
                coordinates.UniverseID,
                coordinates.GalaxyID,
                universePosition);
        }

        private GalaxyGenerator
            ResolveGalaxyGenerator(GalaxyData galaxy)
        {
            return Generation.Universe.UniverseGeneration.ResolveGalaxyGenerator(
                configuration.GalaxyGenerators,
                galaxy.UniverseID,
                galaxy.GalaxyID,
                galaxy.UniversePositionLightYears);
        }
    }
}