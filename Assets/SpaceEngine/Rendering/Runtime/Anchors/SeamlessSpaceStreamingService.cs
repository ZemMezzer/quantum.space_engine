using System;
using SpaceEngine.Rendering.Runtime.Galaxy;
using SpaceEngine.Rendering.Runtime.SolarSystem;
using SpaceEngine.Runtime.Content;
using SpaceEngine.Rendering.Content;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.SolarSystem;
using SpaceEngine.Runtime.Streaming.Runtime.Anchors;
using UnityEngine;

namespace SpaceEngine.Rendering.Runtime.Anchors
{
    public sealed class SeamlessSpaceStreamingService
    { 
        private SeamlessSpaceAnchor _spaceAnchor;
        private StellarFieldRenderer _stellarFieldRenderer;
        private SolarSystemScaledSpaceRenderer _solarSystemRenderer;
        private SpaceEngineConfiguration _configuration;
        private CelestialRenderConfiguration _renderConfiguration;
        private float _proximityCheckIntervalSeconds = 0.25f;
        private int _nearestSystemSectorSearchRadius = 1;
        private double _solarSystemActivationDistanceLightYears = 0.02;
        private double _solarSystemDeactivationDistanceLightYears = 0.03;
        private float _stellarPointHideAfterLod1AngularDiameterDegrees =
            0.35f;

        private bool _solarSystemLodActive;
        private float _nextProximityCheckTime;
        private long _anchorPointStyleSolarSystemID = long.MinValue;
        private bool _hasAnchorPointStyleOverride;
        private Color _anchorPointStyleColor = Color.white;
        private float _anchorPointStyleIntensity = 1.5f;

        public event Action<CoordinatesData> SolarSystemLodEntered;
        public event Action<CoordinatesData> SolarSystemLodExited;

        public bool IsSolarSystemLodActive => _solarSystemLodActive;

        public void Initialize()
        {
            _solarSystemRenderer?.SetScaledSpaceVisible(false);
            UpdateAnchorStellarPointAppearance();
        }

        public void Dispose()
        {
            _solarSystemRenderer?.SetScaledSpaceVisible(false);
            SetStellarPointSuppression(false);
            _stellarFieldRenderer?.ClearAnchorSolarSystemLocation();
            ClearAnchorPointStyleOverride();
            ApplyAnchorPointStyleOverride();
            _solarSystemLodActive = false;
        }

        internal void Configure(
            SeamlessSpaceAnchor anchor,
            StellarFieldRenderer renderer,
            SolarSystemScaledSpaceRenderer solarRenderer,
            SpaceEngineConfiguration configuration,
            CelestialRenderConfiguration renderConfiguration,
            double activationDistanceLightYears,
            double deactivationDistanceLightYears,
            float stellarPointHideAngularDiameterDegrees)
        {
            _spaceAnchor = anchor;
            _stellarFieldRenderer = renderer;
            _solarSystemRenderer = solarRenderer;
            this._configuration = configuration;
            this._renderConfiguration = renderConfiguration;
            _solarSystemActivationDistanceLightYears = Math.Max(
                0.000001,
                activationDistanceLightYears);
            _solarSystemDeactivationDistanceLightYears = Math.Max(
                _solarSystemActivationDistanceLightYears,
                deactivationDistanceLightYears);
            _stellarPointHideAfterLod1AngularDiameterDegrees = Mathf.Max(
                0.001f,
                stellarPointHideAngularDiameterDegrees);
            _proximityCheckIntervalSeconds = Mathf.Max(
                0.001f,
                _proximityCheckIntervalSeconds);
            _nearestSystemSectorSearchRadius = Mathf.Max(
                0,
                _nearestSystemSectorSearchRadius);
        }

        public void Tick(float unscaledTime)
        {
            if (_spaceAnchor == null || !_spaceAnchor.IsConfigured)
                return;

            if (unscaledTime < _nextProximityCheckTime)
                return;

            _nextProximityCheckTime = unscaledTime +
                                      _proximityCheckIntervalSeconds;
            EvaluateNow();
        }

        public void EvaluateNow()
        {
            if (_spaceAnchor == null || !_spaceAnchor.IsConfigured)
                return;

            UpdateAnchorStellarPointAppearance();

            var activationDistanceMeters =
                _solarSystemActivationDistanceLightYears *
                SeamlessSpaceAnchor.MetersPerLightYear;

            if (_spaceAnchor.GetDistanceToActiveSolarSystemMeters() <=
                activationDistanceMeters)
            {
                ActivateSolarSystemLod(_spaceAnchor.ActiveSolarSystem);
                return;
            }

            var hasNearest = SolarSystemProximityResolver.TryFindNearest(
                _configuration,
                _spaceAnchor.Galaxy,
                _spaceAnchor.GalaxyLocalPositionLightYears,
                _nearestSystemSectorSearchRadius,
                out var nearestSolarSystem,
                out var nearestDistanceMeters);

            var deactivationDistanceMeters =
                _solarSystemDeactivationDistanceLightYears *
                SeamlessSpaceAnchor.MetersPerLightYear;

            if (!_solarSystemLodActive)
            {
                if (hasNearest &&
                    nearestDistanceMeters <= activationDistanceMeters)
                {
                    ActivateSolarSystemLod(nearestSolarSystem);
                }

                return;
            }

            if (hasNearest &&
                nearestSolarSystem.SolarSystemID !=
                _spaceAnchor.Coordinates.SolarSystemID &&
                nearestDistanceMeters <= activationDistanceMeters)
            {
                ActivateSolarSystemLod(nearestSolarSystem);
                return;
            }

            if (_spaceAnchor.GetDistanceToActiveSolarSystemMeters() >
                deactivationDistanceMeters)
            {
                DeactivateSolarSystemLod();
                return;
            }

            UpdateStellarPointSuppression();
        }

