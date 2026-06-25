using System;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Galaxy;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Runtime.Streaming
{
    /// <summary>
    /// Authoritative hierarchical position for one ship or player.
    ///
    /// Local gameplay stays in metres relative to an active solar-system
    /// barycentre. Galaxy and universe positions are reconstructed only for
    /// large-scale streaming and rendering, where metre precision is neither
    /// needed nor representable in one coordinate value.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GalaxySpaceAnchor))]
    public sealed class SeamlessSpaceAnchor : MonoBehaviour
    {
        public const double MetersPerLightYear =
            9_460_730_472_580_800.0;

        [Header("Bridge to the galaxy streamer")]
        [SerializeField, HideInInspector]
        private GalaxySpaceAnchor galaxySpaceAnchor;

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
            _galaxy = GalaxyGenerator.Generate(
                coordinates.UniverseID,
                coordinates.GalaxyID);

            _activeSolarSystem = SolarSystemLocationGenerator.Generate(
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
        public void RebaseToSolarSystem(ulong solarSystemID)
        {
            if (!_isConfigured ||
                solarSystemID == _coordinates.SolarSystemID)
            {
                return;
            }

            var galaxyPosition = GalaxyLocalPositionLightYears;
            var nextSystem = SolarSystemLocationGenerator.Generate(
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

        internal void Configure(GalaxySpaceAnchor anchor)
        {
            galaxySpaceAnchor = anchor;
        }

        private void Awake()
        {
            galaxySpaceAnchor ??= GetComponent<GalaxySpaceAnchor>();
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
                    GalaxyLocalPositionLightYears);

                return;
            }

            galaxySpaceAnchor.SetGalaxyLocalPosition(
                GalaxyLocalPositionLightYears);
        }
    }
}
