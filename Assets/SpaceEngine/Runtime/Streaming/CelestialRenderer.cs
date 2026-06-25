using SpaceEngine.Runtime.Core;
using UnityEngine;

namespace SpaceEngine.Runtime.Streaming
{
    /// <summary>
    /// Base contract for one celestial visual implementation. SpaceEngine owns
    /// the frame loop; renderers do not own Update or LateUpdate. The renderer
    /// itself keeps the currently observed anchor; SpaceEngine does not.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Core.SpaceEngine))]
    public abstract class CelestialRenderer : MonoBehaviour
    {
        public Core.SpaceEngine Engine { get; private set; }

        public CelestialAnchor Anchor { get; private set; }

        public abstract bool IsReady { get; }

        internal void Initialize(Core.SpaceEngine engine)
        {
            Engine = engine;
            OnInitialize();
        }

        internal void SetAnchor(CelestialAnchor anchor)
        {
            if (ReferenceEquals(Anchor, anchor))
                return;

            Anchor = anchor;
            OnAnchorChanged();
        }

        internal void ClearAnchor(CelestialAnchor expectedAnchor)
        {
            if (!ReferenceEquals(Anchor, expectedAnchor))
                return;

            Anchor = null;
            OnAnchorChanged();
        }

        internal void RefreshAnchor(CelestialAnchor anchor)
        {
            if (!ReferenceEquals(Anchor, anchor))
                return;

            OnEvaluateNow();
        }

        internal void TickStreaming(float unscaledTime)
        {
            OnTickStreaming(unscaledTime);
        }

        internal void TickVisuals(
            float deltaTime,
            double simulationTimeSeconds)
        {
            OnTickVisuals(deltaTime, simulationTimeSeconds);
        }

        internal void EvaluateNow()
        {
            OnEvaluateNow();
        }

        internal void Shutdown()
        {
            OnShutdown();
            Anchor = null;
            Engine = null;
        }

        protected abstract void OnInitialize();

        protected abstract void OnTickStreaming(float unscaledTime);

        protected abstract void OnTickVisuals(
            float deltaTime,
            double simulationTimeSeconds);

        protected abstract void OnAnchorChanged();

        protected abstract void OnEvaluateNow();

        protected abstract void OnShutdown();
    }
}
