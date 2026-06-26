using System;
using System.Collections.Generic;
using SpaceEngine.Rendering.Content;
using SpaceEngine.Rendering.Runtime;
using UnityEngine;

namespace SpaceEngine.Rendering
{
    /// <summary>
    /// Rendering-module configuration. Generator/entity bindings stay in
    /// SpaceEngineConfiguration; this asset independently maps the same
    /// entities to renderer assets for the 3D backend. It can be replaced by a
    /// 2D configuration without changing generation or physics.
    /// </summary>
    [CreateAssetMenu(
        menuName = "Space Engine/Rendering/Celestial Render Configuration",
        fileName = "CelestialRenderConfiguration")]
    public sealed class CelestialRenderConfiguration : ScriptableObject
    {
        [Header("Galaxy entities and renderers")]
        [SerializeField] private GalaxyRendererBinding[] galaxyRenderers =
            Array.Empty<GalaxyRendererBinding>();

        [Header("Stellar entities and renderers")]
        [SerializeField]
        private StellarObjectRendererBinding[] stellarObjectRenderers =
            Array.Empty<StellarObjectRendererBinding>();

        [Header("Universe and galaxy streaming")]
        [SerializeField, Range(64, 4_096)]
        private int aggregateStarSampleCount = 18_000;
        [SerializeField, Range(64, 4_096)]
        private int maximumDistantGalaxyPoints = 2_048;
        [SerializeField, Range(1, 8)]
        private int distantGalaxyPointHorizontalSectorRadius = 3;
        [SerializeField, Range(0, 4)]
        private int distantGalaxyPointVerticalSectorRadius = 2;
        [SerializeField, Min(0.25f)]
        private float galaxyLod0MinimumPointDiameterPixels = 3.0f;
        [SerializeField, Min(0.05f)]
        private float galaxyLod0NearPointDiameterPixels = 0.35f;
        [SerializeField, Min(0.25f)]
        private float galaxyLod0ShrinkCompleteDiameterPixels = 4.5f;
        [SerializeField, Min(0.25f)]
        private float galaxyLod1FadeInStartDiameterPixels = 6.0f;
        [SerializeField, Min(0.25f)]
        private float galaxyLod1FullyVisibleDiameterPixels = 16.0f;
        [SerializeField, Min(0.25f)]
        private float galaxyLod0HideAfterLod1DiameterPixels = 16.0f;
        [SerializeField, Range(1, 16)]
        private int maximumLoadedExternalGalaxies = 8;
        [SerializeField, Range(256, 8_192)]
        private int externalGalaxyStarfieldSampleCount = 2_048;
        [SerializeField, Range(0.25f, 3.0f)]
        private float externalGalaxyStarPointDiameterPixels = 1.0f;
        [SerializeField, Min(1.0f)]
        private float galaxyActivationDistanceInRadii = 1.20f;

        [Header("Galaxy pipeline quality")]
        [SerializeField] private bool enableGalaxyGas = true;
        [SerializeField, Range(8, 96)]
        private int galaxyGasRaymarchSteps = 24;

        [Header("Stellar field")]
        [SerializeField, Range(1, 12)]
        private int stellarFieldSectorRadius = 7;
        [SerializeField, Range(0, 12)]
        private int stellarFieldVerticalSectorRadius = 7;
        [SerializeField, Range(256, 40_000)]
        private int maximumStellarPoints = 10_000;
        [SerializeField, Min(0.25f)]
        private float minimumStarPointDiameterPixels = 3.0f;

        [Header("Solar-system handoff")]
        [SerializeField, Min(0.000001f)]
        private double solarSystemActivationDistanceLightYears = 0.02;
        [SerializeField, Min(0.000001f)]
        private double solarSystemDeactivationDistanceLightYears = 0.03;
        [SerializeField, Range(0.001f, 10.0f)]
        private float systemPointHideAfterVisualAngularDiameterDegrees = 0.35f;
        [SerializeField, Min(1.0f)]
        private double scaledSpaceMetersPerUnityUnit = 10_000_000.0;

        public IReadOnlyList<GalaxyRendererBinding> GalaxyRenderers =>
            galaxyRenderers ?? Array.Empty<GalaxyRendererBinding>();

        public IReadOnlyList<StellarObjectRendererBinding>
            StellarObjectRenderers =>
                stellarObjectRenderers ??
                Array.Empty<StellarObjectRendererBinding>();

