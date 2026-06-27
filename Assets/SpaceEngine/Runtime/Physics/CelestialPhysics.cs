using SpaceEngine.Runtime.Data;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Runtime.Physics
{
    /// <summary>
    /// Base contract for one celestial simulation. SpaceEngine owns the frame
    /// loop. Anchors use their owning SpaceEngine to resolve deterministic
    /// system positions, while physics remains independent from whichever
    /// anchor is currently rendered.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Core.SpaceEngine))]
    public abstract class CelestialPhysics : MonoBehaviour
    {
        public Core.SpaceEngine Engine { get; private set; }

        /// <summary>
        /// Shared simulation clock in seconds. Renderers use this value when
        /// they evaluate generated orbital positions.
        /// </summary>
        public abstract double SimulationTimeSeconds { get; }

        internal void Initialize(Core.SpaceEngine engine)
        {
            Engine = engine;
            OnInitialize();
        }

        internal void Tick(float unscaledDeltaTime)
        {
            OnTick(Mathf.Max(0.0f, unscaledDeltaTime));
        }

        internal void Shutdown()
        {
            OnShutdown();
            Engine = null;
        }

        /// <summary>
        /// Resolves a deterministic universe/galaxy/solar-system address.
        /// Every valid coordinate value maps to a generated solar system.
        /// </summary>
        public abstract CelestialPositionData GetMoveData(
            CoordinatesData coordinates);

        /// <summary>
        /// Resolves a body inside an existing generated solar system.
        /// A requested body can be absent, so this query is conditional.
        /// </summary>
        public abstract bool TryGetMoveData(
            CelestialBodyCoordinatesData bodyCoordinates,
            out CelestialPositionData positionData);

        /// <summary>
        /// Returns local radiative-equilibrium temperature in kelvin at a
        /// solar-system-local position. The implementation derives it from
        /// the generated luminosities at the current simulation time.
        /// </summary>
        internal abstract double GetTemperatureLocal(
            in CoordinatesData coordinates,
            double3 solarSystemLocalPositionMeters);

        /// <summary>
        /// Returns the resultant gravitational acceleration vector in m/s² at
        /// a solar-system-local position. Its direction points toward the
        /// sources of gravity.
        /// </summary>
        internal abstract double3 GetGravitationVector(
            in CoordinatesData coordinates,
            double3 solarSystemLocalPositionMeters);

        /// <summary>
        /// Returns the magnitude of local gravitational acceleration in
        /// standard Earth gravities (g).
        /// </summary>
        internal abstract double GetGravitationForce(
            in CoordinatesData coordinates,
            double3 solarSystemLocalPositionMeters);

        protected virtual void OnInitialize()
        {
        }

        protected abstract void OnTick(float unscaledDeltaTime);

        protected virtual void OnShutdown()
        {
        }
    }
}
