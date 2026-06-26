using System;
using SpaceEngine.Rendering.Runtime.Universe;
using SpaceEngine.Runtime.Streaming.Runtime.Anchors;
using UnityEngine;

namespace SpaceEngine.Rendering.Runtime.Anchors
{
    public sealed class UniverseGalaxyStreamingService
    {
        private SeamlessSpaceAnchor _spaceAnchor;
        private UniverseGalaxyFieldRenderer _universeRenderer;
        private float _proximityCheckIntervalSeconds = 0.15f;
        private double _activationDistanceInRadii = 1.20;
        private float _nextProximityCheckTime;

        internal void Configure(
            SeamlessSpaceAnchor anchor,
            UniverseGalaxyFieldRenderer renderer,
            double activationDistanceMultiplier)
        {
            _spaceAnchor = anchor;
            _universeRenderer = renderer;
            _activationDistanceInRadii = Math.Max(
                1.0,
                activationDistanceMultiplier);
            _proximityCheckIntervalSeconds = Mathf.Max(
                0.01f,
                _proximityCheckIntervalSeconds);
        }

        public void Tick(float unscaledTime)
        {
            if (_spaceAnchor == null || !_spaceAnchor.IsConfigured ||
                _universeRenderer == null)
            {
                return;
            }

            if (unscaledTime < _nextProximityCheckTime)
                return;

            _nextProximityCheckTime = unscaledTime +
                                      _proximityCheckIntervalSeconds;
            EvaluateNow();
        }

        public void EvaluateNow()
        {
            if (_spaceAnchor == null || !_spaceAnchor.IsConfigured ||
                _universeRenderer == null)
            {
                return;
            }

            if (!_universeRenderer.TryFindGalaxyForHandoff(
                    _activationDistanceInRadii,
                    out var galaxyLocation))
            {
                return;
            }

            if (!_spaceAnchor.RebaseToGalaxy(galaxyLocation))
                return;
            _universeRenderer.ForceRefresh();
        }
    }
}