        internal CelestialStreamingSettings CreateRuntimeSettings(
            Camera celestialCamera,
            LayerMask celestialLayer)
        {
            return new CelestialStreamingSettings(
                celestialCamera,
                celestialLayer,
                aggregateStarSampleCount,
                maximumDistantGalaxyPoints,
                distantGalaxyPointHorizontalSectorRadius,
                distantGalaxyPointVerticalSectorRadius,
                galaxyLod0MinimumPointDiameterPixels,
                galaxyLod0NearPointDiameterPixels,
                galaxyLod0ShrinkCompleteDiameterPixels,
                galaxyLod1FadeInStartDiameterPixels,
                galaxyLod1FullyVisibleDiameterPixels,
                galaxyLod0HideAfterLod1DiameterPixels,
                maximumLoadedExternalGalaxies,
                externalGalaxyStarfieldSampleCount,
                externalGalaxyStarPointDiameterPixels,
                galaxyActivationDistanceInRadii,
                enableGalaxyGas,
                galaxyGasRaymarchSteps,
                stellarFieldSectorRadius,
                stellarFieldVerticalSectorRadius,
                maximumStellarPoints,
                minimumStarPointDiameterPixels,
                scaledSpaceMetersPerUnityUnit,
                solarSystemActivationDistanceLightYears,
                solarSystemDeactivationDistanceLightYears,
                systemPointHideAfterVisualAngularDiameterDegrees);
        }

        private void OnValidate()
        {
            aggregateStarSampleCount = Mathf.Clamp(
                aggregateStarSampleCount,
                64,
                4_096);
            maximumDistantGalaxyPoints = Mathf.Clamp(
                maximumDistantGalaxyPoints,
                64,
                4_096);
            distantGalaxyPointHorizontalSectorRadius = Mathf.Clamp(
                distantGalaxyPointHorizontalSectorRadius,
                1,
                8);
            distantGalaxyPointVerticalSectorRadius = Mathf.Clamp(
                distantGalaxyPointVerticalSectorRadius,
                0,
                4);
            galaxyLod0MinimumPointDiameterPixels = Mathf.Max(
                0.25f,
                galaxyLod0MinimumPointDiameterPixels);
            galaxyLod0NearPointDiameterPixels = Mathf.Clamp(
                galaxyLod0NearPointDiameterPixels,
                0.05f,
                galaxyLod0MinimumPointDiameterPixels);
            galaxyLod1FadeInStartDiameterPixels = Mathf.Max(
                0.25f,
                galaxyLod1FadeInStartDiameterPixels);
            galaxyLod1FullyVisibleDiameterPixels = Mathf.Max(
                galaxyLod1FadeInStartDiameterPixels,
                galaxyLod1FullyVisibleDiameterPixels);
            galaxyLod0HideAfterLod1DiameterPixels = Mathf.Max(
                galaxyLod1FullyVisibleDiameterPixels,
                galaxyLod0HideAfterLod1DiameterPixels);
            maximumLoadedExternalGalaxies = Mathf.Clamp(
                maximumLoadedExternalGalaxies,
                1,
                16);
            externalGalaxyStarfieldSampleCount = Mathf.Clamp(
                externalGalaxyStarfieldSampleCount,
                256,
                8_192);
            externalGalaxyStarPointDiameterPixels = Mathf.Clamp(
                externalGalaxyStarPointDiameterPixels,
                0.25f,
                3.0f);
            galaxyActivationDistanceInRadii = Mathf.Max(
                1.0f,
                galaxyActivationDistanceInRadii);
            galaxyGasRaymarchSteps = Mathf.Clamp(
                galaxyGasRaymarchSteps,
                8,
                96);
            stellarFieldSectorRadius = Mathf.Clamp(
                stellarFieldSectorRadius,
                1,
                12);
            stellarFieldVerticalSectorRadius = Mathf.Clamp(
                stellarFieldVerticalSectorRadius,
                0,
                12);
            maximumStellarPoints = Mathf.Clamp(
                maximumStellarPoints,
                256,
                40_000);
            minimumStarPointDiameterPixels = Mathf.Max(
                0.25f,
                minimumStarPointDiameterPixels);
            solarSystemActivationDistanceLightYears = Math.Max(
                0.000001,
                solarSystemActivationDistanceLightYears);
            solarSystemDeactivationDistanceLightYears = Math.Max(
                solarSystemActivationDistanceLightYears,
                solarSystemDeactivationDistanceLightYears);
            systemPointHideAfterVisualAngularDiameterDegrees = Mathf.Max(
                0.001f,
                systemPointHideAfterVisualAngularDiameterDegrees);
            scaledSpaceMetersPerUnityUnit = Math.Max(
                1.0,
                scaledSpaceMetersPerUnityUnit);
        }
    }
}
