using System;
using SpaceEngine.Runtime.Content;
using SpaceEngine.Runtime.Content.StellarObjects.Generation.Galaxies;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Coordinates;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Streaming.Runtime.Anchors
{
    public sealed class SeamlessSpaceAnchor
    {
        public const double MetersPerLightYear =
            9_460_730_472_580_800.0;

        public SeamlessSpaceAnchor(GalaxySpaceAnchor galaxySpaceAnchor)
        {
            this.galaxySpaceAnchor = galaxySpaceAnchor ??
                throw new ArgumentNullException(nameof(galaxySpaceAnchor));
            configuration = galaxySpaceAnchor.Configuration;
        }
        private readonly GalaxySpaceAnchor galaxySpaceAnchor;
        private readonly SpaceEngineConfiguration configuration;

        public GalaxySpaceAnchor GalaxyAnchor => galaxySpaceAnchor;
        internal SpaceEngineConfiguration Configuration => configuration;

        private CoordinatesData _coordinates;
        private GalaxyData _galaxy;
        private SolarSystemLocationData _activeSolarSystem;
        private double3 _solarSystemLocalPositionMeters;
        private bool _isConfigured;

        /// <summary>
        /// Invoked after the active solar-system frame changes. The physical
        /// galaxy position remains unchanged during a rebase.
        /// </summary>
        public event Action<CoordinatesData> ActiveSolarSystemChanged;

        public bool IsConfigured => _isConfigured;

        public CoordinatesData Coordinates => _coordinates;

        public GalaxyData Galaxy => _galaxy;

        public SolarSystemLocationData ActiveSolarSystem =>
            _activeSolarSystem;

        /// <summary>
        /// Exact ship position relative to the active system barycentre.
        /// Unit: metres.
        /// </summary>
        public double3 SolarSystemLocalPositionMeters =>
            _solarSystemLocalPositionMeters;

        /// <summary>
        /// Galaxy-local position reconstructed for galaxy-scale work.
        /// Unit: light-years. Do not use this for close-range physics.
        /// </summary>
        public double3 GalaxyLocalPositionLightYears =>
            _activeSolarSystem.GalaxyLocalPositionLightYears +
            _solarSystemLocalPositionMeters / MetersPerLightYear;

        /// <summary>
        /// Universe-space position reconstructed for universe-scale rendering.
        /// Unit: light-years. It intentionally has only large-scale precision.
        /// </summary>
        public double3 UniversePositionLightYears =>
            _galaxy.UniversePositionLightYears +
            GalaxyLocalPositionLightYears;

        public void Configure(
            CoordinatesData coordinates,
            double3 solarSystemLocalPositionMeters)
        {
            _coordinates = coordinates;
            _galaxy = ResolveGalaxy(coordinates);

            _activeSolarSystem = ResolveGalaxyGenerator(_galaxy).GenerateSolarSystemLocation(
                    _galaxy,
                    coordinates.SolarSystemID);

            _solarSystemLocalPositionMeters =
                solarSystemLocalPositionMeters;

            _isConfigured = true;
            SynchronizeGalaxyAnchor(forceConfigure: true);

            ActiveSolarSystemChanged?.Invoke(_coordinates);
        }

        /// <summary>
        /// Moves the authoritative ship position. This is the method a ship
        /// controller should call; it never moves astronomical Unity objects.
        /// </summary>
        public void MoveByMeters(double3 deltaMeters)
        {
            if (!_isConfigured)
                return;

            _solarSystemLocalPositionMeters += deltaMeters;
            SynchronizeGalaxyAnchor(forceConfigure: false);
        }

        public void SetSolarSystemLocalPositionMeters(
            double3 positionMeters)
        {
            if (!_isConfigured)
                return;

            _solarSystemLocalPositionMeters = positionMeters;
            SynchronizeGalaxyAnchor(forceConfigure: false);
        }

        /// <summary>
        /// Switches the local reference frame to another known stellar system
        /// without moving the ship in galaxy space.
        /// </summary>
        public void RebaseToSolarSystem(long solarSystemID)
        {
            if (!_isConfigured ||
                solarSystemID == _coordinates.SolarSystemID)
            {
                return;
            }

            var galaxyPosition = GalaxyLocalPositionLightYears;
            var nextSystem = ResolveGalaxyGenerator(_galaxy).GenerateSolarSystemLocation(
                    _galaxy,
                    solarSystemID);

            _coordinates = new CoordinatesData(
                _coordinates.UniverseID,
                _coordinates.GalaxyID,
                solarSystemID);

            _activeSolarSystem = nextSystem;
            _solarSystemLocalPositionMeters =
                (galaxyPosition -
                 nextSystem.GalaxyLocalPositionLightYears) *
                MetersPerLightYear;

            SynchronizeGalaxyAnchor(forceConfigure: true);
            ActiveSolarSystemChanged?.Invoke(_coordinates);
        }

        /// <summary>
        /// Changes the active galaxy frame without moving the traveller in
        /// universe space. It is the universe-scale counterpart of
        /// RebaseToSolarSystem: an approaching external galaxy becomes the
        /// real active galaxy, so its gas and stellar streaming render rather
        /// than leaving the player on a never-ending proxy billboard.
        /// </summary>
        public bool RebaseToGalaxy(in GalaxyLocationData galaxyLocation)
        {
            if (!_isConfigured ||
                galaxyLocation.GalaxyID == _coordinates.GalaxyID)
            {
                return false;
            }

            var universePosition = UniversePositionLightYears;
            var targetGalaxy = SpaceEngine.Runtime.Generation.Universe.UniverseGeneration.GenerateGalaxy(
                configuration.GalaxyGenerators,
                _coordinates.UniverseID,
                galaxyLocation.GalaxyID,
                galaxyLocation.UniversePositionLightYears);
            var targetGalaxyLocalPosition = universePosition -
                                            targetGalaxy
                                                .UniversePositionLightYears;

            // Keep the existing logical solar-system address. Every ID is a
            // deterministic valid system and its local-frame offset preserves
            // the traveller's exact universe position during the rebase.
            var targetCoordinates = new CoordinatesData(
                _coordinates.UniverseID,
                galaxyLocation.GalaxyID,
                _coordinates.SolarSystemID);
            var targetSystem = ResolveGalaxyGenerator(targetGalaxy).GenerateSolarSystemLocation(
                    targetGalaxy,
                    targetCoordinates.SolarSystemID);

            _coordinates = targetCoordinates;
            _galaxy = targetGalaxy;
            _activeSolarSystem = targetSystem;
            _solarSystemLocalPositionMeters =
                (targetGalaxyLocalPosition -
                 targetSystem.GalaxyLocalPositionLightYears) *
                MetersPerLightYear;

            SynchronizeGalaxyAnchor(forceConfigure: true);
            ActiveSolarSystemChanged?.Invoke(_coordinates);
            return true;
        }

        /// <summary>
        /// Returns the current ship-to-system-barycentre distance in metres.
        /// </summary>
        public double GetDistanceToActiveSolarSystemMeters()
        {
            return math.length(_solarSystemLocalPositionMeters);
        }

        /// <summary>
        /// Returns a position relative to the ship, in metres, for any known
        /// solar-system location in the same galaxy.
        /// </summary>
        public double3 GetRelativePositionToSolarSystemMeters(
            in SolarSystemLocationData solarSystem)
        {
            return (solarSystem.GalaxyLocalPositionLightYears -
                    _activeSolarSystem.GalaxyLocalPositionLightYears) *
                   MetersPerLightYear -
                   _solarSystemLocalPositionMeters;
        }

        /// <summary>
        /// Returns a position relative to the ship in universe-space
        /// light-years. Use it only for universe-frame rendering.
        /// </summary>
        public double3 GetRelativePositionToGalaxyLightYears(
            in GalaxyLocationData galaxy)
        {
            return galaxy.UniversePositionLightYears -
                   UniversePositionLightYears;
        }


        private GalaxyData ResolveGalaxy(in CoordinatesData coordinates)
        {
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
            ResolveGalaxyGenerator(GalaxyData galaxy)
        {
            return SpaceEngine.Runtime.Generation.Universe.UniverseGeneration.ResolveGalaxyGenerator(
                configuration.GalaxyGenerators,
                galaxy.UniverseID,
                galaxy.GalaxyID,
                galaxy.UniversePositionLightYears);
        }

        private void SynchronizeGalaxyAnchor(bool forceConfigure)
        {
            if (galaxySpaceAnchor == null)
                return;

            if (forceConfigure ||
                !galaxySpaceAnchor.HasResolvedGalaxy ||
                galaxySpaceAnchor.Coordinates != _coordinates)
            {
                galaxySpaceAnchor.Configure(
                    _coordinates,
                    _galaxy,
                    GalaxyLocalPositionLightYears);

                return;
            }

            galaxySpaceAnchor.SetGalaxyLocalPosition(
                GalaxyLocalPositionLightYears);
        }
    }
}