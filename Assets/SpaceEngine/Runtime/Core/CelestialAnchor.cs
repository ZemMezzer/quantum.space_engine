using System;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Physics;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Runtime.Core
{
    /// <summary>
    /// Base API for one traveller in a SpaceEngine instance.
    ///
    /// The caller owns an anchor. SpaceEngine never registers it, never keeps
    /// an anchor collection and never exposes an active-anchor property. An
    /// anchor receives its engine in the constructor and uses only the
    /// engine's internal API to resolve data or become renderer-observed.
    /// </summary>
    public abstract class CelestialAnchor : ICelestialAnchor
    {
        private CelestialPositionData _position;
        private bool _isInitialized;
        private bool _isDisposed;

        protected CelestialAnchor(SpaceEngine engine)
        {
            Engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        /// <summary>
        /// Owning engine available to derived implementations. This reference
        /// is one-way: the engine does not retain this anchor.
        /// </summary>
        protected SpaceEngine Engine { get; }

        protected CelestialPhysics Physics => Engine.GetPhysicsForAnchor();

        public bool IsDisposed => _isDisposed;

        public CelestialPositionData Position => _position;

        public event Action<CelestialPositionData> PositionChanged;

        public event Action<CelestialAnchor> Disposed;

        public abstract bool IsConfigured { get; }

        public abstract CoordinatesData Coordinates { get; }

        public abstract double3 SolarSystemLocalPositionMeters { get; }

        public abstract double3 GalaxyLocalPositionLightYears { get; }

        /// <summary>
        /// Resolves an addressable solar system into move data.
        /// Universe, galaxy and solar-system coordinates are deterministic
        /// addresses, so every coordinate value has a generated result.
        /// </summary>
        public CelestialPositionData GetMoveData(
            CoordinatesData coordinates)
        {
            ThrowIfDisposed();
            return Physics.GetMoveData(coordinates);
        }

        /// <summary>
        /// Resolves a celestial body into move data. A solar system always
        /// exists for its coordinates, but a requested body can be absent.
        /// This is the only conditional move-data query in the public API.
        /// </summary>
        public bool TryGetMoveData(
            CelestialBodyCoordinatesData bodyCoordinates,
            out CelestialPositionData positionData)
        {
            ThrowIfDisposed();

            return Physics.TryGetMoveData(
                bodyCoordinates,
                out positionData);
        }

        /// <summary>
        /// Returns the current local radiative-equilibrium temperature in
        /// kelvin. It is evaluated from the generated objects in the active
        /// system at the anchor's present position.
        ///
        /// An anchor that has not yet entered a solar-system frame is simply
        /// in deep space: this returns the cosmic microwave background instead
        /// of throwing.
        /// </summary>
        public double GetTemperatureLocal()
        {
            ThrowIfDisposed();

            return IsConfigured
                ? Physics.GetTemperatureLocal(
                    Coordinates,
                    SolarSystemLocalPositionMeters)
                : StellarObjectData.CosmicBackgroundTemperatureKelvin;
        }

        /// <summary>
        /// Returns the resultant local gravitational acceleration vector in
        /// m/s². The vector points toward the generated gravitating bodies.
        ///
        /// An anchor without an active solar-system frame is treated as deep
        /// space and returns a zero vector.
        /// </summary>
        public double3 GetGravitationVector()
        {
            ThrowIfDisposed();

            return IsConfigured
                ? Physics.GetGravitationVector(
                    Coordinates,
                    SolarSystemLocalPositionMeters)
                : double3.zero;
        }

        /// <summary>
        /// Returns the magnitude of local gravitational acceleration in
        /// standard Earth gravities (g). An unconfigured anchor is in deep
        /// space and therefore returns zero.
        /// </summary>
        public double GetGravitationForce()
        {
            ThrowIfDisposed();

            return IsConfigured
                ? Physics.GetGravitationForce(
                    Coordinates,
                    SolarSystemLocalPositionMeters)
                : 0.0;
        }

        /// <summary>
        /// Moves to a solar-system barycentre.
        /// </summary>
        public void Move(CoordinatesData coordinates)
        {
            Move(GetMoveData(coordinates));
        }

        /// <summary>
        /// Moves to a position relative to the target solar-system barycentre.
        /// Position is expressed in metres.
        /// </summary>
        public void Move(
            CoordinatesData coordinates,
            double3 solarSystemLocalPositionMeters)
        {
            Move(GetMoveData(coordinates).WithSolarSystemLocalPosition(
                solarSystemLocalPositionMeters));
        }

        /// <summary>
        /// Makes this anchor the one currently observed by the renderer.
        /// The renderer keeps that visual reference; SpaceEngine does not.
        /// Move the anchor to a system first.
        /// </summary>
        public void Activate()
        {
            ThrowIfDisposed();

            if (!IsConfigured)
            {
                throw new InvalidOperationException(
                    "Move this celestial anchor to a solar system before " +
                    "activating it for rendering.");
            }

            Engine.ObserveAnchor(this);

            // Activating an anchor must be sufficient to make it visible.
            // The renderer may have just created a new runtime for this
            // anchor, so request its first evaluation immediately instead of
            // waiting for the next streaming tick.
            Engine.RefreshObservedAnchor(this);
        }

        /// <summary>
        /// Requests an immediate renderer refresh when this anchor is the
        /// one observed by the renderer.
        /// </summary>
        public void Refresh()
        {
            ThrowIfDisposed();
            Engine.RefreshObservedAnchor(this);
        }

        /// <summary>
        /// Moves this anchor to already resolved move data.
        /// For a body, call TryGetMoveData first and pass the result here only
        /// when that call returns true.
        /// </summary>
        public abstract void Move(CelestialPositionData positionData);

        /// <summary>
        /// Moves inside the active local frame. Distance is in metres.
        /// </summary>
        public void Move(Vector3 direction, double distanceMeters)
        {
            ThrowIfDisposed();

            if (distanceMeters == 0.0 ||
                direction.sqrMagnitude <= 0.0000001f)
            {
                return;
            }

            var normalized = direction.normalized;
            MoveByMeters(new double3(
                normalized.x * distanceMeters,
                normalized.y * distanceMeters,
                normalized.z * distanceMeters));
        }

        public abstract void MoveByMeters(double3 deltaMeters);

        public abstract void SetSolarSystemLocalPositionMeters(
            double3 positionMeters);

        public void Dispose()
        {
            DisposeInternal();
            GC.SuppressFinalize(this);
        }

        internal void InitializeInternal()
        {
            if (_isInitialized || _isDisposed)
                return;

            _isInitialized = true;
            OnInitialize();
        }

        protected void SetPositionData(
            CelestialPositionData positionData,
            bool refreshRenderer)
        {
            _position = positionData;
            PositionChanged?.Invoke(_position);

            if (refreshRenderer && !_isDisposed)
                Engine.RefreshObservedAnchor(this);
        }

        protected void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(
                    GetType().Name,
                    "This celestial anchor has already been disposed.");
            }
        }

        protected virtual void OnInitialize()
        {
        }

        protected virtual void OnDispose()
        {
        }

        private void DisposeInternal()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            try
            {
                // Release only the renderer's optional observation reference.
                // SpaceEngine has no registry or ownership of this anchor.
                Engine.ReleaseObservedAnchor(this);
                OnDispose();
            }
            finally
            {
                PositionChanged = null;
                Disposed?.Invoke(this);
                Disposed = null;
            }
        }
    }
}
