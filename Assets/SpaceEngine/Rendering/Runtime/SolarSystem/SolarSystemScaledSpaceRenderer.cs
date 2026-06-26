using System;
using System.Collections.Generic;
using SpaceEngine.Runtime.Content;
using SpaceEngine.Rendering.Content;
using SpaceEngine.Runtime.Content.StellarObjects.Rendering;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Generation.SolarSystem;
using SpaceEngine.Runtime.Physics;
using SpaceEngine.Runtime.Streaming.Runtime.Anchors;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Rendering.Runtime.SolarSystem
{
    public sealed class SolarSystemScaledSpaceRenderer : IDisposable
    {
        private readonly Transform _parent;
        private readonly SpaceEngineConfiguration _contentConfiguration;
        private readonly CelestialRenderConfiguration _renderConfiguration;
        private readonly List<VisualBinding> _visuals = new();

        private SeamlessSpaceAnchor _anchor;
        private Camera _camera;
        private LayerMask _layer;
        private Transform _visualRoot;
        private SolarSystemData _solarSystem;
        private ulong _loadedSystemSeed;
        private bool _hasSystem;
        private bool _isVisible;
        private double _metersPerUnityUnit = 10_000_000.0;
        private double _simulationTimeSeconds;

        private sealed class VisualBinding
        {
            public StellarObjectData Data;
            public int ObjectIndex;
            public IStellarObjectVisual Visual;
        }

        public SolarSystemScaledSpaceRenderer(
            Transform parent,
            SpaceEngineConfiguration contentConfiguration,
            CelestialRenderConfiguration renderConfiguration)
        {
            _parent = parent ?? throw new ArgumentNullException(
                nameof(parent));
            _contentConfiguration = contentConfiguration ??
                throw new ArgumentNullException(
                    nameof(contentConfiguration));
            _renderConfiguration = renderConfiguration ??
                throw new ArgumentNullException(
                    nameof(renderConfiguration));
        }

        public void Configure(
            SeamlessSpaceAnchor streamingAnchor,
            Camera renderCamera,
            LayerMask renderLayer,
            double metersPerUnityUnit)
        {
            _anchor = streamingAnchor;
            _camera = renderCamera;
            _layer = renderLayer;
            _metersPerUnityUnit = Math.Max(1.0, metersPerUnityUnit);
            EnsureVisualRoot();
        }

        public void Tick(double simulationTimeSeconds)
        {
            if (!_isVisible || _anchor == null || !_anchor.IsConfigured)
                return;

            _simulationTimeSeconds = simulationTimeSeconds;
            EnsureSystemData();
            UpdateVisuals(immediate: false);
        }

        public void SetScaledSpaceVisible(bool isVisible)
        {
            _isVisible = isVisible;
            EnsureVisualRoot();

            if (_visualRoot != null)
                _visualRoot.gameObject.SetActive(isVisible);

            if (isVisible)
                RefreshNow();
        }

        public void RefreshNow()
        {
            if (_anchor == null || !_anchor.IsConfigured)
                return;

            EnsureSystemData();
            if (_isVisible)
                UpdateVisuals(immediate: true);
        }

        /// <summary>
        /// The system's index-zero visual owns the LOD0 handoff. This is an
        /// index convention, not an assumption about it being a star.
        /// </summary>
        public bool IsPrimaryVisualReadyAt(
            float requiredAngularDiameterDegrees)
        {
            for (var index = 0; index < _visuals.Count; index++)
            {
                var binding = _visuals[index];
                if (binding.ObjectIndex != 0 || binding.Visual == null)
                    continue;

                return binding.Visual.IsDistantPointReplacementReady(
                    requiredAngularDiameterDegrees);
            }

            return false;
        }

        public void Dispose()
        {
            ClearVisuals();
        }

        private void EnsureSystemData()
        {
            var coordinates = _anchor.Coordinates;
            var requestedSeed = coordinates.GetSolarSystemSeed();

            if (_hasSystem && _loadedSystemSeed == requestedSeed)
                return;

            ClearVisuals();
            _hasSystem = SolarSystemGeneration.TryGenerate(
                coordinates,
                _contentConfiguration.SolarSystemGenerators,
                _contentConfiguration.StellarObjectGenerators,
                _contentConfiguration.PlanetGenerators,
                out _solarSystem);
            _loadedSystemSeed = requestedSeed;

            if (_hasSystem)
                RebuildVisuals();
        }

        private void RebuildVisuals()
        {
            if (_solarSystem?.StellarObjects == null)
                return;

            for (var index = 0;
                 index < _solarSystem.StellarObjects.Length;
                 index++)
            {
                var data = _solarSystem.StellarObjects[index];
                if (data == null)
                    continue;

                var renderer = ContentRendererSelection
                    .SelectStellarObjectRendererOrNull(
                        _renderConfiguration.StellarObjectRenderers,
                        data.Entity);

                if (renderer == null)
                    continue;

                var context = new StellarObjectRenderContext(
                    _visualRoot,
                    _camera,
                    GetLayerIndex(),
                    _metersPerUnityUnit,
                    data,
                    index);

                var visual = renderer.CreateVisual(context);
                if (visual == null)
                    continue;

                visual.SetVisible(_isVisible);
                _visuals.Add(new VisualBinding
                {
                    Data = data,
                    ObjectIndex = index,
                    Visual = visual
                });
            }
        }

        private void UpdateVisuals(bool immediate)
        {
            if (_solarSystem?.StellarObjects == null ||
                _visuals.Count == 0 ||
                _anchor == null)
            {
                return;
            }

            var totalMassKg = SolarSystemGeneration.GetTotalSystemMassKg(
                _solarSystem);
            if (totalMassKg <= 0.0)
                return;

            var gravitationalParameter =
                SolarSystemOrbitUtility.GravitationalConstant *
                totalMassKg;

            for (var index = 0; index < _visuals.Count; index++)
            {
                var binding = _visuals[index];
                if (binding.Visual == null || binding.Data == null)
                    continue;

                var barycentricPosition =
                    SolarSystemOrbitUtility.GetPositionMeters(
                        binding.Data.Orbit,
                        gravitationalParameter,
                        _simulationTimeSeconds);
                var relativePosition = barycentricPosition -
                                       _anchor
                                           .SolarSystemLocalPositionMeters;
                var distanceMeters = math.length(relativePosition);

                binding.Visual.Update(
                    new StellarObjectVisualUpdateContext(
                        binding.Data,
                        binding.ObjectIndex,
                        _camera,
                        barycentricPosition,
                        relativePosition,
                        distanceMeters,
                        _simulationTimeSeconds,
                        _metersPerUnityUnit,
                        immediate));
            }
        }

        private void EnsureVisualRoot()
        {
            if (_visualRoot != null)
                return;

            var rootObject = new GameObject("Solar System Visual Root")
            {
                layer = GetLayerIndex()
            };

            _visualRoot = rootObject.transform;
            _visualRoot.SetParent(_parent, false);
        }

        private int GetLayerIndex()
        {
            var value = _layer.value;
            if (value == 0)
                return 0;

            for (var index = 0; index < 32; index++)
            {
                if ((value & (1 << index)) != 0)
                    return index;
            }

            return 0;
        }

        private void ClearVisuals()
        {
            for (var index = 0; index < _visuals.Count; index++)
                _visuals[index].Visual?.Dispose();

            _visuals.Clear();
            _solarSystem = null;
            _hasSystem = false;
        }
    }
}
