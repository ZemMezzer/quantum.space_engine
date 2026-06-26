using System;
using SpaceEngine.Rendering.Runtime.Anchors;
using SpaceEngine.Rendering.Runtime.Galaxy;
using SpaceEngine.Rendering.Runtime.SolarSystem;
using SpaceEngine.Rendering.Runtime.Universe;
using SpaceEngine.Runtime.Content;
using SpaceEngine.Runtime.Streaming.Runtime.Anchors;
using UnityEngine;

namespace SpaceEngine.Rendering.Runtime
{
    /// <summary>
    /// Small runtime coordinator. It owns no content rules: all generation is
    /// delegated to the configured ScriptableObject generators and every
    /// content visual to its ScriptableObject renderer.
    /// </summary>
    internal sealed class CelestialRenderRuntime : IDisposable
    {
        private readonly SeamlessSpaceAnchor _spaceAnchor;
        private readonly SpaceEngineConfiguration _contentConfiguration;
        private readonly CelestialRenderConfiguration _renderConfiguration;
        private readonly UniverseGalaxyFieldRenderer _universeRenderer = new();
        private readonly UniverseGalaxyStreamingService _universeStreamingService = new();
        private readonly GalaxyRenderer _galaxyRenderer = new();
        private readonly GalaxyStarfieldRenderer _galaxyStarfieldRenderer = new();
        private readonly StellarFieldRenderer _stellarRenderer = new();
        private readonly SolarSystemScaledSpaceRenderer _solarRenderer;
        private readonly SeamlessSpaceStreamingService _streamingService = new();

        public SeamlessSpaceAnchor SpaceAnchor => _spaceAnchor;
        public SeamlessSpaceStreamingService StreamingService => _streamingService;
        public SolarSystemScaledSpaceRenderer SolarRenderer => _solarRenderer;

        public CelestialRenderRuntime(
            Transform celestialRoot,
            SeamlessSpaceAnchor anchor,
            SpaceEngineConfiguration contentConfiguration,
            CelestialRenderConfiguration renderConfiguration)
        {
            _spaceAnchor = anchor ?? throw new ArgumentNullException(nameof(anchor));
            this._contentConfiguration = contentConfiguration ?? throw new ArgumentNullException(nameof(contentConfiguration));
            this._renderConfiguration = renderConfiguration ?? throw new ArgumentNullException(nameof(renderConfiguration));
            _solarRenderer = new SolarSystemScaledSpaceRenderer(
                celestialRoot,
                contentConfiguration,
                renderConfiguration);
        }

        public void Configure(
            Camera celestialCamera,
            LayerMask celestialLayer)
        {
            var settings = _renderConfiguration.CreateRuntimeSettings(
                celestialCamera,
                celestialLayer);
            _universeRenderer.Configure(
                _spaceAnchor,
                _contentConfiguration,
                _renderConfiguration,
                settings.CelestialCamera,
                settings.CelestialLayer,
                settings.MaximumGalaxyProxies,
                settings.UniverseGalaxyHorizontalSectorRadius,
                settings.UniverseGalaxyVerticalSectorRadius,
                settings.GalaxyLod0MinimumPointDiameterPixels,
                settings.GalaxyLod0NearPointDiameterPixels,
                settings.GalaxyLod0ShrinkCompleteDiameterPixels,
                settings.GalaxyLod1FadeInStartDiameterPixels,
                settings.GalaxyLod1FullyVisibleDiameterPixels,
                settings.GalaxyLod0HideAfterLod1DiameterPixels,
                settings.MaximumLoadedExternalGalaxies,
                settings.ExternalGalaxyStarfieldSampleCount,
                settings.ExternalGalaxyStarPointDiameterPixels);

            _universeStreamingService.Configure(
                _spaceAnchor,
                _universeRenderer,
                settings.GalaxyActivationDistanceInRadii);

            _galaxyRenderer.Configure(
                _spaceAnchor,
                _contentConfiguration,
                _renderConfiguration,
                settings.CelestialCamera,
                settings.CelestialLayer,
                settings.EnableGalaxyGas,
                settings.GalaxyGasRaymarchSteps);

            _galaxyStarfieldRenderer.Configure(
                _spaceAnchor,
                _contentConfiguration,
                _renderConfiguration,
                settings.CelestialCamera,
                settings.CelestialLayer,
                settings.AggregateStarSampleCount,
                settings.StellarFieldSectorRadius * 10.0f);

            _stellarRenderer.Configure(
                _spaceAnchor.GalaxyAnchor,
                settings.CelestialCamera,
                settings.CelestialLayer,
                _contentConfiguration,
                settings.StellarFieldSectorRadius,
                settings.StellarFieldVerticalSectorRadius,
                settings.MaximumStellarPoints,
                settings.MinimumStarPointDiameterPixels);

            _solarRenderer.Configure(
                _spaceAnchor,
                settings.CelestialCamera,
                settings.CelestialLayer,
                settings.ScaledSpaceMetersPerUnityUnit);

            _streamingService.Configure(
                _spaceAnchor,
                _stellarRenderer,
                _solarRenderer,
                _contentConfiguration,
                _renderConfiguration,
                settings.SolarSystemActivationDistanceLightYears,
                settings.SolarSystemDeactivationDistanceLightYears,
                settings.SystemPointHideAfterVisualAngularDiameterDegrees);
        }

        public void Initialize() => _streamingService.Initialize();

        public void UpdateStreaming(float unscaledTime)
        {
            _universeStreamingService.Tick(unscaledTime);
            _streamingService.Tick(unscaledTime);
        }

        public void UpdateVisuals(double simulationTimeSeconds)
        {
            _universeRenderer.Tick();
            _galaxyRenderer.Tick();
            _galaxyStarfieldRenderer.Tick();
            _stellarRenderer.Tick();
            _solarRenderer.Tick(simulationTimeSeconds);
        }

        public void EvaluateNow()
        {
            _universeStreamingService.EvaluateNow();
            _streamingService.EvaluateNow();
            _solarRenderer.RefreshNow();
        }

        public void Dispose()
        {
            _streamingService.Dispose();
            _solarRenderer.Dispose();
            _stellarRenderer.Dispose();
            _galaxyStarfieldRenderer.Dispose();
            _galaxyRenderer.Dispose();
            _universeRenderer.Dispose();
        }
    }
}
