using System;
using SpaceEngine.Runtime.Physics;
using SpaceEngine.Runtime.Streaming;
using SpaceEngine.Runtime.Content;
using UnityEngine;

namespace SpaceEngine.Runtime.Core
{
    /// <summary>
    /// Root coordinator for one celestial simulation.
    ///
    /// SpaceEngine owns only shared simulation services and the Unity frame
    /// loop. It deliberately does not retain, enumerate or select anchors.
    /// A caller creates an anchor, stores it itself and then the anchor talks
    /// back to this engine through its internal service API.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public class SpaceEngine : MonoBehaviour
    {
        [SerializeField] private CelestialPhysics physics;
        [SerializeField] private CelestialRenderer renderer;

        [Header("Content configuration")]
        [SerializeField] private SpaceEngineConfiguration configuration;

        private bool _isInitialized;
        private bool _isShuttingDown;

        /// <summary>
        /// Shared simulation time exposed for inspection only. Gameplay
        /// movement and location changes stay on CelestialAnchor.
        /// </summary>
        public double SimulationTimeSeconds =>
            physics == null ? 0.0 : physics.SimulationTimeSeconds;

        /// <summary>
        /// Pure authored configuration shared by generation and rendering.
        /// It contains only generator/entity bindings and system-structure
        /// generators; runtime data is always returned from generators instead
        /// of stored here.
        /// </summary>
        public SpaceEngineConfiguration Configuration => configuration;

        private void Awake()
        {
            if (configuration == null ||
                configuration.GalaxyGenerators.Count == 0 ||
                configuration.SolarSystemGenerators.Count == 0 ||
                configuration.StellarObjectGenerators.Count == 0)
            {
                Debug.LogError(
                    "SpaceEngine requires a SpaceEngineConfiguration with at " +
                    "least one galaxy entity/generator binding, SolarSystemGenerator " +
                    "and stellar-object entity/generator binding assigned.",
                    this);
                enabled = false;
                return;
            }

            physics ??= GetComponent<CelestialPhysics>();
            renderer ??= GetComponent<CelestialRenderer>();

            if (physics == null || renderer == null)
            {
                Debug.LogError(
                    "SpaceEngine requires one CelestialPhysics and one " +
                    "CelestialRenderer implementation.",
                    this);

                enabled = false;
                return;
            }

            physics.Initialize(this);
            renderer.Initialize(this);
            _isInitialized = true;
        }

        private void Update()
        {
            if (!_isInitialized)
                return;

            physics.Tick(Time.unscaledDeltaTime);
            renderer.TickStreaming(Time.unscaledTime);
        }

        private void LateUpdate()
        {
            if (!_isInitialized)
                return;

            renderer.TickVisuals(
                Time.deltaTime,
                physics.SimulationTimeSeconds);
        }

        private void OnDestroy()
        {
            _isShuttingDown = true;
            _isInitialized = false;

            // Anchors are not owned by SpaceEngine. Callers dispose their own
            // anchors; renderer shutdown only releases its current reference.
            renderer?.Shutdown();
            physics?.Shutdown();

        }

        /// <summary>
        /// Creates and returns a new anchor without retaining it. The caller
        /// owns the returned IDisposable instance and must keep its reference.
        /// </summary>
        public CelestialAnchor CreateAnchor()
        {
            EnsureAvailable();

            var anchor = CreateAnchorImplementation();

            if (anchor == null)
            {
                throw new InvalidOperationException(
                    "CreateAnchorImplementation returned null.");
            }

            anchor.InitializeInternal();
            return anchor;
        }

        /// <summary>
        /// Override in a specialised engine to provide another anchor type.
        /// The default runtime is 3D, so it creates CelestialAnchor3D.
        /// </summary>
        protected virtual CelestialAnchor CreateAnchorImplementation()
        {
            return new CelestialAnchor3D(this);
        }

        // Everything below is intentionally internal. Only an anchor that was
        // created by this engine receives access to these service operations.
        // None of these methods stores the passed anchor on SpaceEngine.

        internal CelestialPhysics GetPhysicsForAnchor()
        {
            EnsureAvailable();
            return physics;
        }

        internal void ObserveAnchor(CelestialAnchor anchor)
        {
            EnsureAvailable();

            if (anchor == null)
                throw new ArgumentNullException(nameof(anchor));

            renderer.SetAnchor(anchor);
        }

        internal void RefreshObservedAnchor(CelestialAnchor anchor)
        {
            if (!_isInitialized || _isShuttingDown || anchor == null)
                return;

            renderer.RefreshAnchor(anchor);
        }

        internal void ReleaseObservedAnchor(CelestialAnchor anchor)
        {
            if (!_isInitialized || _isShuttingDown || anchor == null)
                return;

            renderer.ClearAnchor(anchor);
        }

        private void Reset()
        {
            physics = GetComponent<CelestialPhysics3D>();
            renderer = GetComponent<CelestialRenderer>();
        }

        private void EnsureAvailable()
        {
            if (!_isInitialized || _isShuttingDown)
            {
                throw new InvalidOperationException(
                    "The owning SpaceEngine is not available. Create and use " +
                    "anchors only after the engine has initialized.");
            }
        }
    }
}