        private void ActivateSolarSystemLod(
            in SolarSystemLocationData solarSystem)
        {
            if (_spaceAnchor.Coordinates.SolarSystemID !=
                solarSystem.SolarSystemID)
            {
                _spaceAnchor.RebaseToSolarSystem(solarSystem.SolarSystemID);
            }

            if (_solarSystemRenderer != null)
            {
                _solarSystemRenderer.SetScaledSpaceVisible(true);
                _solarSystemRenderer.RefreshNow();
            }

            UpdateAnchorStellarPointAppearance();

            SetStellarPointSuppression(false);

            if (_solarSystemLodActive)
            {
                UpdateStellarPointSuppression();
                return;
            }

            _solarSystemLodActive = true;
            UpdateStellarPointSuppression();
            SolarSystemLodEntered?.Invoke(_spaceAnchor.Coordinates);
        }

        private void DeactivateSolarSystemLod()
        {
            if (_solarSystemRenderer != null)
                _solarSystemRenderer.SetScaledSpaceVisible(false);

            UpdateAnchorStellarPointAppearance();
            SetStellarPointSuppression(false);

            if (!_solarSystemLodActive)
                return;

            _solarSystemLodActive = false;
            SolarSystemLodExited?.Invoke(_spaceAnchor.Coordinates);
        }

        private void UpdateStellarPointSuppression()
        {
            UpdateAnchorStellarPointAppearance();

            var canHideStellarPoint =
                _solarSystemRenderer != null &&
                _solarSystemRenderer.IsPrimaryVisualReadyAt(
                    _stellarPointHideAfterLod1AngularDiameterDegrees);

            SetStellarPointSuppression(canHideStellarPoint);
        }

        private void SetStellarPointSuppression(bool suppress)
        {
            if (_stellarFieldRenderer != null)
            {
                _stellarFieldRenderer.SetAnchorSolarSystemPointSuppressed(
                    suppress);
            }
        }

        private void UpdateAnchorStellarPointAppearance()
        {
            if (_spaceAnchor == null || !_spaceAnchor.IsConfigured)
            {
                _stellarFieldRenderer?.ClearAnchorSolarSystemLocation();
                return;
            }

            _stellarFieldRenderer?.SetAnchorSolarSystemLocation(
                _spaceAnchor.ActiveSolarSystem);

            ResolveAnchorPointStyleOverride();
            ApplyAnchorPointStyleOverride();
        }

        private void ResolveAnchorPointStyleOverride()
        {
            if (_spaceAnchor == null ||
                !_spaceAnchor.IsConfigured ||
                _configuration == null)
            {
                ClearAnchorPointStyleOverride();
                return;
            }

            var solarSystemID = _spaceAnchor.Coordinates.SolarSystemID;
            if (_anchorPointStyleSolarSystemID == solarSystemID)
                return;

            _anchorPointStyleSolarSystemID = solarSystemID;
            _hasAnchorPointStyleOverride = false;
            _anchorPointStyleColor = Color.white;
            _anchorPointStyleIntensity = 1.5f;

            if (!SolarSystemGeneration.TryGenerate(
                    _spaceAnchor.Coordinates,
                    _configuration.SolarSystemGenerators,
                    _configuration.StellarObjectGenerators,
                    _configuration.PlanetGenerators,
                    out var solarSystem))
            {
                return;
            }

            var dominantMassKg = double.NegativeInfinity;

            for (var index = 0;
                 index < solarSystem.StellarObjects.Length;
                 index++)
            {
                var objectData = solarSystem.StellarObjects[index];
                if (objectData == null)
                    continue;

                var renderer =
                    ContentRendererSelection
                        .SelectStellarObjectRendererOrNull(
                            _renderConfiguration.StellarObjectRenderers,
                            objectData.Entity);

                if (renderer == null ||
                    objectData.MassKg <= dominantMassKg ||
                    !renderer.TryGetDistantPointStyle(
                        objectData,
                        out var color,
                        out var intensity))
                {
                    continue;
                }

                dominantMassKg = objectData.MassKg;
                _hasAnchorPointStyleOverride = true;
                _anchorPointStyleColor = color;
                _anchorPointStyleIntensity = Mathf.Max(0.0f, intensity);
            }
        }

        private void ApplyAnchorPointStyleOverride()
        {
            if (_stellarFieldRenderer == null)
                return;

            if (_hasAnchorPointStyleOverride)
            {
                _stellarFieldRenderer.SetAnchorSolarSystemPointOverride(
                    _anchorPointStyleColor,
                    _anchorPointStyleIntensity);

                return;
            }

            _stellarFieldRenderer.ClearAnchorSolarSystemPointOverride();
        }

        private void ClearAnchorPointStyleOverride()
        {
            _anchorPointStyleSolarSystemID = long.MinValue;
            _hasAnchorPointStyleOverride = false;
            _anchorPointStyleColor = Color.white;
            _anchorPointStyleIntensity = 1.5f;
        }
    }
}