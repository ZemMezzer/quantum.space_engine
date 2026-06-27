using System;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Streaming;
using SpaceEngine.Runtime.Streaming.Runtime.Anchors;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Core
{
    /// <summary>
    /// Default 3D anchor. It owns the seamless 3D reference-frame backend
    /// while the abstract CelestialAnchor base owns the gameplay API,
    /// movement helpers and lifecycle.
    /// </summary>
    public sealed class CelestialAnchor3D : CelestialAnchor
    {
        private readonly SeamlessSpaceAnchor _backend;
        private bool _isApplyingPosition;

        internal CelestialAnchor3D(SpaceEngine engine)
            : base(engine)
        {
            var galaxyAnchor = new GalaxySpaceAnchor(engine.Configuration);
            _backend = new SeamlessSpaceAnchor(galaxyAnchor);
            _backend.ActiveSolarSystemChanged +=
                HandleActiveSolarSystemChanged;
        }

        public override bool IsConfigured => _backend.IsConfigured;

        public override CoordinatesData Coordinates => _backend.Coordinates;

        public override double3 SolarSystemLocalPositionMeters =>
            _backend.SolarSystemLocalPositionMeters;

        public override double3 GalaxyLocalPositionLightYears =>
            _backend.GalaxyLocalPositionLightYears;

        /// <summary>
        /// Shared 3D reference-frame state used by the 3D renderer only.
        /// Gameplay remains on the abstract CelestialAnchor API.
        /// </summary>
        public SeamlessSpaceAnchor Backend => _backend;

        public override void Move(CelestialPositionData positionData)
        {
            ThrowIfDisposed();

            if (!positionData.IsValid)
            {
                throw new ArgumentException(
                    "Move data must identify a solar system or celestial body.",
                    nameof(positionData));
            }

            _isApplyingPosition = true;

            try
            {
                _backend.Configure(
                    positionData.Coordinates,
                    positionData.SolarSystemLocalPositionMeters);
            }
            finally
            {
                _isApplyingPosition = false;
            }

            SetPositionData(positionData, refreshRenderer: true);
        }

        public override void MoveByMeters(double3 deltaMeters)
        {
            ThrowIfDisposed();
            ThrowIfUnconfigured();

            if (math.lengthsq(deltaMeters) <= 0.0)
                return;

            _backend.MoveByMeters(deltaMeters);
            SynchronizePositionFromBackend(refreshRenderer: false);
        }

        public override void SetSolarSystemLocalPositionMeters(
            double3 positionMeters)
        {
            ThrowIfDisposed();
            ThrowIfUnconfigured();

            _backend.SetSolarSystemLocalPositionMeters(positionMeters);
            SynchronizePositionFromBackend(refreshRenderer: false);
        }

        protected override void OnDispose()
        {
            _backend.ActiveSolarSystemChanged -=
                HandleActiveSolarSystemChanged;
        }

        private void HandleActiveSolarSystemChanged(
            CoordinatesData coordinates)
        {
            if (_isApplyingPosition || !_backend.IsConfigured)
                return;

            // Streaming rebased the local frame to a neighbouring system.
            // The traveller has not teleported in galaxy space, but the
            // renderer must refresh its solar-system representation.
            SynchronizePositionFromBackend(refreshRenderer: true);
        }

        private void SynchronizePositionFromBackend(bool refreshRenderer)
        {
            if (!_backend.IsConfigured)
                return;

            SetPositionData(
                CelestialPositionData.FromSolarSystem(
                    _backend.Coordinates,
                    _backend.ActiveSolarSystem,
                    _backend.SolarSystemLocalPositionMeters),
                refreshRenderer);
        }

        private void ThrowIfUnconfigured()
        {
            if (!_backend.IsConfigured)
            {
                throw new InvalidOperationException(
                    "Move this celestial anchor to a solar system before " +
                    "performing local-frame movement.");
            }
        }
    }
}
