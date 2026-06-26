using UnityEngine;

namespace SpaceEngine.Rendering.Runtime
{
    /// <summary>
    /// Immutable generic streaming snapshot. Per-object visual parameters are
    /// deliberately absent: they live on each renderer SO.
    /// </summary>
    internal readonly struct CelestialStreamingSettings
    {
        public readonly Camera CelestialCamera;
        public readonly LayerMask CelestialLayer;
        public readonly int AggregateStarSampleCount;
        public readonly int MaximumGalaxyProxies;
        public readonly int UniverseGalaxyHorizontalSectorRadius;
        public readonly int UniverseGalaxyVerticalSectorRadius;
        public readonly float GalaxyLod0MinimumPointDiameterPixels;
        public readonly float GalaxyLod0NearPointDiameterPixels;
        public readonly float GalaxyLod0ShrinkCompleteDiameterPixels;
        public readonly float GalaxyLod1FadeInStartDiameterPixels;
        public readonly float GalaxyLod1FullyVisibleDiameterPixels;
        public readonly float GalaxyLod0HideAfterLod1DiameterPixels;
        public readonly int MaximumLoadedExternalGalaxies;
        public readonly int ExternalGalaxyStarfieldSampleCount;
        public readonly float ExternalGalaxyStarPointDiameterPixels;
        public readonly double GalaxyActivationDistanceInRadii;
        public readonly bool EnableGalaxyGas;
        public readonly int GalaxyGasRaymarchSteps;
        public readonly int StellarFieldSectorRadius;
        public readonly int StellarFieldVerticalSectorRadius;
        public readonly int MaximumStellarPoints;
        public readonly float MinimumStarPointDiameterPixels;
        public readonly double ScaledSpaceMetersPerUnityUnit;
        public readonly double SolarSystemActivationDistanceLightYears;
        public readonly double SolarSystemDeactivationDistanceLightYears;
        public readonly float SystemPointHideAfterVisualAngularDiameterDegrees;

        public CelestialStreamingSettings(
            Camera celestialCamera,
            LayerMask celestialLayer,
            int aggregateStarSampleCount,
            int maximumGalaxyProxies,
            int universeGalaxyHorizontalSectorRadius,
            int universeGalaxyVerticalSectorRadius,
            float galaxyLod0MinimumPointDiameterPixels,
            float galaxyLod0NearPointDiameterPixels,
            float galaxyLod0ShrinkCompleteDiameterPixels,
            float galaxyLod1FadeInStartDiameterPixels,
            float galaxyLod1FullyVisibleDiameterPixels,
            float galaxyLod0HideAfterLod1DiameterPixels,
            int maximumLoadedExternalGalaxies,
            int externalGalaxyStarfieldSampleCount,
            float externalGalaxyStarPointDiameterPixels,
            double galaxyActivationDistanceInRadii,
            bool enableGalaxyGas,
            int galaxyGasRaymarchSteps,
            int stellarFieldSectorRadius,
            int stellarFieldVerticalSectorRadius,
            int maximumStellarPoints,
            float minimumStarPointDiameterPixels,
            double scaledSpaceMetersPerUnityUnit,
            double solarSystemActivationDistanceLightYears,
            double solarSystemDeactivationDistanceLightYears,
            float systemPointHideAfterVisualAngularDiameterDegrees)
        {
            CelestialCamera = celestialCamera;
            CelestialLayer = celestialLayer;
            AggregateStarSampleCount = aggregateStarSampleCount;
            MaximumGalaxyProxies = maximumGalaxyProxies;
            UniverseGalaxyHorizontalSectorRadius =
                universeGalaxyHorizontalSectorRadius;
            UniverseGalaxyVerticalSectorRadius =
                universeGalaxyVerticalSectorRadius;
            GalaxyLod0MinimumPointDiameterPixels =
                galaxyLod0MinimumPointDiameterPixels;
            GalaxyLod0NearPointDiameterPixels =
                galaxyLod0NearPointDiameterPixels;
            GalaxyLod0ShrinkCompleteDiameterPixels =
                galaxyLod0ShrinkCompleteDiameterPixels;
            GalaxyLod1FadeInStartDiameterPixels =
                galaxyLod1FadeInStartDiameterPixels;
            GalaxyLod1FullyVisibleDiameterPixels =
                galaxyLod1FullyVisibleDiameterPixels;
            GalaxyLod0HideAfterLod1DiameterPixels =
                galaxyLod0HideAfterLod1DiameterPixels;
            MaximumLoadedExternalGalaxies = maximumLoadedExternalGalaxies;
            ExternalGalaxyStarfieldSampleCount =
                externalGalaxyStarfieldSampleCount;
            ExternalGalaxyStarPointDiameterPixels =
                externalGalaxyStarPointDiameterPixels;
            GalaxyActivationDistanceInRadii =
                galaxyActivationDistanceInRadii;
            EnableGalaxyGas = enableGalaxyGas;
            GalaxyGasRaymarchSteps = galaxyGasRaymarchSteps;
            StellarFieldSectorRadius = stellarFieldSectorRadius;
            StellarFieldVerticalSectorRadius =
                stellarFieldVerticalSectorRadius;
            MaximumStellarPoints = maximumStellarPoints;
            MinimumStarPointDiameterPixels = minimumStarPointDiameterPixels;
            ScaledSpaceMetersPerUnityUnit = scaledSpaceMetersPerUnityUnit;
            SolarSystemActivationDistanceLightYears =
                solarSystemActivationDistanceLightYears;
            SolarSystemDeactivationDistanceLightYears =
                solarSystemDeactivationDistanceLightYears;
            SystemPointHideAfterVisualAngularDiameterDegrees =
                systemPointHideAfterVisualAngularDiameterDegrees;
        }
    }
}
