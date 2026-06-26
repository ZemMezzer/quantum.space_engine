using System;
using System.Collections;
using System.Collections.Generic;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Data.Planet;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Generation.Galaxy;
using SpaceEngine.Runtime.Generation.SolarSystem;
using SpaceEngine.Runtime.Generation.Universe;
using SpaceEngine.Runtime.Utils;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Runtime.Streaming
{
/// <summary>
    /// Logical kind of a rendered celestial body. The LOD registry uses this
    /// instead of assuming that every body is a star.
    /// </summary>
    public enum CelestialBodyKind : byte
    {
        Star,
        Planet,
        Moon,
        Asteroid,
        ArtificialSatellite
    }

/// <summary>
    /// Frame data supplied to a body-specific LOD 2 renderer. The streaming
    /// system always supplies the true camera-relative position and the
    /// physical radius used by generation. PresentationRadiusMeters is a
    /// fixed scaled-space floor used only to keep very small bodies visible;
    /// it never changes with distance and is not an approach warp.
    /// </summary>
    public readonly struct CelestialBodyLodContext
    {
        public CelestialBodyRenderKey Key { get; }
        public Transform VisualRoot { get; }
        public double3 PhysicalRelativePositionMeters { get; }
        public double PhysicalRadiusMeters { get; }
        public double PresentationRadiusMeters { get; }
        public double DistanceToCentreMeters { get; }
        public double DistanceInRadii { get; }
        public double ScaledSpaceMetersPerUnityUnit { get; }
        public float SimulationTimeSeconds { get; }
        public bool Immediate { get; }

        public CelestialBodyLodContext(
            CelestialBodyRenderKey key,
            Transform visualRoot,
            double3 physicalRelativePositionMeters,
            double physicalRadiusMeters,
            double presentationRadiusMeters,
            double distanceToCentreMeters,
            double distanceInRadii,
            double scaledSpaceMetersPerUnityUnit,
            float simulationTimeSeconds,
            bool immediate)
        {
            Key = key;
            VisualRoot = visualRoot;
            PhysicalRelativePositionMeters =
                physicalRelativePositionMeters;
            PhysicalRadiusMeters = physicalRadiusMeters;
            PresentationRadiusMeters = presentationRadiusMeters;
            DistanceToCentreMeters = distanceToCentreMeters;
            DistanceInRadii = distanceInRadii;
            ScaledSpaceMetersPerUnityUnit =
                scaledSpaceMetersPerUnityUnit;
            SimulationTimeSeconds = simulationTimeSeconds;
            Immediate = immediate;
        }
    }

/// <summary>
    /// Shared fixed-scale transform conversion for body-specific LOD 2
    /// renderers. It applies the true camera-relative position and the same
    /// stable presentation radius as LOD 1. It never pulls a body toward the
    /// camera and never applies a distance-dependent enlargement.
    /// </summary>
    public static class CelestialBodyLodTransformUtility
    {
        public static void ApplyPhysicalTransform(
            Transform target,
            in CelestialBodyLodContext context)
        {
            if (target == null ||
                context.ScaledSpaceMetersPerUnityUnit <= 0.0)
            {
                return;
            }

            var metersPerUnit = context.ScaledSpaceMetersPerUnityUnit;
            var relative = context.PhysicalRelativePositionMeters;

            target.localPosition = new Vector3(
                (float)(relative.x / metersPerUnit),
                (float)(relative.y / metersPerUnit),
                (float)(relative.z / metersPerUnit));

            var radiusMeters = context.PresentationRadiusMeters > 0.0
                ? context.PresentationRadiusMeters
                : context.PhysicalRadiusMeters;

            var diameterInUnityUnits =
                (float)(radiusMeters * 2.0 / metersPerUnit);

            target.localScale = Vector3.one * Mathf.Max(
                0.000001f,
                diameterInUnityUnits);
        }
    }

/// <summary>
    /// Stable body key within the currently loaded solar system. Planet and
    /// moon renderers can use exactly the same key type later.
    /// </summary>
    public readonly struct CelestialBodyRenderKey :
        IEquatable<CelestialBodyRenderKey>
    {
        public CelestialBodyKind Kind { get; }
        public int LocalIndex { get; }

        public CelestialBodyRenderKey(
            CelestialBodyKind kind,
            int localIndex)
        {
            Kind = kind;
            LocalIndex = localIndex;
        }

        public bool Equals(CelestialBodyRenderKey other)
        {
            return Kind == other.Kind &&
                   LocalIndex == other.LocalIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is CelestialBodyRenderKey other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Kind * 397) ^ LocalIndex;
            }
        }

        public override string ToString()
        {
            return $"{Kind}:{LocalIndex}";
        }
    }

/// <summary>
    /// Contract for a body-specific detailed LOD 2 visual. The streaming
    /// system decides when a body enters the local range and supplies its
    /// true position plus a stable scaled-space presentation radius.
    /// Implementations own mesh generation and materials, so planets, moons,
    /// asteroids and structures can each use a different generator without a
    /// synthetic approach effect.
    /// </summary>
    public interface ICelestialBodyLodRenderer
    {
        CelestialBodyRenderKey Key { get; }
        bool IsActive { get; }

        void SetLodActive(bool isActive);

        void UpdateLod(in CelestialBodyLodContext context);
    }

internal static class ReferenceFrameLayerUtility
    {
        public static bool TryGetSingleLayerIndex(
            LayerMask layerMask,
            out int layerIndex)
        {
            var value = layerMask.value;

            if (value == 0 || (value & (value - 1)) != 0)
            {
                layerIndex = 0;
                return false;
            }

            for (var index = 0; index < 32; index++)
            {
                if ((value & (1 << index)) == 0)
                    continue;

                layerIndex = index;
                return true;
            }

            layerIndex = 0;
            return false;
        }

        public static int GetSingleLayerIndexOrDefault(
            LayerMask layerMask,
            int fallbackLayer = 0)
        {
            return TryGetSingleLayerIndex(
                layerMask,
                out var layerIndex)
                ? layerIndex
                : fallbackLayer;
        }

        public static bool AreDifferentSingleLayers(
            LayerMask first,
            LayerMask second,
            LayerMask third,
            LayerMask fourth)
        {
            if (!TryGetSingleLayerIndex(first, out var firstIndex) ||
                !TryGetSingleLayerIndex(second, out var secondIndex) ||
                !TryGetSingleLayerIndex(third, out var thirdIndex) ||
                !TryGetSingleLayerIndex(fourth, out var fourthIndex))
            {
                return false;
            }

            return firstIndex != secondIndex &&
                   firstIndex != thirdIndex &&
                   firstIndex != fourthIndex &&
                   secondIndex != thirdIndex &&
                   secondIndex != fourthIndex &&
                   thirdIndex != fourthIndex;
        }
    }

/// <summary>
    /// Evaluates the Keplerian orbits already stored in runtime data.
    /// Positions are system-barycentric and expressed in metres.
    /// </summary>
    public static class SolarSystemOrbitUtility
    {
        public const double GravitationalConstant = 6.67430e-11;

        public static double3 GetPositionMeters(
            in OrbitData orbit,
            double gravitationalParameter,
            double simulationTimeSeconds)
        {
            if (orbit.SemiMajorAxisMeters <= 0.0 ||
                gravitationalParameter <= 0.0)
            {
                return double3.zero;
            }

            var semiMajorAxis = orbit.SemiMajorAxisMeters;
            var eccentricity = math.clamp(
                orbit.Eccentricity,
                0.0,
                0.999999);

            var meanMotion = Math.Sqrt(
                gravitationalParameter /
                (semiMajorAxis * semiMajorAxis * semiMajorAxis));

            var meanAnomaly = NormalizeRadians(
                orbit.MeanAnomalyAtEpochRadians +
                meanMotion *
                (simulationTimeSeconds - orbit.EpochSeconds));

            var eccentricAnomaly = SolveEccentricAnomaly(
                meanAnomaly,
                eccentricity);

            var cosineEccentricAnomaly = Math.Cos(eccentricAnomaly);
            var sineEccentricAnomaly = Math.Sin(eccentricAnomaly);
            var ellipseYScale = Math.Sqrt(
                1.0 - eccentricity * eccentricity);

            var orbitalX = semiMajorAxis *
                           (cosineEccentricAnomaly - eccentricity);

            var orbitalY = semiMajorAxis *
                           ellipseYScale * sineEccentricAnomaly;

            return RotateOrbitalPlane(
                orbitalX,
                orbitalY,
                orbit.ArgumentOfPeriapsisRadians,
                orbit.InclinationRadians,
                orbit.LongitudeOfAscendingNodeRadians);
        }

        private static double SolveEccentricAnomaly(
            double meanAnomaly,
            double eccentricity)
        {
            var eccentricAnomaly = eccentricity < 0.8
                ? meanAnomaly
                : Math.PI;

            for (var i = 0; i < 10; i++)
            {
                var sine = Math.Sin(eccentricAnomaly);
                var cosine = Math.Cos(eccentricAnomaly);

                var delta =
                    (eccentricAnomaly - eccentricity * sine -
                     meanAnomaly) /
                    (1.0 - eccentricity * cosine);

                eccentricAnomaly -= delta;

                if (Math.Abs(delta) < 0.0000000001)
                    break;
            }

            return eccentricAnomaly;
        }

        private static double3 RotateOrbitalPlane(
            double orbitalX,
            double orbitalY,
            double argumentOfPeriapsis,
            double inclination,
            double longitudeOfAscendingNode)
        {
            var cosArgument = Math.Cos(argumentOfPeriapsis);
            var sinArgument = Math.Sin(argumentOfPeriapsis);

            var argumentX =
                cosArgument * orbitalX -
                sinArgument * orbitalY;

            var argumentY =
                sinArgument * orbitalX +
                cosArgument * orbitalY;

            var cosInclination = Math.Cos(inclination);
            var sinInclination = Math.Sin(inclination);

            var inclinedX = argumentX;
            var inclinedY = cosInclination * argumentY;
            var inclinedZ = sinInclination * argumentY;

            var cosNode = Math.Cos(longitudeOfAscendingNode);
            var sinNode = Math.Sin(longitudeOfAscendingNode);

            return new double3(
                cosNode * inclinedX - sinNode * inclinedY,
                sinNode * inclinedX + cosNode * inclinedY,
                inclinedZ);
        }

        private static double NormalizeRadians(double radians)
        {
            var fullTurn = Math.PI * 2.0;
            radians %= fullTurn;

            if (radians < 0.0)
                radians += fullTurn;

            return radians;
        }
    }

/// <summary>
    /// Resolves the closest real stellar system near a galaxy-space position.
    /// It only scans a small number of internal streaming sectors and is meant
    /// to be called periodically by the seamless LOD controller, not per
    /// frame.
    /// </summary>
    public static class SolarSystemProximityResolver
    {
        public static bool TryFindNearest(
            in GalaxyData galaxy,
            double3 galaxyLocalPositionLightYears,
            int sectorSearchRadius,
            out SolarSystemLocationData nearestSolarSystem,
            out double distanceMeters)
        {
            var centerSector = GalaxySectorUtility.GetCoordinates(
                galaxyLocalPositionLightYears);

            var radius = Math.Max(0, sectorSearchRadius);
            var nearestDistanceSquared = double.PositiveInfinity;
            nearestSolarSystem = default;

            for (var z = centerSector.z - radius;
                 z <= centerSector.z + radius;
                 z++)
            {
                for (var y = centerSector.y - radius;
                     y <= centerSector.y + radius;
                     y++)
                {
                    for (var x = centerSector.x - radius;
                         x <= centerSector.x + radius;
                         x++)
                    {
                        var sectorCoordinates = new int3(x, y, z);

                        if (!SolarSystemIDUtility.IsSectorCoordinateInRange(
                                sectorCoordinates))
                        {
                            continue;
                        }

                        var sector = GalaxySectorGenerator.Generate(
                            galaxy,
                            sectorCoordinates);

                        for (var i = 0;
                             i < sector.SolarSystems.Length;
                             i++)
                        {
                            var solarSystem = sector.SolarSystems[i];
                            var relativeMeters =
                                (solarSystem
                                     .GalaxyLocalPositionLightYears -
                                 galaxyLocalPositionLightYears) *
                                SeamlessSpaceAnchor.MetersPerLightYear;

                            var distanceSquared = math.dot(
                                relativeMeters,
                                relativeMeters);

                            if (distanceSquared >= nearestDistanceSquared)
                                continue;

                            nearestDistanceSquared = distanceSquared;
                            nearestSolarSystem = solarSystem;
                        }
                    }
                }
            }

            if (double.IsPositiveInfinity(nearestDistanceSquared))
            {
                distanceMeters = 0.0;
                return false;
            }

            distanceMeters = Math.Sqrt(nearestDistanceSquared);
            return true;
        }
    }

/// <summary>
    /// One-time bootstrap resolver for a real stellar system near a galaxy's
    /// centre. All returned IDs originate from GalaxySectorGenerator.
    /// </summary>
    internal static class SolarSystemSpawnResolver
    {
        public static bool TryFindNearestGeneratedSolarSystem(
            in GalaxyData galaxy,
            int horizontalSectorRadius,
            int verticalSectorRadius,
            out SolarSystemLocationData location)
        {
            horizontalSectorRadius = Math.Max(0, horizontalSectorRadius);
            verticalSectorRadius = Math.Max(0, verticalSectorRadius);

            var found = false;
            var nearestDistanceSquared = double.PositiveInfinity;
            location = default;

            var horizontalRadiusSquared =
                horizontalSectorRadius * horizontalSectorRadius;

            for (var z = -horizontalSectorRadius;
                 z <= horizontalSectorRadius;
                 z++)
            {
                for (var y = -verticalSectorRadius;
                     y <= verticalSectorRadius;
                     y++)
                {
                    for (var x = -horizontalSectorRadius;
                         x <= horizontalSectorRadius;
                         x++)
                    {
                        if (x * x + z * z > horizontalRadiusSquared)
                            continue;

                        var sectorCoordinates = new int3(x, y, z);
                        var sector = GalaxySectorGenerator.Generate(
                            galaxy,
                            sectorCoordinates);

                        for (var index = 0;
                             index < sector.SolarSystems.Length;
                             index++)
                        {
                            var candidate = sector.SolarSystems[index];
                            var distanceSquared = math.dot(
                                candidate.GalaxyLocalPositionLightYears,
                                candidate.GalaxyLocalPositionLightYears);

                            if (distanceSquared >= nearestDistanceSquared)
                                continue;

                            nearestDistanceSquared = distanceSquared;
                            location = candidate;
                            found = true;
                        }
                    }
                }
            }

            return found;
        }
    }

/// <summary>
    /// Dictionary key for one internal streaming sector.
    /// </summary>
    internal readonly struct StreamingSectorKey :
        IEquatable<StreamingSectorKey>
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Z;

        public StreamingSectorKey(int3 coordinates)
        {
            X = coordinates.x;
            Y = coordinates.y;
            Z = coordinates.z;
        }

        public bool Equals(StreamingSectorKey other)
        {
            return X == other.X &&
                   Y == other.Y &&
                   Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is StreamingSectorKey other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = X;
                hash = hash * 397 ^ Y;
                hash = hash * 397 ^ Z;
                return hash;
            }
        }
    }

/// <summary>
    /// Finds valid galaxy IDs issued by UniverseSectorGenerator. It exists for
    /// demo/bootstrap code only; gameplay should normally receive IDs from
    /// maps, scanners, portals or saved coordinates.
    /// </summary>
    internal static class UniverseSpawnResolver
    {
        public static bool TryResolveExisting(
            long universeID,
            long galaxyID,
            out GalaxyLocationData location)
        {
            GalaxyIDUtility.DecodeGalaxyID(
                galaxyID,
                out var sectorCoordinates,
                out _);

            var sector = UniverseSectorGenerator.Generate(
                universeID,
                sectorCoordinates);

            for (var i = 0; i < sector.Galaxies.Length; i++)
            {
                if (sector.Galaxies[i].GalaxyID != galaxyID)
                    continue;

                location = sector.Galaxies[i];
                return true;
            }

            location = default;
            return false;
        }

        /// <summary>
        /// Searches from the universe origin outwards and returns the closest
        /// actually generated galaxy. This avoids treating 0 as a valid opaque
        /// GalaxyID, because zero decodes to the minimum representable sector.
        /// </summary>
        public static bool TryFindNearestGeneratedGalaxy(
            long universeID,
            int maximumSectorRadius,
            out GalaxyLocationData location)
        {
            maximumSectorRadius = Math.Max(0, maximumSectorRadius);

            var found = false;
            var nearestDistanceSquared = double.PositiveInfinity;
            location = default;

            for (var radius = 0; radius <= maximumSectorRadius; radius++)
            {
                for (var z = -radius; z <= radius; z++)
                {
                    for (var y = -radius; y <= radius; y++)
                    {
                        for (var x = -radius; x <= radius; x++)
                        {
                            if (math.max(math.abs(x), math.max(math.abs(y), math.abs(z))) != radius)
                                continue;

                            var sectorCoordinates = new int3(x, y, z);
                            var sector = UniverseSectorGenerator.Generate(
                                universeID,
                                sectorCoordinates);

                            for (var index = 0;
                                 index < sector.Galaxies.Length;
                                 index++)
                            {
                                var candidate = sector.Galaxies[index];
                                var distanceSquared = math.dot(
                                    candidate.UniversePositionLightYears,
                                    candidate.UniversePositionLightYears);

                                if (distanceSquared >= nearestDistanceSquared)
                                    continue;

                                nearestDistanceSquared = distanceSquared;
                                location = candidate;
                                found = true;
                            }
                        }
                    }
                }

                if (found)
                    return true;
            }

            return false;
        }
    }

/// <summary>
    /// Renders nearby universe galaxies with a dense point-only distant LOD.
    ///
    /// LOD 0 draws every discovered remote galaxy as one star-sized pixel,
    /// keeping intergalactic space populated before any real galaxy visuals
    /// are needed. Once an external galaxy becomes
    /// distinguishable, the renderer preloads a diffuse, data-driven fog
    /// layer and a deterministic point cloud from its real GalaxyData. The
    /// marker remains until that concrete visual has started to fade in.
    /// </summary>
    public sealed class UniverseGalaxyFieldRenderer
    {
        private const int MaximumInstancesPerDrawCall = 1023;

        private readonly struct GalaxyProxy
        {
            public readonly GalaxyLocationData Location;

            public GalaxyProxy(GalaxyLocationData location)
            {
                Location = location;
            }
        }

        private readonly struct ExternalStarSample
        {
            public readonly double3 GalaxyLocalPositionLightYears;
            public readonly float Brightness;

            public ExternalStarSample(
                double3 galaxyLocalPositionLightYears,
                float brightness)
            {
                GalaxyLocalPositionLightYears =
                    galaxyLocalPositionLightYears;
                Brightness = brightness;
            }
        }

        private readonly struct ExternalGalaxyCandidate
        {
            public readonly GalaxyProxy Proxy;
            public readonly float ProjectedDiameterPixels;
            public readonly float Fade;

            public ExternalGalaxyCandidate(
                GalaxyProxy proxy,
                float projectedDiameterPixels,
                float fade)
            {
                Proxy = proxy;
                ProjectedDiameterPixels = projectedDiameterPixels;
                Fade = fade;
            }
        }

        private sealed class LoadedExternalGalaxy
        {
            public readonly GalaxyProxy Proxy;
            public readonly GalaxyData Data;
            public readonly List<ExternalStarSample> Samples = new();
            public Matrix4x4[][] Matrices = Array.Empty<Matrix4x4[]>();

            public LoadedExternalGalaxy(
                GalaxyProxy proxy,
                GalaxyData data)
            {
                Proxy = proxy;
                Data = data;
            }
        }

        private SeamlessSpaceAnchor spaceAnchor;
        private Camera celestialCamera;
        private Mesh proxyMesh;
        private Material markerMaterial;
        private LayerMask celestialLayer = 0;
        private float unityUnitsPerLightYear = 0.000001f;
        private int horizontalSectorRadius = 1;
        private int verticalSectorRadius = 1;
        private bool useCircularFootprint = true;
        private int maximumGalaxyProxies = 512;
        private float minimumGalaxyMarkerPixels = 3.0f;
        private float nearGalaxyMarkerPixels = 0.35f;
        private float markerShrinkCompleteAtGalaxyDiameterPixels = 8.0f;
        private float galaxyVisualFadeInStartPixels = 0.75f;
        private float galaxyVisualFullyVisiblePixels = 2.5f;
        private float markerHideAfterGalaxyVisualPixels = 4.0f;
        private int maximumLoadedExternalGalaxies = 4;
        private int externalGalaxyStarfieldSampleCount = 2_048;
        private float externalGalaxyStarPointDiameterPixels = 1.0f;
        private float brightnessMultiplier = 0.75f;

        private readonly List<GalaxyProxy> _galaxies = new();
        private readonly List<ExternalGalaxyCandidate>
            _externalCandidates = new();
        private readonly Dictionary<long, LoadedExternalGalaxy>
            _loadedExternalGalaxies = new();
        private readonly HashSet<long> _selectedExternalGalaxyIDs = new();
        private readonly Dictionary<long, float> _selectedExternalGalaxyFades =
            new();
        private Matrix4x4[][] _markerMatrices = Array.Empty<Matrix4x4[]>();

        private MaterialPropertyBlock _markerPropertyBlock;
        private MaterialPropertyBlock _externalStarfieldPropertyBlock;
        private MaterialPropertyBlock _externalGalaxyFogPropertyBlock;
        private Mesh _runtimeProxyMesh;
        private Material _runtimeMarkerMaterial;
        private Material _runtimeExternalGalaxyFogMaterial;

        private int3 _lastCenterSector;
        private bool _hasCenterSector;

        internal void Configure(
            SeamlessSpaceAnchor anchor,
            Camera frameCamera,
            LayerMask frameLayer,
            int maximumProxies,
            int distantPointHorizontalSectorRadius,
            int distantPointVerticalSectorRadius,
            float distantMarkerPixels,
            float nearMarkerPixels,
            float markerShrinkCompletePixels,
            float visualFadeInStartPixels,
            float visualFullyVisiblePixels,
            float markerHideAfterVisualPixels,
            int loadedExternalGalaxyCount,
            int externalStarfieldSampleCount,
            float externalStarPointDiameterPixels)
        {
            var changed =
                spaceAnchor != anchor ||
                celestialCamera != frameCamera ||
                celestialLayer.value != frameLayer.value ||
                maximumGalaxyProxies != maximumProxies ||
                horizontalSectorRadius !=
                    distantPointHorizontalSectorRadius ||
                verticalSectorRadius !=
                    distantPointVerticalSectorRadius ||
                !Mathf.Approximately(
                    minimumGalaxyMarkerPixels,
                    distantMarkerPixels) ||
                !Mathf.Approximately(
                    nearGalaxyMarkerPixels,
                    nearMarkerPixels) ||
                !Mathf.Approximately(
                    markerShrinkCompleteAtGalaxyDiameterPixels,
                    markerShrinkCompletePixels) ||
                !Mathf.Approximately(
                    galaxyVisualFadeInStartPixels,
                    visualFadeInStartPixels) ||
                !Mathf.Approximately(
                    galaxyVisualFullyVisiblePixels,
                    visualFullyVisiblePixels) ||
                !Mathf.Approximately(
                    markerHideAfterGalaxyVisualPixels,
                    markerHideAfterVisualPixels) ||
                maximumLoadedExternalGalaxies !=
                    loadedExternalGalaxyCount ||
                externalGalaxyStarfieldSampleCount !=
                    externalStarfieldSampleCount ||
                !Mathf.Approximately(
                    externalGalaxyStarPointDiameterPixels,
                    externalStarPointDiameterPixels);

            spaceAnchor = anchor;
            celestialCamera = frameCamera;
            celestialLayer = frameLayer;
            maximumGalaxyProxies = Mathf.Clamp(
                maximumProxies,
                16,
                4_096);
            horizontalSectorRadius = Mathf.Clamp(
                distantPointHorizontalSectorRadius,
                1,
                8);
            verticalSectorRadius = Mathf.Clamp(
                distantPointVerticalSectorRadius,
                0,
                4);

            minimumGalaxyMarkerPixels = Mathf.Max(
                0.25f,
                distantMarkerPixels);
            nearGalaxyMarkerPixels = Mathf.Clamp(
                nearMarkerPixels,
                0.05f,
                minimumGalaxyMarkerPixels);
            markerShrinkCompleteAtGalaxyDiameterPixels = Mathf.Max(
                0.25f,
                markerShrinkCompletePixels);
            galaxyVisualFadeInStartPixels = Mathf.Max(
                0.25f,
                visualFadeInStartPixels);
            galaxyVisualFullyVisiblePixels = Mathf.Max(
                galaxyVisualFadeInStartPixels,
                visualFullyVisiblePixels);
            markerHideAfterGalaxyVisualPixels = Mathf.Max(
                galaxyVisualFullyVisiblePixels,
                markerHideAfterVisualPixels);
            markerShrinkCompleteAtGalaxyDiameterPixels = Mathf.Min(
                markerShrinkCompleteAtGalaxyDiameterPixels,
                markerHideAfterGalaxyVisualPixels);
            maximumLoadedExternalGalaxies = Mathf.Clamp(
                loadedExternalGalaxyCount,
                1,
                16);
            externalGalaxyStarfieldSampleCount = Mathf.Clamp(
                externalStarfieldSampleCount,
                256,
                8_192);
            externalGalaxyStarPointDiameterPixels = Mathf.Clamp(
                externalStarPointDiameterPixels,
                0.25f,
                3.0f);

            if (changed)
            {
                _loadedExternalGalaxies.Clear();
                ForceRefresh();
            }
        }

        public void Tick()
        {
            if (spaceAnchor == null || !spaceAnchor.IsConfigured)
                return;

            EnsureGalaxyList();
            RenderGalaxyProxies();
        }

        /// <summary>
        /// Finds the nearest rendered-universe galaxy whose generated edge is
        /// already within the supplied activation multiplier. This powers the
        /// same kind of physical frame rebase that solar LOD uses for stars:
        /// the distant proxy is not merely hidden; the traveller changes to
        /// that galaxy's real streaming context.
        /// </summary>
        internal bool TryFindGalaxyForHandoff(
            double activationDistanceInRadii,
            out GalaxyLocationData location)
        {
            location = default;

            if (spaceAnchor == null || !spaceAnchor.IsConfigured)
                return false;

            activationDistanceInRadii = Math.Max(
                1.0,
                activationDistanceInRadii);

            // Do not depend on the visual proxy cap here. A close galaxy must
            // remain reachable even if a dense universe sector contains more
            // distant markers than the configured draw budget.
            var centreSector = UniverseSectorUtility.GetCoordinates(
                spaceAnchor.UniversePositionLightYears);
            var universeID = spaceAnchor.Coordinates.UniverseID;
            var bestEdgeDistance = double.PositiveInfinity;
            var found = false;

            var radiusSquared =
                horizontalSectorRadius * horizontalSectorRadius;

            for (var z = -horizontalSectorRadius;
                 z <= horizontalSectorRadius;
                 z++)
            {
                for (var y = -verticalSectorRadius;
                     y <= verticalSectorRadius;
                     y++)
                {
                    for (var x = -horizontalSectorRadius;
                         x <= horizontalSectorRadius;
                         x++)
                    {
                        if (useCircularFootprint &&
                            x * x + z * z > radiusSquared)
                        {
                            continue;
                        }

                        var sectorCoordinates = centreSector +
                                                new int3(x, y, z);

                        if (!GalaxyIDUtility.IsSectorCoordinateInRange(
                                sectorCoordinates))
                        {
                            continue;
                        }

                        var sector = UniverseSectorGenerator.Generate(
                            universeID,
                            sectorCoordinates);

                        for (var index = 0;
                             index < sector.Galaxies.Length;
                             index++)
                        {
                            var candidate = sector.Galaxies[index];

                            if (candidate.GalaxyID ==
                                spaceAnchor.Coordinates.GalaxyID)
                            {
                                continue;
                            }

                            var relative =
                                spaceAnchor
                                    .GetRelativePositionToGalaxyLightYears(
                                        candidate);
                            var centreDistance = math.length(relative);
                            var activationDistance = Math.Max(
                                1.0,
                                candidate.RadiusLightYears *
                                activationDistanceInRadii);

                            if (centreDistance > activationDistance)
                                continue;

                            var edgeDistance = Math.Max(
                                0.0,
                                centreDistance -
                                candidate.RadiusLightYears);

                            if (edgeDistance >= bestEdgeDistance)
                                continue;

                            bestEdgeDistance = edgeDistance;
                            location = candidate;
                            found = true;
                        }
                    }
                }
            }

            return found;
        }

        private void EnsureGalaxyList()
        {
            var centerSector = UniverseSectorUtility.GetCoordinates(
                spaceAnchor.UniversePositionLightYears);

            if (_hasCenterSector &&
                centerSector.Equals(_lastCenterSector))
            {
                return;
            }

            _lastCenterSector = centerSector;
            _hasCenterSector = true;
            RebuildGalaxyList(centerSector);
        }

        public void Dispose()
        {
            _externalCandidates.Clear();
            _loadedExternalGalaxies.Clear();
            _selectedExternalGalaxyIDs.Clear();
            _selectedExternalGalaxyFades.Clear();

            if (_runtimeProxyMesh != null)
                UnityEngine.Object.Destroy(_runtimeProxyMesh);

            if (_runtimeMarkerMaterial != null)
                UnityEngine.Object.Destroy(_runtimeMarkerMaterial);

            if (_runtimeExternalGalaxyFogMaterial != null)
                UnityEngine.Object.Destroy(_runtimeExternalGalaxyFogMaterial);
        }

        public void ForceRefresh()
        {
            _hasCenterSector = false;
        }

        private void RebuildGalaxyList(int3 centerSector)
        {
            _galaxies.Clear();

            var locations = new List<GalaxyLocationData>();
            var radiusSquared =
                horizontalSectorRadius * horizontalSectorRadius;
            var universeID = spaceAnchor.Coordinates.UniverseID;
            var anchorUniversePosition =
                spaceAnchor.UniversePositionLightYears;

            for (var z = -horizontalSectorRadius;
                 z <= horizontalSectorRadius;
                 z++)
            {
                for (var y = -verticalSectorRadius;
                     y <= verticalSectorRadius;
                     y++)
                {
                    for (var x = -horizontalSectorRadius;
                         x <= horizontalSectorRadius;
                         x++)
                    {
                        if (useCircularFootprint &&
                            x * x + z * z > radiusSquared)
                        {
                            continue;
                        }

                        var sectorCoordinates = centerSector +
                                                new int3(x, y, z);

                        if (!GalaxyIDUtility.IsSectorCoordinateInRange(
                                sectorCoordinates))
                        {
                            continue;
                        }

                        var sector = UniverseSectorGenerator.Generate(
                            universeID,
                            sectorCoordinates);

                        for (var i = 0; i < sector.Galaxies.Length; i++)
                        {
                            var location = sector.Galaxies[i];

                            if (location.GalaxyID ==
                                spaceAnchor.Coordinates.GalaxyID)
                            {
                                continue;
                            }

                            locations.Add(location);
                        }
                    }
                }
            }

            // The potato LOD is deliberately just a dense field of cheap
            // star-like points. Sort before applying the draw budget so
            // close galaxies are retained no matter which sector was visited
            // first while rebuilding the field.
            locations.Sort((left, right) =>
            {
                var leftOffset =
                    left.UniversePositionLightYears - anchorUniversePosition;
                var rightOffset =
                    right.UniversePositionLightYears - anchorUniversePosition;

                return math.lengthsq(leftOffset).CompareTo(
                    math.lengthsq(rightOffset));
            });

            var count = Mathf.Min(maximumGalaxyProxies, locations.Count);
            for (var i = 0; i < count; i++)
                _galaxies.Add(new GalaxyProxy(locations[i]));

            EnsureMatrixStorage(_galaxies.Count, ref _markerMatrices);
        }

        private void RenderGalaxyProxies()
        {
            if (_galaxies.Count == 0)
                return;

            var mesh = ResolveProxyMesh();
            var marker = ResolveMarkerMaterial();
            var fog = ResolveExternalGalaxyFogMaterial();
            var camera = ResolveCamera();

            if (mesh == null || marker == null || camera == null)
                return;

            // First determine which nearby galaxies are genuinely preloaded.
            // This state controls both the external fog/starfield and whether
            // their far marker may fade. Galaxies outside the loaded budget
            // remain a point regardless of projected size.
            _externalCandidates.Clear();
            for (var i = 0; i < _galaxies.Count; i++)
            {
                var proxy = _galaxies[i];
                var relativeLightYears =
                    spaceAnchor.GetRelativePositionToGalaxyLightYears(
                        proxy.Location);
                var position = ToUnityPosition(relativeLightYears);

                if (!IsInCameraFrustum(camera, position))
                    continue;

                var distance = position.magnitude;
                var physicalDiameter = GetPhysicalGalaxyDiameter(
                    proxy.Location.RadiusLightYears);
                var projectedDiameterPixels = GetProjectedDiameterPixels(
                    camera,
                    distance,
                    physicalDiameter);

                var preloadFade = Mathf.InverseLerp(
                    galaxyVisualFadeInStartPixels,
                    galaxyVisualFullyVisiblePixels,
                    projectedDiameterPixels);

                if (preloadFade <= 0.001f)
                    continue;

                _externalCandidates.Add(
                    new ExternalGalaxyCandidate(
                        proxy,
                        projectedDiameterPixels,
                        preloadFade));
            }

            SelectExternalGalaxiesForPreload();

            var markerCount = 0;
            var cameraRotation = camera.transform.rotation;

            // A remote galaxy is always a star-sized point until it actually
            // has a loaded external visual. This prevents empty gaps when the
            // nearby-galaxy budget is occupied by other candidates.
            for (var i = 0; i < _galaxies.Count; i++)
            {
                var proxy = _galaxies[i];
                var relativeLightYears =
                    spaceAnchor.GetRelativePositionToGalaxyLightYears(
                        proxy.Location);
                var position = ToUnityPosition(relativeLightYears);

                if (!IsInCameraFrustum(camera, position))
                    continue;

                var distance = position.magnitude;
                var markerFade = 1.0f;

                if (_selectedExternalGalaxyFades.TryGetValue(
                        proxy.Location.GalaxyID,
                        out var externalFade))
                {
                    // Keep the point until its real diffuse fog/starfield is
                    // already present. SmoothStep avoids a visible pop.
                    markerFade = 1.0f - Mathf.SmoothStep(
                        0.0f,
                        1.0f,
                        externalFade);
                }

                if (markerFade <= 0.001f)
                    continue;

                var markerBatch =
                    markerCount / MaximumInstancesPerDrawCall;
                var markerIndex =
                    markerCount % MaximumInstancesPerDrawCall;
                var markerDiameter = GetPixelDiameter(
                    camera,
                    distance,
                    Mathf.Max(
                        0.02f,
                        minimumGalaxyMarkerPixels * markerFade));

                _markerMatrices[markerBatch][markerIndex] =
                    Matrix4x4.TRS(
                        position,
                        cameraRotation,
                        Vector3.one * markerDiameter);

                markerCount++;
            }

            DrawMarkers(mesh, marker, markerCount);
            RenderSelectedExternalGalaxyVisuals(mesh, marker, fog, camera);
        }

        private void SelectExternalGalaxiesForPreload()
        {
            _externalCandidates.Sort(
                (left, right) => right.ProjectedDiameterPixels.CompareTo(
                    left.ProjectedDiameterPixels));

            _selectedExternalGalaxyIDs.Clear();
            _selectedExternalGalaxyFades.Clear();

            var loadedCount = Mathf.Min(
                maximumLoadedExternalGalaxies,
                _externalCandidates.Count);

            for (var candidateIndex = 0;
                 candidateIndex < loadedCount;
                 candidateIndex++)
            {
                var candidate = _externalCandidates[candidateIndex];
                var galaxyID = candidate.Proxy.Location.GalaxyID;
                _selectedExternalGalaxyIDs.Add(galaxyID);
                _selectedExternalGalaxyFades[galaxyID] = candidate.Fade;
            }
        }

        private void RenderSelectedExternalGalaxyVisuals(
            Mesh mesh,
            Material starMaterial,
            Material fogMaterial,
            Camera camera)
        {
            if (_selectedExternalGalaxyIDs.Count == 0)
                return;

            for (var candidateIndex = 0;
                 candidateIndex < _externalCandidates.Count;
                 candidateIndex++)
            {
                var candidate = _externalCandidates[candidateIndex];
                var galaxyID = candidate.Proxy.Location.GalaxyID;

                if (!_selectedExternalGalaxyIDs.Contains(galaxyID))
                    continue;

                var loaded = GetOrCreateLoadedExternalGalaxy(
                    candidate.Proxy);

                // The diffuse layer is drawn before individual aggregate
                // stars. It is intentionally a real, data-driven galaxy
                // visual rather than a separate placeholder shape.
                if (fogMaterial != null)
                {
                    RenderLoadedExternalGalaxyFog(
                        mesh,
                        fogMaterial,
                        camera,
                        loaded,
                        candidate);
                }

                RenderLoadedExternalGalaxy(
                    mesh,
                    starMaterial,
                    camera,
                    loaded,
                    candidate);
            }

            // Keep a small cache so a nearby galaxy does not rebuild samples
            // every time it briefly crosses the preload threshold.
            if (_loadedExternalGalaxies.Count <=
                maximumLoadedExternalGalaxies * 2)
            {
                return;
            }

            var removals = new List<long>();
            foreach (var entry in _loadedExternalGalaxies)
            {
                if (!_selectedExternalGalaxyIDs.Contains(entry.Key))
                    removals.Add(entry.Key);
            }

            for (var i = 0; i < removals.Count; i++)
                _loadedExternalGalaxies.Remove(removals[i]);
        }

        private LoadedExternalGalaxy GetOrCreateLoadedExternalGalaxy(
            GalaxyProxy proxy)
        {
            if (_loadedExternalGalaxies.TryGetValue(
                    proxy.Location.GalaxyID,
                    out var loaded))
            {
                return loaded;
            }

            var galaxy = GalaxyGenerator.Generate(
                spaceAnchor.Coordinates.UniverseID,
                proxy.Location.GalaxyID);

            loaded = new LoadedExternalGalaxy(proxy, galaxy);
            CreateExternalGalaxyStarSamples(
                galaxy,
                externalGalaxyStarfieldSampleCount,
                loaded.Samples);
            EnsureMatrixStorage(loaded.Samples.Count, ref loaded.Matrices);
            _loadedExternalGalaxies.Add(proxy.Location.GalaxyID, loaded);
            return loaded;
        }

        private void RenderLoadedExternalGalaxyFog(
            Mesh mesh,
            Material material,
            Camera camera,
            LoadedExternalGalaxy loaded,
            ExternalGalaxyCandidate candidate)
        {
            if (candidate.Fade <= 0.001f)
                return;

            var galaxy = loaded.Data;
            var relativeGalaxyCentre =
                spaceAnchor.GetRelativePositionToGalaxyLightYears(
                    loaded.Proxy.Location);
            var position = ToUnityPosition(relativeGalaxyCentre);

            if (!IsInCameraFrustum(camera, position))
                return;

            var diameter = GetPhysicalGalaxyDiameter(
                galaxy.RadiusLightYears);
            var rotation = CreateGalaxyPlaneRotation(
                galaxy.RotationRadians);
            var radiusLightYears = Math.Max(1.0, galaxy.RadiusLightYears);
            var color = GetExternalGalaxyFogColor(galaxy.Type);
            color *= brightnessMultiplier;
            color.a = candidate.Fade;

            _externalGalaxyFogPropertyBlock ??=
                new MaterialPropertyBlock();
            _externalGalaxyFogPropertyBlock.Clear();
            _externalGalaxyFogPropertyBlock.SetColor(
                "_GalaxyColor",
                color);
            _externalGalaxyFogPropertyBlock.SetVector(
                "_GalaxyShape",
                new Vector4(
                    (float)galaxy.Type,
                    Mathf.Max(1.0f, galaxy.SpiralArmCount),
                    Mathf.Max(0.0f, (float)galaxy.SpiralArmTightness),
                    1.0f));
            _externalGalaxyFogPropertyBlock.SetVector(
                "_GalaxyStructure",
                new Vector4(
                    Mathf.Clamp(
                        (float)(galaxy.CoreRadiusLightYears /
                                radiusLightYears),
                        0.025f,
                        0.85f),
                    Mathf.Clamp(
                        (float)(galaxy.BarLengthLightYears /
                                radiusLightYears),
                        0.001f,
                        1.5f),
                    Mathf.Clamp(
                        (float)(galaxy.RingRadiusLightYears /
                                radiusLightYears),
                        0.005f,
                        1.5f),
                    Mathf.Clamp(
                        (float)(galaxy.RingWidthLightYears /
                                radiusLightYears),
                        0.01f,
                        1.0f)));

            Graphics.DrawMesh(
                mesh,
                Matrix4x4.TRS(
                    position,
                    rotation,
                    Vector3.one * diameter),
                material,
                ReferenceFrameLayerUtility.GetSingleLayerIndexOrDefault(
                    celestialLayer),
                null,
                0,
                _externalGalaxyFogPropertyBlock,
                ShadowCastingMode.Off,
                false,
                null,
                LightProbeUsage.Off,
                null);
        }

        private void RenderLoadedExternalGalaxy(
            Mesh mesh,
            Material material,
            Camera camera,
            LoadedExternalGalaxy loaded,
            ExternalGalaxyCandidate candidate)
        {
            if (loaded.Samples.Count == 0)
                return;

            var visibleBudget = GetExternalSampleBudget(
                candidate.ProjectedDiameterPixels,
                candidate.Fade,
                loaded.Samples.Count);
            if (visibleBudget <= 0)
                return;

            var relativeGalaxyCentre =
                spaceAnchor.GetRelativePositionToGalaxyLightYears(
                    loaded.Proxy.Location);
            var cameraRotation = camera.transform.rotation;
            var visibleCount = 0;
            var sampleStride = Mathf.Max(
                1,
                loaded.Samples.Count / visibleBudget);
            var sampleOffset = (int)(loaded.Data.Seed %
                (ulong)loaded.Samples.Count);

            for (var sampleOrder = 0;
                 sampleOrder < visibleBudget;
                 sampleOrder++)
            {
                var sampleIndex = (sampleOffset +
                    sampleOrder * sampleStride) % loaded.Samples.Count;
                var sample = loaded.Samples[sampleIndex];
                var rotatedSample = RotateGalaxyLocalPosition(
                    sample.GalaxyLocalPositionLightYears,
                    loaded.Data.RotationRadians);
                var relativeLightYears = relativeGalaxyCentre +
                                        rotatedSample;
                var position = ToUnityPosition(relativeLightYears);

                if (!IsInCameraFrustum(camera, position))
                    continue;

                var batchIndex = visibleCount / MaximumInstancesPerDrawCall;
                var instanceIndex =
                    visibleCount % MaximumInstancesPerDrawCall;
                var diameter = GetPixelDiameter(
                    camera,
                    position.magnitude,
                    Mathf.Max(
                        0.15f,
                        externalGalaxyStarPointDiameterPixels *
                        sample.Brightness));

                loaded.Matrices[batchIndex][instanceIndex] =
                    Matrix4x4.TRS(
                        position,
                        cameraRotation,
                        Vector3.one * diameter);
                visibleCount++;
            }

            if (visibleCount == 0)
                return;

            _externalStarfieldPropertyBlock ??=
                new MaterialPropertyBlock();
            _externalStarfieldPropertyBlock.Clear();

            var color = GetExternalStarfieldColor(loaded.Data.Type) *
                        (brightnessMultiplier * candidate.Fade);
            _externalStarfieldPropertyBlock.SetColor("_Color", color);
            _externalStarfieldPropertyBlock.SetColor("_BaseColor", color);
            _externalStarfieldPropertyBlock.SetColor(
                "_EmissionColor",
                color);
            _externalStarfieldPropertyBlock.SetFloat(
                "_Intensity",
                0.65f);
            _externalStarfieldPropertyBlock.SetFloat("_Softness", 2.0f);

            DrawInstanced(
                mesh,
                material,
                loaded.Matrices,
                visibleCount,
                _externalStarfieldPropertyBlock);
        }

        private static int GetExternalSampleBudget(
            float projectedDiameterPixels,
            float fade,
            int maximumSampleCount)
        {
            var projectedArea = Mathf.PI * 0.25f *
                                projectedDiameterPixels *
                                projectedDiameterPixels;
            var budget = Mathf.CeilToInt(projectedArea * 0.45f * fade);
            return Mathf.Clamp(budget, 0, maximumSampleCount);
        }

        private static Quaternion CreateGalaxyPlaneRotation(
            double rotationRadians)
        {
            var sine = (float)Math.Sin(rotationRadians);
            var cosine = (float)Math.Cos(rotationRadians);
            var galaxyForward = Vector3.up;
            var galaxyUp = new Vector3(-sine, 0.0f, -cosine);
            return Quaternion.LookRotation(galaxyForward, galaxyUp);
        }

        private static Color GetExternalGalaxyFogColor(GalaxyType type)
        {
            switch (type)
            {
                case GalaxyType.Elliptical:
                case GalaxyType.Lenticular:
                    return new Color(1.0f, 0.72f, 0.42f, 1.0f);

                case GalaxyType.Dwarf:
                case GalaxyType.Irregular:
                    return new Color(0.38f, 0.62f, 1.0f, 1.0f);

                case GalaxyType.Ring:
                    return new Color(0.48f, 0.70f, 1.0f, 1.0f);

                default:
                    return new Color(0.54f, 0.64f, 1.0f, 1.0f);
            }
        }

        private static Color GetExternalStarfieldColor(GalaxyType type)
        {
            switch (type)
            {
                case GalaxyType.Elliptical:
                case GalaxyType.Lenticular:
                    return new Color(1.0f, 0.78f, 0.48f, 1.0f);

                case GalaxyType.Irregular:
                case GalaxyType.Dwarf:
                    return new Color(0.38f, 0.66f, 1.0f, 1.0f);

                case GalaxyType.Ring:
                    return new Color(0.52f, 0.72f, 1.0f, 1.0f);

                default:
                    return new Color(0.72f, 0.82f, 1.0f, 1.0f);
            }
        }

        private static double3 RotateGalaxyLocalPosition(
            double3 position,
            double rotationRadians)
        {
            var cosine = math.cos(rotationRadians);
            var sine = math.sin(rotationRadians);

            return new double3(
                position.x * cosine - position.z * sine,
                position.y,
                position.x * sine + position.z * cosine);
        }

        private static void CreateExternalGalaxyStarSamples(
            in GalaxyData galaxy,
            int sampleCount,
            List<ExternalStarSample> samples)
        {
            samples.Clear();

            var random = new QuantumRandom(
                GalaxyIDUtility.Combine(
                    galaxy.Seed,
                    0x4558545F47414CUL));
            var verticalExtent = GetExternalGalaxyVerticalExtent(galaxy);

            for (var sampleIndex = 0;
                 sampleIndex < sampleCount;
                 sampleIndex++)
            {
                for (var attempt = 0; attempt < 32; attempt++)
                {
                    var x = random.NextDouble(
                        -galaxy.RadiusLightYears,
                        galaxy.RadiusLightYears);
                    var z = random.NextDouble(
                        -galaxy.RadiusLightYears,
                        galaxy.RadiusLightYears);
                    var planarRadius = math.length(new double2(x, z));

                    if (planarRadius > galaxy.RadiusLightYears)
                        continue;

                    var vertical = random.NextDouble(-1.0, 1.0) +
                                   random.NextDouble(-1.0, 1.0) +
                                   random.NextDouble(-1.0, 1.0);
                    var position = new double3(
                        x,
                        vertical * verticalExtent / 3.0,
                        z);
                    var density = GalaxyDensityUtility.GetDensity(
                        galaxy,
                        position);

                    if (density <= 0.0 || random.NextDouble() > density)
                        continue;

                    samples.Add(new ExternalStarSample(
                        position,
                        (float)random.NextDouble(0.45, 1.15)));
                    break;
                }
            }
        }

        private static double GetExternalGalaxyVerticalExtent(
            in GalaxyData galaxy)
        {
            switch (galaxy.Type)
            {
                case GalaxyType.Elliptical:
                    return Math.Max(
                        galaxy.RadiusLightYears * galaxy.Ellipticity,
                        galaxy.DiskThicknessLightYears);

                case GalaxyType.Dwarf:
                case GalaxyType.Irregular:
                    return Math.Max(
                        galaxy.RadiusLightYears * 0.35,
                        galaxy.DiskThicknessLightYears);

                default:
                    return Math.Max(
                        galaxy.DiskThicknessLightYears * 2.0,
                        100.0);
            }
        }

        private void DrawMarkers(
            Mesh mesh,
            Material material,
            int instanceCount)
        {
            if (instanceCount <= 0)
                return;

            _markerPropertyBlock ??= new MaterialPropertyBlock();
            _markerPropertyBlock.Clear();

            var color = new Color(
                brightnessMultiplier,
                brightnessMultiplier * 0.94f,
                brightnessMultiplier * 0.88f);

            _markerPropertyBlock.SetColor("_Color", color);
            _markerPropertyBlock.SetColor("_BaseColor", color);
            _markerPropertyBlock.SetColor("_EmissionColor", color);
            _markerPropertyBlock.SetFloat("_Intensity", 0.9f);
            _markerPropertyBlock.SetFloat("_Softness", 2.0f);

            DrawInstanced(
                mesh,
                material,
                _markerMatrices,
                instanceCount,
                _markerPropertyBlock);
        }

        private void DrawInstanced(
            Mesh mesh,
            Material material,
            Matrix4x4[][] matrices,
            int instanceCount,
            MaterialPropertyBlock propertyBlock)
        {
            var drawn = 0;
            for (var batchIndex = 0;
                 batchIndex < matrices.Length && drawn < instanceCount;
                 batchIndex++)
            {
                var count = Mathf.Min(
                    MaximumInstancesPerDrawCall,
                    instanceCount - drawn);

                Graphics.DrawMeshInstanced(
                    mesh,
                    0,
                    material,
                    matrices[batchIndex],
                    count,
                    propertyBlock,
                    ShadowCastingMode.Off,
                    false,
                    ReferenceFrameLayerUtility
                        .GetSingleLayerIndexOrDefault(celestialLayer),
                    null,
                    LightProbeUsage.Off,
                    null);

                drawn += count;
            }
        }

        private float GetPhysicalGalaxyDiameter(double radiusLightYears)
        {
            return Mathf.Max(
                0.000001f,
                (float)(radiusLightYears * 2.0 * unityUnitsPerLightYear));
        }

        private static float GetProjectedDiameterPixels(
            Camera camera,
            float distance,
            float diameter)
        {
            return diameter / GetPixelDiameter(
                camera,
                distance,
                1.0f);
        }

        private static float GetPixelDiameter(
            Camera camera,
            float distance,
            float pixels)
        {
            var halfFovRadians =
                camera.fieldOfView * Mathf.Deg2Rad * 0.5f;
            var unitsPerPixel =
                2.0f * Mathf.Max(distance, camera.nearClipPlane) *
                Mathf.Tan(halfFovRadians) /
                Mathf.Max(1, Screen.height);

            return unitsPerPixel * pixels;
        }

        private Vector3 ToUnityPosition(double3 relativeLightYears)
        {
            return new Vector3(
                (float)(relativeLightYears.x * unityUnitsPerLightYear),
                (float)(relativeLightYears.y * unityUnitsPerLightYear),
                (float)(relativeLightYears.z * unityUnitsPerLightYear));
        }

        private Camera ResolveCamera()
        {
            return celestialCamera != null
                ? celestialCamera
                : Camera.main;
        }

        private Mesh ResolveProxyMesh()
        {
            if (proxyMesh != null)
                return proxyMesh;

            if (_runtimeProxyMesh != null)
                return _runtimeProxyMesh;

            _runtimeProxyMesh = new Mesh
            {
                name = "Runtime Galaxy Proxy"
            };

            _runtimeProxyMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f)
            };

            _runtimeProxyMesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };

            _runtimeProxyMesh.triangles = new[]
            {
                0, 2, 1,
                0, 3, 2
            };

            _runtimeProxyMesh.RecalculateBounds();
            return _runtimeProxyMesh;
        }

        private Material ResolveExternalGalaxyFogMaterial()
        {
            if (_runtimeExternalGalaxyFogMaterial != null)
                return _runtimeExternalGalaxyFogMaterial;

            var shader = Shader.Find("SpaceEngine/Streaming/Galaxy Proxy");
            if (shader == null)
                return null;

            _runtimeExternalGalaxyFogMaterial = new Material(shader)
            {
                name = "Runtime External Galaxy Fog Material",
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = (int)RenderQueue.Background - 20
            };

            return _runtimeExternalGalaxyFogMaterial;
        }

        private Material ResolveMarkerMaterial()
        {
            var material = markerMaterial != null
                ? markerMaterial
                : CreateRuntimeMarkerMaterialIfNeeded();

            if (material != null)
                material.enableInstancing = true;

            return material;
        }

        private Material CreateRuntimeMarkerMaterialIfNeeded()
        {
            if (_runtimeMarkerMaterial != null)
                return _runtimeMarkerMaterial;

            var shader = Shader.Find("SpaceEngine/Streaming/Star Point");

            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader == null)
                return null;

            _runtimeMarkerMaterial = new Material(shader)
            {
                name = "Runtime Universe Galaxy Marker Material",
                enableInstancing = true,
                renderQueue = (int)RenderQueue.Background
            };

            if (_runtimeMarkerMaterial.HasProperty("_Cull"))
                _runtimeMarkerMaterial.SetFloat("_Cull", 0.0f);

            if (_runtimeMarkerMaterial.HasProperty("_Intensity"))
                _runtimeMarkerMaterial.SetFloat("_Intensity", 0.9f);

            if (_runtimeMarkerMaterial.HasProperty("_Softness"))
                _runtimeMarkerMaterial.SetFloat("_Softness", 2.0f);

            return _runtimeMarkerMaterial;
        }

        private static bool IsInCameraFrustum(
            Camera camera,
            Vector3 position)
        {
            var local = camera.transform.InverseTransformPoint(position);
            if (local.z < camera.nearClipPlane ||
                local.z > camera.farClipPlane)
            {
                return false;
            }

            var halfHeight =
                local.z * Mathf.Tan(
                    camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            var halfWidth = halfHeight * camera.aspect;

            return Mathf.Abs(local.x) <= halfWidth &&
                   Mathf.Abs(local.y) <= halfHeight;
        }

        private static void EnsureMatrixStorage(
            int instanceCount,
            ref Matrix4x4[][] matrices)
        {
            var requiredBatchCount =
                (instanceCount + MaximumInstancesPerDrawCall - 1) /
                MaximumInstancesPerDrawCall;

            if (matrices.Length == requiredBatchCount)
                return;

            matrices = new Matrix4x4[requiredBatchCount][];

            for (var i = 0; i < requiredBatchCount; i++)
                matrices[i] = new Matrix4x4[MaximumInstancesPerDrawCall];
        }

        private static void EnsureVectorStorage(
            int instanceCount,
            ref Vector4[][] vectors)
        {
            var requiredBatchCount =
                (instanceCount + MaximumInstancesPerDrawCall - 1) /
                MaximumInstancesPerDrawCall;

            if (vectors.Length == requiredBatchCount)
                return;

            vectors = new Vector4[requiredBatchCount][];

            for (var i = 0; i < requiredBatchCount; i++)
                vectors[i] = new Vector4[MaximumInstancesPerDrawCall];
        }
    }

/// <summary>
    /// Renders the unresolved light of the active galaxy as a deterministic
    /// point cloud. These samples are an aggregate representation of distant
    /// stellar populations, not selectable solar-system records.
    ///
    /// Nearby individual systems remain the responsibility of
    /// GalaxySpaceStreamer, which uses real SolarSystemLocationData and can
    /// hand off exactly to a solar-system LOD.
    /// </summary>
        public sealed class GalaxyGasRenderer
    {
        private const float VolumeRadiusInGalaxyRadii = 1.18f;

        private SeamlessSpaceAnchor spaceAnchor;
        private Camera celestialCamera;
        private LayerMask celestialLayer = 0;
        private bool enabled = true;
        private int raymarchSteps = 40;
        private float brightness = 1.0f;
        private float opacity = 1.25f;
        private float dustStrength = 0.9f;
        private float diskRadiusMultiplier = 1.0f;
        private float diskThicknessMultiplier = 1.0f;

        private Mesh _runtimeFullscreenMesh;
        private Material _runtimeMaterial;
        private MaterialPropertyBlock _propertyBlock;

        internal void Configure(
            SeamlessSpaceAnchor anchor,
            Camera frameCamera,
            LayerMask frameLayer,
            bool enableRenderer,
            int gasRaymarchSteps,
            float gasBrightness,
            float gasOpacity,
            float gasDustStrength,
            float gasDiskRadiusMultiplier,
            float gasDiskThicknessMultiplier)
        {
            spaceAnchor = anchor;
            celestialCamera = frameCamera;
            celestialLayer = frameLayer;
            enabled = enableRenderer;
            raymarchSteps = Mathf.Clamp(gasRaymarchSteps, 8, 96);
            brightness = Mathf.Max(0.0f, gasBrightness);
            opacity = Mathf.Clamp(gasOpacity, 0.0f, 4.0f);
            dustStrength = Mathf.Clamp(gasDustStrength, 0.0f, 2.0f);
            diskRadiusMultiplier = Mathf.Clamp(
                gasDiskRadiusMultiplier,
                0.5f,
                2.0f);
            diskThicknessMultiplier = Mathf.Clamp(
                gasDiskThicknessMultiplier,
                0.5f,
                3.0f);
        }

        public void Tick()
        {
            if (!enabled ||
                spaceAnchor == null ||
                !spaceAnchor.IsConfigured)
            {
                return;
            }

            var mesh = ResolveFullscreenMesh();
            var material = ResolveMaterial();
            var camera = ResolveCamera();

            if (mesh == null || material == null || camera == null)
                return;

            var galaxy = spaceAnchor.Galaxy;
            var radiusLightYears = Math.Max(1.0, galaxy.RadiusLightYears);
            var cameraPosition = ToShapeLocalPosition(
                spaceAnchor.GalaxyLocalPositionLightYears,
                galaxy.RotationRadians);

            var shapeCameraPosition = new Vector3(
                (float)(cameraPosition.x / radiusLightYears),
                (float)(cameraPosition.y / radiusLightYears),
                (float)(cameraPosition.z / radiusLightYears));

            // The shader reconstructs its ray from Unity's per-camera GPU
            // matrices, so it always uses the same actual projection as the
            // celestial camera. We only provide the fixed galaxy orientation.
            var worldToGalaxyShape = CreateWorldToGalaxyShapeMatrix(
                galaxy.RotationRadians);

            var galaxyType = (float)galaxy.Type;
            var gasDensityFactor = Mathf.Clamp01(
                0.35f + (float)galaxy.GasDensity * 1.8f);
            var colors = GetGalaxyColors(galaxy.Type);

            _propertyBlock ??= new MaterialPropertyBlock();
            _propertyBlock.Clear();
            _propertyBlock.SetVector(
                "_CameraGalaxyPosition",
                shapeCameraPosition);
            _propertyBlock.SetMatrix(
                "_WorldToGalaxyShape",
                worldToGalaxyShape);
            _propertyBlock.SetFloat("_RaymarchSteps", raymarchSteps);
            _propertyBlock.SetFloat("_VolumeRadius", VolumeRadiusInGalaxyRadii);
            _propertyBlock.SetFloat("_GalaxyType", galaxyType);
            _propertyBlock.SetFloat(
                "_CoreRadius",
                Mathf.Clamp(
                    (float)(galaxy.CoreRadiusLightYears / radiusLightYears),
                    0.005f,
                    0.85f));
            _propertyBlock.SetFloat(
                "_DiskThickness",
                Mathf.Clamp(
                    (float)(galaxy.DiskThicknessLightYears / radiusLightYears) *
                    diskThicknessMultiplier,
                    0.0025f,
                    1.0f));
            _propertyBlock.SetFloat(
                "_DiskRadiusMultiplier",
                diskRadiusMultiplier);
            _propertyBlock.SetFloat(
                "_SpiralArmCount",
                Mathf.Max(1.0f, galaxy.SpiralArmCount));
            _propertyBlock.SetFloat(
                "_SpiralArmTightness",
                Mathf.Max(0.0f, (float)galaxy.SpiralArmTightness));
            _propertyBlock.SetFloat(
                "_BarLength",
                Mathf.Clamp(
                    (float)(galaxy.BarLengthLightYears / radiusLightYears),
                    0.005f,
                    1.5f));
            _propertyBlock.SetFloat(
                "_Ellipticity",
                Mathf.Clamp((float)galaxy.Ellipticity, 0.05f, 2.0f));
            _propertyBlock.SetFloat(
                "_RingRadius",
                Mathf.Clamp(
                    (float)(galaxy.RingRadiusLightYears / radiusLightYears),
                    0.005f,
                    1.5f));
            _propertyBlock.SetFloat(
                "_RingWidth",
                Mathf.Clamp(
                    (float)(galaxy.RingWidthLightYears / radiusLightYears),
                    0.0025f,
                    1.0f));
            _propertyBlock.SetFloat(
                "_Irregularity",
                Mathf.Clamp01((float)galaxy.Irregularity));
            _propertyBlock.SetFloat("_GasDensity", gasDensityFactor);
            _propertyBlock.SetFloat("_Brightness", brightness);
            _propertyBlock.SetFloat("_Opacity", opacity);
            _propertyBlock.SetFloat("_DustStrength", dustStrength);
            _propertyBlock.SetFloat(
                "_Seed",
                (float)((galaxy.Seed & 0xFFFFUL) / 65535.0));
            _propertyBlock.SetColor("_CoreColor", colors.Core);
            _propertyBlock.SetColor("_DiskColor", colors.Disk);
            _propertyBlock.SetColor("_NebulaColor", colors.Nebula);
            _propertyBlock.SetColor("_HaloColor", colors.Halo);

            // The vertex shader generates clip-space coordinates itself.
            // Keeping this mesh near the celestial camera only gives Unity a
            // sensible bounds centre for draw-call culling.
            var matrix = Matrix4x4.TRS(
                camera.transform.position +
                camera.transform.forward * (camera.nearClipPlane * 2.0f),
                Quaternion.identity,
                Vector3.one);

            Graphics.DrawMesh(
                mesh,
                matrix,
                material,
                ReferenceFrameLayerUtility.GetSingleLayerIndexOrDefault(
                    celestialLayer),
                null,
                0,
                _propertyBlock,
                ShadowCastingMode.Off,
                false,
                null,
                LightProbeUsage.Off,
                null);
        }

        public void Dispose()
        {
            if (_runtimeFullscreenMesh != null)
                UnityEngine.Object.Destroy(_runtimeFullscreenMesh);

            if (_runtimeMaterial != null)
                UnityEngine.Object.Destroy(_runtimeMaterial);
        }

        private Mesh ResolveFullscreenMesh()
        {
            if (_runtimeFullscreenMesh != null)
                return _runtimeFullscreenMesh;

            _runtimeFullscreenMesh = new Mesh
            {
                name = "Runtime Galaxy Gas Fullscreen Mesh"
            };

            _runtimeFullscreenMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0.0f),
                new Vector3( 0.5f, -0.5f, 0.0f),
                new Vector3( 0.5f,  0.5f, 0.0f),
                new Vector3(-0.5f,  0.5f, 0.0f)
            };

            _runtimeFullscreenMesh.uv = new[]
            {
                new Vector2(0.0f, 0.0f),
                new Vector2(1.0f, 0.0f),
                new Vector2(1.0f, 1.0f),
                new Vector2(0.0f, 1.0f)
            };

            _runtimeFullscreenMesh.triangles = new[]
            {
                0, 1, 2,
                0, 2, 3
            };

            _runtimeFullscreenMesh.bounds = new Bounds(
                Vector3.zero,
                Vector3.one * 1_000_000.0f);

            return _runtimeFullscreenMesh;
        }

        private Material ResolveMaterial()
        {
            if (_runtimeMaterial != null)
                return _runtimeMaterial;

            var shader = Shader.Find(
                "SpaceEngine/Streaming/Galaxy Gas Volume");

            if (shader == null)
                return null;

            _runtimeMaterial = new Material(shader)
            {
                name = "Runtime Galaxy Gas Volume",
                hideFlags = HideFlags.HideAndDontSave
            };

            return _runtimeMaterial;
        }

        private Camera ResolveCamera()
        {
            return celestialCamera != null
                ? celestialCamera
                : Camera.main;
        }

        private static double3 ToShapeLocalPosition(
            double3 galaxyLocalPosition,
            double rotationRadians)
        {
            var cosine = Math.Cos(-rotationRadians);
            var sine = Math.Sin(-rotationRadians);

            return new double3(
                galaxyLocalPosition.x * cosine -
                galaxyLocalPosition.z * sine,
                galaxyLocalPosition.y,
                galaxyLocalPosition.x * sine +
                galaxyLocalPosition.z * cosine);
        }

        private static Matrix4x4 CreateWorldToGalaxyShapeMatrix(
            double rotationRadians)
        {
            var cosine = (float)Math.Cos(-rotationRadians);
            var sine = (float)Math.Sin(-rotationRadians);
            var matrix = Matrix4x4.identity;

            // This is exactly the same XZ rotation as
            // ToShapeLocalPosition above, expressed as a shader matrix.
            matrix.m00 = cosine;
            matrix.m02 = -sine;
            matrix.m20 = sine;
            matrix.m22 = cosine;
            return matrix;
        }

        private static (
            Color Core,
            Color Disk,
            Color Nebula,
            Color Halo) GetGalaxyColors(GalaxyType galaxyType)
        {
            switch (galaxyType)
            {
                case GalaxyType.Elliptical:
                    return (
                        new Color(1.0f, 0.82f, 0.58f, 1.0f),
                        new Color(0.92f, 0.80f, 0.63f, 1.0f),
                        new Color(0.58f, 0.56f, 0.68f, 1.0f),
                        new Color(0.25f, 0.32f, 0.46f, 1.0f));

                case GalaxyType.Dwarf:
                case GalaxyType.Irregular:
                    return (
                        new Color(1.0f, 0.84f, 0.62f, 1.0f),
                        new Color(0.48f, 0.61f, 0.92f, 1.0f),
                        new Color(0.24f, 0.52f, 1.0f, 1.0f),
                        new Color(0.12f, 0.22f, 0.42f, 1.0f));

                default:
                    return (
                        new Color(1.0f, 0.76f, 0.42f, 1.0f),
                        new Color(0.46f, 0.56f, 0.82f, 1.0f),
                        new Color(0.20f, 0.46f, 1.0f, 1.0f),
                        new Color(0.10f, 0.18f, 0.36f, 1.0f));
            }
        }
    }

public sealed class GalaxyStarfieldRenderer
    {
        private const int MaximumInstancesPerDrawCall = 1023;
        private const ulong AggregatePointSalt = 0x47414C5F53544152UL;

        private readonly struct StarSample
        {
            public readonly double3 GalaxyLocalPositionLightYears;
            public readonly float Brightness;

            public StarSample(
                double3 galaxyLocalPositionLightYears,
                float brightness)
            {
                GalaxyLocalPositionLightYears =
                    galaxyLocalPositionLightYears;
                Brightness = brightness;
            }
        }

        private SeamlessSpaceAnchor spaceAnchor;
        private Camera celestialCamera;
        private Mesh pointMesh;
        private Material pointMaterial;
        private LayerMask celestialLayer = 0;
        private float unityUnitsPerLightYear = 0.001f;
        private int aggregateSampleCount = 12_000;
        private int attemptsPerSample = 32;
        private float unresolvedInnerRadiusLightYears = 150f;
        private float starPixels = 1.35f;
        private float minimumPointDiameter = 0.0001f;
        private float maximumPointDiameter = 0.25f;
        private float brightnessMultiplier = 1.0f;

        private readonly List<StarSample> _samples = new();
        private Matrix4x4[][] _matrices = Array.Empty<Matrix4x4[]>();

        private MaterialPropertyBlock _propertyBlock;
        private Mesh _runtimePointMesh;
        private Material _runtimePointMaterial;
        private Texture2D _runtimePointTexture;
        private ulong _loadedGalaxySeed;

        internal void Configure(
            SeamlessSpaceAnchor anchor,
            Camera frameCamera,
            LayerMask frameLayer,
            int sampleCount,
            float resolvedStellarFieldRadiusLightYears)
        {
            var clampedInnerRadius = Mathf.Max(
                0f,
                resolvedStellarFieldRadiusLightYears);

            var changed =
                spaceAnchor != anchor ||
                celestialCamera != frameCamera ||
                celestialLayer.value != frameLayer.value ||
                aggregateSampleCount != sampleCount ||
                !Mathf.Approximately(
                    unresolvedInnerRadiusLightYears,
                    clampedInnerRadius);

            spaceAnchor = anchor;
            celestialCamera = frameCamera;
            celestialLayer = frameLayer;
            aggregateSampleCount = Mathf.Clamp(
                sampleCount,
                1_000,
                30_000);
            unresolvedInnerRadiusLightYears = clampedInnerRadius;

            if (changed)
                ForceRefresh();
        }



        public void Tick()
        {
            if (spaceAnchor == null || !spaceAnchor.IsConfigured)
                return;

            if (_loadedGalaxySeed != spaceAnchor.Galaxy.Seed)
            {
                RebuildSamples(spaceAnchor.Galaxy);
                _loadedGalaxySeed = spaceAnchor.Galaxy.Seed;
            }

            RenderSamples();
        }

        public void Dispose()
        {
            if (_runtimePointMesh != null)
                UnityEngine.Object.Destroy(_runtimePointMesh);

            if (_runtimePointMaterial != null)
                UnityEngine.Object.Destroy(_runtimePointMaterial);

            if (_runtimePointTexture != null)
                UnityEngine.Object.Destroy(_runtimePointTexture);
        }

        /// <summary>
        /// Rebuilds the deterministic aggregate cloud for the active galaxy.
        /// Call after changing visual density settings from code.
        /// </summary>
        public void ForceRefresh()
        {
            _loadedGalaxySeed = 0UL;
        }

        private void RebuildSamples(in GalaxyData galaxy)
        {
            _samples.Clear();

            var random = new QuantumRandom(
                GalaxyIDUtility.Combine(
                    galaxy.Seed,
                    AggregatePointSalt));

            for (var sampleIndex = 0;
                 sampleIndex < aggregateSampleCount;
                 sampleIndex++)
            {
                if (TryCreateSample(galaxy, ref random, out var sample))
                    _samples.Add(sample);
            }

            EnsureMatrixStorage(_samples.Count, ref _matrices);
        }


        private bool TryCreateSample(
            in GalaxyData galaxy,
            ref QuantumRandom random,
            out StarSample sample)
        {
            var verticalExtent = GetVerticalExtent(galaxy);

            for (var attempt = 0; attempt < attemptsPerSample; attempt++)
            {
                var x = random.NextDouble(
                    -galaxy.RadiusLightYears,
                    galaxy.RadiusLightYears);

                var z = random.NextDouble(
                    -galaxy.RadiusLightYears,
                    galaxy.RadiusLightYears);

                var planarRadius = math.length(new double2(x, z));
                if (planarRadius > galaxy.RadiusLightYears)
                    continue;

                // A triangular distribution keeps most unresolved light near
                // the disk/ellipsoid centre while retaining vertical extent.
                var vertical =
                    random.NextDouble(-1.0, 1.0) +
                    random.NextDouble(-1.0, 1.0) +
                    random.NextDouble(-1.0, 1.0);

                var position = new double3(
                    x,
                    vertical * verticalExtent / 3.0,
                    z);

                var density = GalaxyDensityUtility.GetDensity(
                    galaxy,
                    position);

                if (density <= 0.0 || random.NextDouble() > density)
                    continue;

                var brightness = (float)random.NextDouble(0.45, 1.15);
                sample = new StarSample(position, brightness);
                return true;
            }

            sample = default;
            return false;
        }

        private static double GetVerticalExtent(in GalaxyData galaxy)
        {
            switch (galaxy.Type)
            {
                case GalaxyType.Elliptical:
                    return Math.Max(
                        galaxy.RadiusLightYears * galaxy.Ellipticity,
                        galaxy.DiskThicknessLightYears);

                case GalaxyType.Dwarf:
                case GalaxyType.Irregular:
                    return Math.Max(
                        galaxy.RadiusLightYears * 0.35,
                        galaxy.DiskThicknessLightYears);

                default:
                    return Math.Max(
                        galaxy.DiskThicknessLightYears * 2.0,
                        100.0);
            }
        }



        private void RenderSamples()
        {
            if (_samples.Count == 0)
                return;

            var mesh = ResolvePointMesh();
            var material = ResolvePointMaterial();
            var camera = ResolveCamera();

            if (mesh == null || material == null || camera == null)
                return;

            var anchorPosition = spaceAnchor.GalaxyLocalPositionLightYears;
            var cameraRotation = camera.transform.rotation;
            var visibleCount = 0;

            for (var i = 0; i < _samples.Count; i++)
            {
                var sample = _samples[i];
                var relativeLightYears =
                    sample.GalaxyLocalPositionLightYears - anchorPosition;

                if (math.length(relativeLightYears) <
                    unresolvedInnerRadiusLightYears)
                {
                    continue;
                }

                var position = ToUnityPosition(relativeLightYears);
                if (!IsInCameraFrustum(camera, position))
                    continue;

                var batchIndex = visibleCount / MaximumInstancesPerDrawCall;
                var instanceIndex = visibleCount % MaximumInstancesPerDrawCall;
                var diameter = GetPointDiameter(
                    camera,
                    position.magnitude,
                    sample.Brightness);

                _matrices[batchIndex][instanceIndex] = Matrix4x4.TRS(
                    position,
                    cameraRotation,
                    Vector3.one * diameter);

                visibleCount++;
            }

            if (visibleCount == 0)
                return;

            _propertyBlock ??= new MaterialPropertyBlock();
            _propertyBlock.Clear();
            var color = Color.white * brightnessMultiplier;
            _propertyBlock.SetColor("_Color", color);
            _propertyBlock.SetColor("_BaseColor", color);
            _propertyBlock.SetColor("_EmissionColor", color);

            var drawn = 0;
            for (var batchIndex = 0;
                 batchIndex < _matrices.Length && drawn < visibleCount;
                 batchIndex++)
            {
                var count = Mathf.Min(
                    MaximumInstancesPerDrawCall,
                    visibleCount - drawn);

                Graphics.DrawMeshInstanced(
                    mesh,
                    0,
                    material,
                    _matrices[batchIndex],
                    count,
                    _propertyBlock,
                    ShadowCastingMode.Off,
                    false,
                    ReferenceFrameLayerUtility.GetSingleLayerIndexOrDefault(
                        celestialLayer),
                    null,
                    LightProbeUsage.Off,
                    null);

                drawn += count;
            }
        }




        private float GetPointDiameter(
            Camera camera,
            float distance,
            float brightness)
        {
            var halfFovRadians =
                camera.fieldOfView * Mathf.Deg2Rad * 0.5f;

            var unitsPerPixel =
                2f * Mathf.Max(distance, camera.nearClipPlane) *
                Mathf.Tan(halfFovRadians) /
                Mathf.Max(1, Screen.height);

            return Mathf.Clamp(
                unitsPerPixel * starPixels * brightness,
                minimumPointDiameter,
                maximumPointDiameter);
        }

        private static bool IsInCameraFrustum(
            Camera camera,
            Vector3 position)
        {
            var local = camera.transform.InverseTransformPoint(position);
            if (local.z < camera.nearClipPlane ||
                local.z > camera.farClipPlane)
            {
                return false;
            }

            var halfHeight =
                local.z * Mathf.Tan(
                    camera.fieldOfView * Mathf.Deg2Rad * 0.5f);

            var halfWidth = halfHeight * camera.aspect;

            return Mathf.Abs(local.x) <= halfWidth &&
                   Mathf.Abs(local.y) <= halfHeight;
        }

        private Vector3 ToUnityPosition(double3 relativeLightYears)
        {
            return new Vector3(
                (float)(relativeLightYears.x * unityUnitsPerLightYear),
                (float)(relativeLightYears.y * unityUnitsPerLightYear),
                (float)(relativeLightYears.z * unityUnitsPerLightYear));
        }

        private Camera ResolveCamera()
        {
            if (celestialCamera != null)
                return celestialCamera;

            return Camera.main;
        }

        private Mesh ResolvePointMesh()
        {
            if (pointMesh != null)
                return pointMesh;

            if (_runtimePointMesh != null)
                return _runtimePointMesh;

            _runtimePointMesh = new Mesh
            {
                name = "Runtime Billboard Point"
            };

            _runtimePointMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f)
            };

            _runtimePointMesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };

            _runtimePointMesh.triangles = new[]
            {
                0, 2, 1,
                0, 3, 2
            };

            _runtimePointMesh.RecalculateBounds();
            return _runtimePointMesh;
        }

        private Material ResolvePointMaterial()
        {
            var material = pointMaterial != null
                ? pointMaterial
                : CreateRuntimePointMaterialIfNeeded();

            if (material != null)
                material.enableInstancing = true;

            return material;
        }

        private Material CreateRuntimePointMaterialIfNeeded()
        {
            if (_runtimePointMaterial != null)
                return _runtimePointMaterial;

            var shader = Shader.Find("SpaceEngine/Streaming/Star Point");

            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");

            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            if (shader == null)
                return null;

            _runtimePointMaterial = new Material(shader)
            {
                name = "Runtime Galaxy Star Point Material",
                enableInstancing = true,
                renderQueue = shader.name == "SpaceEngine/Streaming/Star Point"
                    ? (int)RenderQueue.Background
                    : (int)RenderQueue.Transparent
            };

            if (shader.name != "SpaceEngine/Streaming/Star Point")
            {
                _runtimePointTexture = CreatePointTexture();

                if (_runtimePointMaterial.HasProperty("_BaseMap"))
                    _runtimePointMaterial.SetTexture("_BaseMap", _runtimePointTexture);

                if (_runtimePointMaterial.HasProperty("_MainTex"))
                    _runtimePointMaterial.SetTexture("_MainTex", _runtimePointTexture);
            }

            if (_runtimePointMaterial.HasProperty("_BaseColor"))
                _runtimePointMaterial.SetColor("_BaseColor", Color.white);

            if (_runtimePointMaterial.HasProperty("_Color"))
                _runtimePointMaterial.SetColor("_Color", Color.white);

            if (_runtimePointMaterial.HasProperty("_Surface"))
                _runtimePointMaterial.SetFloat("_Surface", 1f);

            if (_runtimePointMaterial.HasProperty("_Blend"))
                _runtimePointMaterial.SetFloat("_Blend", 1f);

            if (_runtimePointMaterial.HasProperty("_ZWrite"))
                _runtimePointMaterial.SetFloat("_ZWrite", 0f);

            if (_runtimePointMaterial.HasProperty("_Cull"))
                _runtimePointMaterial.SetFloat("_Cull", 0f);

            if (_runtimePointMaterial.HasProperty("_Intensity"))
                _runtimePointMaterial.SetFloat("_Intensity", 0.65f);

            if (_runtimePointMaterial.HasProperty("_Softness"))
                _runtimePointMaterial.SetFloat("_Softness", 3.5f);

            return _runtimePointMaterial;
        }

        private static Texture2D CreatePointTexture()
        {
            const int size = 32;

            var texture = new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "Runtime Galaxy Star Point Texture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var center = (size - 1) * 0.5f;
            var radius = center;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = (x - center) / radius;
                    var dy = (y - center) / radius;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);
                    var alpha = Mathf.Clamp01(1f - distance);
                    alpha *= alpha;

                    texture.SetPixel(
                        x,
                        y,
                        new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply(false, true);
            return texture;
        }

        private static void EnsureMatrixStorage(
            int instanceCount,
            ref Matrix4x4[][] matrices)
        {
            var requiredBatchCount =
                (instanceCount + MaximumInstancesPerDrawCall - 1) /
                MaximumInstancesPerDrawCall;

            if (matrices.Length == requiredBatchCount)
                return;

            matrices = new Matrix4x4[requiredBatchCount][];

            for (var i = 0; i < requiredBatchCount; i++)
                matrices[i] = new Matrix4x4[MaximumInstancesPerDrawCall];
        }
    }

/// <summary>
    /// Streams real SolarSystemLocationData around the current galaxy-space
    /// anchor and draws it as camera-facing star points.
    ///
    /// Every rendered point comes from GalaxySectorGenerator. The same ID and
    /// location can later resolve into a full solar-system LOD, so points do
    /// not jump to another position during a handoff.
    /// </summary>
    public sealed class StellarFieldRenderer
    {
        private const int MaximumInstancesPerDrawCall = 1023;
        private const int DefaultSectorsPerJob = 192;
        private const int DefaultJobBatchSize = 32;
        private const int DefaultSectorsAppliedPerFrame = 192;
        private const int CachedBorderInSectors = 2;
        private const int ImmediateCoreHorizontalRadius = 2;
        private const int ImmediateCoreVerticalRadius = 2;
        private const float UnityUnitsPerLightYear = 1f;
        private const float DefaultMinimumStarPixels = 2.5f;
        private const float MaximumStarDiameter = 1.5f;

        private readonly struct VisibleStar
        {
            public readonly SolarSystemLocationData Location;
            public readonly double DistanceSquaredLightYears;

            public VisibleStar(
                SolarSystemLocationData location,
                double distanceSquaredLightYears)
            {
                Location = location;
                DistanceSquaredLightYears = distanceSquaredLightYears;
            }
        }

        private GalaxySpaceAnchor galaxyAnchor;
        private Camera celestialCamera;
        private LayerMask celestialLayer;
        private Mesh starPointMesh;
        private Material starPointMaterial;

        private readonly Dictionary<StreamingSectorKey, List<SolarSystemLocationData>>
            _loadedSectors = new();

        private readonly HashSet<StreamingSectorKey> _desiredSectors = new();
        private readonly HashSet<StreamingSectorKey> _loadingSectors = new();
        private readonly Queue<int3> _sectorQueue = new();

        private readonly List<VisibleStar> _visibleStars = new();
        private Matrix4x4[][] _matrices = Array.Empty<Matrix4x4[]>();

        private NativeArray<int3> _pendingCoordinates;
        private NativeArray<GalaxySectorData> _pendingResults;
        private JobHandle _pendingJob;
        private bool _hasPendingJob;
        private bool _pendingJobCompleted;
        private int _pendingResultIndex;

        private MaterialPropertyBlock _propertyBlock;
        private Mesh _runtimePointMesh;
        private Material _runtimePointMaterial;
        private Texture2D _runtimePointTexture;

        private ulong _loadedGalaxySeed;
        private int3 _lastCenterSector;
        private bool _hasCenterSector;

        private int _horizontalSectorRadius = 12;
        private int _verticalSectorRadius = 1;
        private int _maximumVisibleStars = 12_000;
        private float _minimumStarPixels = DefaultMinimumStarPixels;
        private bool _suppressAnchorSolarSystemPoint;
        private bool _hasAnchorSolarSystemPointOverride;
        private Color _anchorSolarSystemPointColor = Color.white;
        private float _anchorSolarSystemPointIntensity = 1.5f;
        private bool _hasExplicitAnchorSolarSystemLocation;
        private SolarSystemLocationData _explicitAnchorSolarSystemLocation;
        private bool _hasAnchorSolarSystemLocation;
        private SolarSystemLocationData _anchorSolarSystemLocation;

        internal void Configure(
            GalaxySpaceAnchor anchor,
            Camera frameCamera,
            LayerMask frameLayer,
            int horizontalSectorRadius,
            int verticalSectorRadius,
            int maximumVisibleStars,
            float minimumStarPixels)
        {
            var clampedMinimumStarPixels = Mathf.Max(
                0.25f,
                minimumStarPixels);

            var changed =
                galaxyAnchor != anchor ||
                celestialCamera != frameCamera ||
                celestialLayer.value != frameLayer.value ||
                _horizontalSectorRadius != horizontalSectorRadius ||
                _verticalSectorRadius != verticalSectorRadius ||
                _maximumVisibleStars != maximumVisibleStars ||
                !Mathf.Approximately(
                    _minimumStarPixels,
                    clampedMinimumStarPixels);

            galaxyAnchor = anchor;
            celestialCamera = frameCamera;
            celestialLayer = frameLayer;
            _horizontalSectorRadius = Mathf.Max(1, horizontalSectorRadius);
            _verticalSectorRadius = Mathf.Max(0, verticalSectorRadius);
            _maximumVisibleStars = Mathf.Max(128, maximumVisibleStars);
            _minimumStarPixels = clampedMinimumStarPixels;

            if (changed)
                ForceRefresh();
        }

        internal void SetAnchorSolarSystemPointSuppressed(bool suppressed)
        {
            _suppressAnchorSolarSystemPoint = suppressed;
        }

        internal void SetAnchorSolarSystemPointOverride(
            Color baseColor,
            float intensity)
        {
            _hasAnchorSolarSystemPointOverride = true;
            _anchorSolarSystemPointColor = baseColor;
            _anchorSolarSystemPointIntensity = Mathf.Max(0.0f, intensity);
        }

        internal void ClearAnchorSolarSystemPointOverride()
        {
            _hasAnchorSolarSystemPointOverride = false;
            _anchorSolarSystemPointColor = Color.white;
            _anchorSolarSystemPointIntensity = 1.5f;
        }

        /// <summary>
        /// Supplies the exact current solar-system location for the LOD 0
        /// point. Gameplay-facing solar-system IDs are not necessarily part
        /// of the density-driven sector catalogue, so relying on a matching
        /// streamed sector entry can leave the active system with no distant
        /// point to return to after its LOD 1 proxy is unloaded.
        /// </summary>
        internal void SetAnchorSolarSystemLocation(
            in SolarSystemLocationData location)
        {
            _explicitAnchorSolarSystemLocation = location;
            _hasExplicitAnchorSolarSystemLocation = true;
        }

        internal void ClearAnchorSolarSystemLocation()
        {
            _explicitAnchorSolarSystemLocation = default;
            _hasExplicitAnchorSolarSystemLocation = false;
            _anchorSolarSystemLocation = default;
            _hasAnchorSolarSystemLocation = false;
        }

        public int LoadedSectorCount => _loadedSectors.Count;

        public int VisibleStarCount => _visibleStars.Count;




        public void Dispose()
        {
            CompleteAndDisposePendingGeneration();
            ClearCachedSectors();
            _loadedGalaxySeed = 0UL;
            _hasCenterSector = false;

            if (_runtimePointMesh != null)
                UnityEngine.Object.Destroy(_runtimePointMesh);

            if (_runtimePointMaterial != null)
                UnityEngine.Object.Destroy(_runtimePointMaterial);

            if (_runtimePointTexture != null)
                UnityEngine.Object.Destroy(_runtimePointTexture);
        }

        public void ForceRefresh()
        {
            CompleteAndDisposePendingGeneration();
            ClearCachedSectors();
            _hasCenterSector = false;
        }

        public void Tick()
        {
            if (galaxyAnchor == null || !galaxyAnchor.HasResolvedGalaxy)
                return;

            if (_loadedGalaxySeed != galaxyAnchor.Galaxy.Seed)
            {
                ForceRefresh();
                _loadedGalaxySeed = galaxyAnchor.Galaxy.Seed;
            }

            var centerSector = GalaxySectorUtility.GetCoordinates(
                galaxyAnchor.GalaxyLocalPositionLightYears);

            if (!_hasCenterSector || !centerSector.Equals(_lastCenterSector))
            {
                _lastCenterSector = centerSector;
                _hasCenterSector = true;
                RebuildSectorRequests(centerSector);
            }

            CompletePendingGenerationIfReady();

            if (!_hasPendingJob)
                ScheduleNextSectorBatch();

            RenderStars();
        }

        private void RebuildSectorRequests(int3 centerSector)
        {
            _desiredSectors.Clear();

            var horizontalRadiusSquared =
                _horizontalSectorRadius * _horizontalSectorRadius;

            var missing = new List<int3>();

            for (var z = -_horizontalSectorRadius;
                 z <= _horizontalSectorRadius;
                 z++)
            {
                for (var y = -_verticalSectorRadius;
                     y <= _verticalSectorRadius;
                     y++)
                {
                    for (var x = -_horizontalSectorRadius;
                         x <= _horizontalSectorRadius;
                         x++)
                    {
                        if (x * x + z * z > horizontalRadiusSquared)
                            continue;

                        var coordinates = centerSector + new int3(x, y, z);

                        if (!SolarSystemIDUtility.IsSectorCoordinateInRange(
                                coordinates))
                        {
                            continue;
                        }

                        var key = new StreamingSectorKey(coordinates);
                        _desiredSectors.Add(key);

                        if (!_loadedSectors.ContainsKey(key) &&
                            !_loadingSectors.Contains(key))
                        {
                            missing.Add(coordinates);
                        }
                    }
                }
            }

            missing.Sort((left, right) =>
                GetSectorDistanceSquared(left, centerSector).CompareTo(
                    GetSectorDistanceSquared(right, centerSector)));

            _sectorQueue.Clear();
            for (var i = 0; i < missing.Count; i++)
                _sectorQueue.Enqueue(missing[i]);

            GenerateImmediateCore(centerSector);
            PruneDistantCachedSectors(centerSector);
        }

        private void GenerateImmediateCore(int3 centerSector)
        {
            var horizontalRadiusSquared =
                ImmediateCoreHorizontalRadius * ImmediateCoreHorizontalRadius;

            for (var z = -ImmediateCoreHorizontalRadius;
                 z <= ImmediateCoreHorizontalRadius;
                 z++)
            {
                for (var y = -ImmediateCoreVerticalRadius;
                     y <= ImmediateCoreVerticalRadius;
                     y++)
                {
                    for (var x = -ImmediateCoreHorizontalRadius;
                         x <= ImmediateCoreHorizontalRadius;
                         x++)
                    {
                        if (x * x + z * z > horizontalRadiusSquared)
                            continue;

                        var coordinates = centerSector + new int3(x, y, z);
                        var key = new StreamingSectorKey(coordinates);

                        if (!_desiredSectors.Contains(key) ||
                            _loadedSectors.ContainsKey(key) ||
                            _loadingSectors.Contains(key) ||
                            !SolarSystemIDUtility.IsSectorCoordinateInRange(
                                coordinates))
                        {
                            continue;
                        }

                        AddSector(GalaxySectorGenerator.Generate(
                            galaxyAnchor.Galaxy,
                            coordinates));
                    }
                }
            }
        }

        private void PruneDistantCachedSectors(int3 centerSector)
        {
            var retainedHorizontalRadius =
                _horizontalSectorRadius + CachedBorderInSectors;
            var retainedVerticalRadius =
                _verticalSectorRadius + CachedBorderInSectors;
            var retainedRadiusSquared =
                retainedHorizontalRadius * retainedHorizontalRadius;

            var toRemove = new List<StreamingSectorKey>();

            foreach (var pair in _loadedSectors)
            {
                var delta = new int3(
                    pair.Key.X - centerSector.x,
                    pair.Key.Y - centerSector.y,
                    pair.Key.Z - centerSector.z);

                if (math.abs(delta.y) <= retainedVerticalRadius &&
                    delta.x * delta.x + delta.z * delta.z <=
                    retainedRadiusSquared)
                {
                    continue;
                }

                toRemove.Add(pair.Key);
            }

            for (var i = 0; i < toRemove.Count; i++)
                _loadedSectors.Remove(toRemove[i]);
        }

        private void ScheduleNextSectorBatch()
        {
            if (_sectorQueue.Count == 0 || galaxyAnchor == null)
                return;

            var coordinates = new List<int3>(DefaultSectorsPerJob);

            while (_sectorQueue.Count > 0 &&
                   coordinates.Count < DefaultSectorsPerJob)
            {
                var next = _sectorQueue.Dequeue();
                var key = new StreamingSectorKey(next);

                if (!_desiredSectors.Contains(key) ||
                    _loadedSectors.ContainsKey(key) ||
                    _loadingSectors.Contains(key))
                {
                    continue;
                }

                coordinates.Add(next);
                _loadingSectors.Add(key);
            }

            if (coordinates.Count == 0)
                return;

            _pendingCoordinates = new NativeArray<int3>(
                coordinates.Count,
                Allocator.Persistent);

            _pendingResults = new NativeArray<GalaxySectorData>(
                coordinates.Count,
                Allocator.Persistent);

            for (var i = 0; i < coordinates.Count; i++)
                _pendingCoordinates[i] = coordinates[i];

            _pendingResultIndex = 0;
            _pendingJobCompleted = false;
            _pendingJob = GalaxySectorBatchGenerator.Schedule(
                galaxyAnchor.Galaxy,
                _pendingCoordinates,
                _pendingResults,
                DefaultJobBatchSize);
            _hasPendingJob = true;
        }

        private void CompletePendingGenerationIfReady()
        {
            if (!_hasPendingJob)
                return;

            if (!_pendingJobCompleted)
            {
                if (!_pendingJob.IsCompleted)
                    return;

                _pendingJob.Complete();
                _pendingJobCompleted = true;
            }

            var applied = 0;

            while (_pendingResultIndex < _pendingResults.Length &&
                   applied < DefaultSectorsAppliedPerFrame)
            {
                var sector = _pendingResults[_pendingResultIndex++];
                var key = new StreamingSectorKey(sector.Coordinates);
                _loadingSectors.Remove(key);

                if (_desiredSectors.Contains(key))
                    AddSector(sector);

                applied++;
            }

            if (_pendingResultIndex >= _pendingResults.Length)
                DisposePendingGeneration();
        }

        private void CompleteAndDisposePendingGeneration()
        {
            if (!_hasPendingJob)
                return;

            _pendingJob.Complete();
            DisposePendingGeneration();
        }

        private void DisposePendingGeneration()
        {
            if (_pendingCoordinates.IsCreated)
                _pendingCoordinates.Dispose();

            if (_pendingResults.IsCreated)
                _pendingResults.Dispose();

            _hasPendingJob = false;
            _pendingJobCompleted = false;
            _pendingResultIndex = 0;
            _loadingSectors.Clear();
        }

        private void AddSector(GalaxySectorData sector)
        {
            var key = new StreamingSectorKey(sector.Coordinates);
            if (_loadedSectors.ContainsKey(key))
                return;

            var systems = new List<SolarSystemLocationData>(
                sector.SolarSystems.Length);

            for (var i = 0; i < sector.SolarSystems.Length; i++)
                systems.Add(sector.SolarSystems[i]);

            _loadedSectors.Add(key, systems);
        }

        private void ClearCachedSectors()
        {
            _loadedSectors.Clear();
            _desiredSectors.Clear();
            _loadingSectors.Clear();
            _sectorQueue.Clear();
            _visibleStars.Clear();
            _matrices = Array.Empty<Matrix4x4[]>();
        }

        private void RenderStars()
        {
            var camera = ResolveCamera();
            var mesh = ResolvePointMesh();
            var material = ResolvePointMaterial();

            if (camera == null || mesh == null || material == null ||
                galaxyAnchor == null)
            {
                return;
            }

            CollectVisibleStars();
            var shouldRenderAnchorPoint = ShouldRenderAnchorSolarSystemPoint();

            if (_visibleStars.Count == 0 && !shouldRenderAnchorPoint)
                return;

            var anchorPosition = galaxyAnchor.GalaxyLocalPositionLightYears;
            var cameraRotation = camera.transform.rotation;
            var visibleCount = 0;

            if (_visibleStars.Count > 0)
            {
                _visibleStars.Sort((left, right) =>
                    left.DistanceSquaredLightYears.CompareTo(
                        right.DistanceSquaredLightYears));

                EnsureMatrixStorage(_visibleStars.Count, ref _matrices);

                for (var i = 0; i < _visibleStars.Count; i++)
                {
                    var star = _visibleStars[i].Location;
                    var relative =
                        star.GalaxyLocalPositionLightYears - anchorPosition;
                    var position = ToUnityPosition(relative);

                    if (!IsInCameraFrustum(camera, position))
                        continue;

                    var batchIndex =
                        visibleCount / MaximumInstancesPerDrawCall;
                    var instanceIndex =
                        visibleCount % MaximumInstancesPerDrawCall;
                    var diameter = GetPointDiameter(
                        camera,
                        position.magnitude,
                        star.EstimatedSystemMassSolarMasses);

                    _matrices[batchIndex][instanceIndex] = Matrix4x4.TRS(
                        position,
                        cameraRotation,
                        Vector3.one * diameter);

                    visibleCount++;
                }
            }

            if (visibleCount == 0 && !shouldRenderAnchorPoint)
                return;

            _propertyBlock ??= new MaterialPropertyBlock();

            if (visibleCount > 0)
            {
                _propertyBlock.Clear();
                _propertyBlock.SetColor("_Color", Color.white);
                _propertyBlock.SetColor("_BaseColor", Color.white);
                _propertyBlock.SetColor(
                    "_EmissionColor",
                    Color.white * 1.5f);
                _propertyBlock.SetFloat("_Intensity", 1.0f);

                var drawn = 0;
                for (var batchIndex = 0;
                     batchIndex < _matrices.Length && drawn < visibleCount;
                     batchIndex++)
                {
                    var count = Mathf.Min(
                        MaximumInstancesPerDrawCall,
                        visibleCount - drawn);

                    Graphics.DrawMeshInstanced(
                        mesh,
                        0,
                        material,
                        _matrices[batchIndex],
                        count,
                        _propertyBlock,
                        ShadowCastingMode.Off,
                        false,
                        ReferenceFrameLayerUtility
                            .GetSingleLayerIndexOrDefault(
                                celestialLayer),
                        null,
                        LightProbeUsage.Off,
                        null);

                    drawn += count;
                }
            }

            if (shouldRenderAnchorPoint)
                RenderAnchorSolarSystemPoint(camera, mesh, material);
        }


        private void CollectVisibleStars()
        {
            _visibleStars.Clear();

            // The active system is rendered in a separate draw call. This
            // guarantees a LOD 0 fallback even for a logical gameplay ID
            // that is absent from the streamed density catalogue, and avoids
            // duplicating it when the same system is present in a sector.
            _hasAnchorSolarSystemLocation =
                _hasExplicitAnchorSolarSystemLocation;

            if (_hasAnchorSolarSystemLocation)
            {
                _anchorSolarSystemLocation =
                    _explicitAnchorSolarSystemLocation;
            }

            var anchorPosition = galaxyAnchor.GalaxyLocalPositionLightYears;
            var anchorSolarSystemID =
                galaxyAnchor.Coordinates.SolarSystemID;
            var renderRadiusLightYears =
                (_horizontalSectorRadius + CachedBorderInSectors) *
                GalaxySectorGenerator.SECTOR_SIZE_LIGHT_YEARS;
            var renderRadiusSquared =
                renderRadiusLightYears * renderRadiusLightYears;

            foreach (var sector in _loadedSectors.Values)
            {
                for (var i = 0; i < sector.Count; i++)
                {
                    var location = sector[i];

                    if (location.SolarSystemID == anchorSolarSystemID)
                    {
                        if (!_hasAnchorSolarSystemLocation)
                        {
                            _anchorSolarSystemLocation = location;
                            _hasAnchorSolarSystemLocation = true;
                        }

                        continue;
                    }

                    var relative =
                        location.GalaxyLocalPositionLightYears - anchorPosition;
                    var distanceSquared = math.dot(relative, relative);

                    if (distanceSquared > renderRadiusSquared)
                        continue;

                    _visibleStars.Add(new VisibleStar(
                        location,
                        distanceSquared));
                }
            }

            if (_visibleStars.Count > _maximumVisibleStars)
            {
                _visibleStars.Sort((left, right) =>
                    left.DistanceSquaredLightYears.CompareTo(
                        right.DistanceSquaredLightYears));

                _visibleStars.RemoveRange(
                    _maximumVisibleStars,
                    _visibleStars.Count - _maximumVisibleStars);
            }
        }

        private bool ShouldRenderAnchorSolarSystemPoint()
        {
            return !_suppressAnchorSolarSystemPoint &&
                   _hasAnchorSolarSystemLocation;
        }

        private void RenderAnchorSolarSystemPoint(
            Camera camera,
            Mesh mesh,
            Material material)
        {
            if (!ShouldRenderAnchorSolarSystemPoint() ||
                galaxyAnchor == null)
            {
                return;
            }

            var relative =
                _anchorSolarSystemLocation.GalaxyLocalPositionLightYears -
                galaxyAnchor.GalaxyLocalPositionLightYears;
            var position = ToUnityPosition(relative);

            if (!IsInCameraFrustum(camera, position))
                return;

            var diameter = GetPointDiameter(
                camera,
                position.magnitude,
                _anchorSolarSystemLocation.EstimatedSystemMassSolarMasses);
            var matrix = Matrix4x4.TRS(
                position,
                camera.transform.rotation,
                Vector3.one * diameter);

            _propertyBlock ??= new MaterialPropertyBlock();
            _propertyBlock.Clear();
            _propertyBlock.SetColor("_Color", _anchorSolarSystemPointColor);
            _propertyBlock.SetColor(
                "_BaseColor",
                _anchorSolarSystemPointColor);
            _propertyBlock.SetColor(
                "_EmissionColor",
                _anchorSolarSystemPointColor *
                _anchorSolarSystemPointIntensity);
            _propertyBlock.SetFloat(
                "_Intensity",
                _anchorSolarSystemPointIntensity);

            Graphics.DrawMesh(
                mesh,
                matrix,
                material,
                ReferenceFrameLayerUtility.GetSingleLayerIndexOrDefault(
                    celestialLayer),
                null,
                0,
                _propertyBlock,
                ShadowCastingMode.Off,
                false,
                null,
                LightProbeUsage.Off,
                null);
        }





        private float GetPointDiameter(
            Camera camera,
            float distance,
            double estimatedMassSolarMasses)
        {
            var halfFovRadians = camera.fieldOfView * Mathf.Deg2Rad * 0.5f;
            var unitsPerPixel =
                2f * Mathf.Max(distance, camera.nearClipPlane) *
                Mathf.Tan(halfFovRadians) /
                Mathf.Max(1, Screen.height);

            var massBrightness = Mathf.Clamp(
                Mathf.Sqrt(Mathf.Max(0.05f, (float)estimatedMassSolarMasses)),
                0.6f,
                2.4f);

            return Mathf.Min(
                unitsPerPixel * _minimumStarPixels * massBrightness,
                MaximumStarDiameter);
        }

        private static bool IsInCameraFrustum(
            Camera camera,
            Vector3 position)
        {
            var local = camera.transform.InverseTransformPoint(position);

            if (local.z < camera.nearClipPlane ||
                local.z > camera.farClipPlane)
            {
                return false;
            }

            var halfHeight = local.z * Mathf.Tan(
                camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            var halfWidth = halfHeight * camera.aspect;

            return Mathf.Abs(local.x) <= halfWidth &&
                   Mathf.Abs(local.y) <= halfHeight;
        }

        private Camera ResolveCamera()
        {
            return celestialCamera != null
                ? celestialCamera
                : Camera.main;
        }

        private Vector3 ToUnityPosition(double3 relativeLightYears)
        {
            return new Vector3(
                (float)(relativeLightYears.x * UnityUnitsPerLightYear),
                (float)(relativeLightYears.y * UnityUnitsPerLightYear),
                (float)(relativeLightYears.z * UnityUnitsPerLightYear));
        }

        private Mesh ResolvePointMesh()
        {
            if (starPointMesh != null)
                return starPointMesh;

            if (_runtimePointMesh != null)
                return _runtimePointMesh;

            _runtimePointMesh = new Mesh
            {
                name = "Runtime Stellar Point"
            };

            _runtimePointMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f)
            };

            _runtimePointMesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };

            _runtimePointMesh.triangles = new[]
            {
                0, 2, 1,
                0, 3, 2
            };

            _runtimePointMesh.RecalculateBounds();
            return _runtimePointMesh;
        }

        private Material ResolvePointMaterial()
        {
            var material = starPointMaterial != null
                ? starPointMaterial
                : CreateRuntimePointMaterial();

            if (material != null)
                material.enableInstancing = true;

            return material;
        }

        private Material CreateRuntimePointMaterial()
        {
            if (_runtimePointMaterial != null)
                return _runtimePointMaterial;

            var shader = Shader.Find("SpaceEngine/Streaming/Star Point");

            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");

            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            if (shader == null)
                return null;

            _runtimePointMaterial = new Material(shader)
            {
                name = "Runtime Stellar Point Material",
                enableInstancing = true,
                renderQueue = shader.name == "SpaceEngine/Streaming/Star Point"
                    ? (int)RenderQueue.Background
                    : (int)RenderQueue.Transparent
            };

            if (shader.name != "SpaceEngine/Streaming/Star Point")
            {
                _runtimePointTexture = CreatePointTexture();

                if (_runtimePointMaterial.HasProperty("_BaseMap"))
                    _runtimePointMaterial.SetTexture("_BaseMap", _runtimePointTexture);

                if (_runtimePointMaterial.HasProperty("_MainTex"))
                    _runtimePointMaterial.SetTexture("_MainTex", _runtimePointTexture);
            }

            if (_runtimePointMaterial.HasProperty("_BaseColor"))
                _runtimePointMaterial.SetColor("_BaseColor", Color.white);

            if (_runtimePointMaterial.HasProperty("_Color"))
                _runtimePointMaterial.SetColor("_Color", Color.white);

            if (_runtimePointMaterial.HasProperty("_Surface"))
                _runtimePointMaterial.SetFloat("_Surface", 1f);

            if (_runtimePointMaterial.HasProperty("_Blend"))
                _runtimePointMaterial.SetFloat("_Blend", 1f);

            if (_runtimePointMaterial.HasProperty("_ZWrite"))
                _runtimePointMaterial.SetFloat("_ZWrite", 0f);

            if (_runtimePointMaterial.HasProperty("_Cull"))
                _runtimePointMaterial.SetFloat("_Cull", 0f);

            if (_runtimePointMaterial.HasProperty("_Intensity"))
                _runtimePointMaterial.SetFloat("_Intensity", 1.15f);

            if (_runtimePointMaterial.HasProperty("_Softness"))
                _runtimePointMaterial.SetFloat("_Softness", 2.5f);

            return _runtimePointMaterial;
        }

        private static Texture2D CreatePointTexture()
        {
            const int size = 32;

            var texture = new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "Runtime Stellar Point Texture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var center = (size - 1) * 0.5f;
            var radius = center;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = (x - center) / radius;
                    var dy = (y - center) / radius;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);
                    var alpha = Mathf.Clamp01(1f - distance);
                    alpha *= alpha;

                    texture.SetPixel(
                        x,
                        y,
                        new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply(false, true);
            return texture;
        }

        private static void EnsureMatrixStorage(
            int instanceCount,
            ref Matrix4x4[][] matrices)
        {
            var requiredBatchCount =
                (instanceCount + MaximumInstancesPerDrawCall - 1) /
                MaximumInstancesPerDrawCall;

            if (matrices.Length == requiredBatchCount)
                return;

            matrices = new Matrix4x4[requiredBatchCount][];

            for (var i = 0; i < requiredBatchCount; i++)
            {
                matrices[i] =
                    new Matrix4x4[MaximumInstancesPerDrawCall];
            }
        }

        private static int GetSectorDistanceSquared(int3 left, int3 right)
        {
            var delta = left - right;
            return delta.x * delta.x +
                   delta.y * delta.y +
                   delta.z * delta.z;
        }
    }

/// <summary>
    /// Renders stars and planets from the active solar system in a separate
    /// scaled reference frame. LOD 1 keeps true orbital positions and uses a
    /// fixed presentation scale; stars add a camera-facing HDR glare. LOD 2
    /// replaces the proxy with a body-specific detailed renderer at the same
    /// fixed position and scale, with no approach warp or distance-dependent
    /// enlargement.
    /// </summary>
    public sealed class SolarSystemScaledSpaceRenderer
    {
        // The accretion-disk mesh is authored around a unit sphere whose
        // transform has a diameter of two physical horizon radii. Keeping the
        // outer mesh radius here lets the LOD 1 disk use a screen-size floor
        // without inflating the event horizon itself.
        private const float BlackHoleAccretionDiskInnerMeshRadius = 1.55f;
        private const float BlackHoleAccretionDiskOuterMeshRadius = 8.75f;

        public readonly struct StarProximityData
        {
            public readonly int StarIndex;
            public readonly double DistanceToCentreMeters;
            public readonly double DistanceToSurfaceMeters;
            public readonly double RadiusMeters;

            internal StarProximityData(
                int starIndex,
                double distanceToCentreMeters,
                double distanceToSurfaceMeters,
                double radiusMeters)
            {
                StarIndex = starIndex;
                DistanceToCentreMeters = distanceToCentreMeters;
                DistanceToSurfaceMeters = distanceToSurfaceMeters;
                RadiusMeters = radiusMeters;
            }
        }

        private sealed class StarVisual
        {
            public StarData Data;
            public Transform Transform;
            public MeshRenderer Renderer;
            public Transform CoronaTransform;
            public MeshRenderer CoronaRenderer;
            public Material Material;
            public Material CoronaMaterial;
            public Transform Lod1LightTransform;
            public MeshRenderer Lod1LightRenderer;
            public Material Lod1LightMaterial;
            public Transform AccretionDiskTransform;
            public MeshRenderer AccretionDiskRenderer;
            public Material AccretionDiskMaterial;
            public double3 BarycentricPositionMeters;
        }

        /// <summary>
        /// Runtime-only high-density LOD 2 star visual. Its root lives in
        /// the scaled-space root rather than under the LOD 1 sphere, so the
        /// detailed representation can reuse the stable body transform.
        /// </summary>
        private sealed class CloseStarSurfaceVisual
        {
            public int StarIndex = -1;
            public Transform Root;
            public Transform CoronaTransform;
            public MeshRenderer SurfaceRenderer;
            public MeshRenderer CoronaRenderer;
            public Material SurfaceMaterial;
            public Material CoronaMaterial;
            // The prominence effect is built from intersecting animated gas sheets.
            // It deliberately does not use solid tube primitives.
            public Material ProminenceMaterial;
            public readonly List<Mesh> ProminenceMeshes = new();
        }

        /// <summary>
        /// Runtime-only detailed black-hole visual. The horizon is a real
        /// sphere at the generated Schwarzschild radius; the larger shell
        /// only renders the optical lensing region around it.
        /// </summary>
        private sealed class CloseBlackHoleVisual
        {
            public int StarIndex = -1;
            public Transform Root;
            public MeshRenderer HorizonRenderer;
            public Transform LensingShellTransform;
            public MeshRenderer LensingShellRenderer;
            public Transform AccretionDiskTransform;
            public MeshRenderer AccretionDiskRenderer;
            public Material HorizonMaterial;
            public Material LensingMaterial;
            public Material AccretionDiskMaterial;
        }

        private sealed class PlanetVisual
        {
            public PlanetData Data;
            public Transform Transform;
            public Material Material;
            public double3 BarycentricPositionMeters;
        }

        /// <summary>
        /// Built-in LOD 2 implementation for stars. It uses the common
        /// interface that future planet, moon and asteroid generators use,
        /// but owns the high-density stellar mesh and plasma materials.
        /// </summary>
        private sealed class StarLocalSurfaceLodRenderer :
            ICelestialBodyLodRenderer,
            IDisposable
        {
            private readonly SolarSystemScaledSpaceRenderer _owner;
            private readonly int _starIndex;

            public StarLocalSurfaceLodRenderer(
                SolarSystemScaledSpaceRenderer owner,
                int starIndex)
            {
                _owner = owner;
                _starIndex = starIndex;
            }

            public CelestialBodyRenderKey Key => new(
                CelestialBodyKind.Star,
                _starIndex);

            public bool IsActive =>
                _owner.IsStarLocalSurfaceActive(_starIndex);

            public void SetLodActive(bool isActive)
            {
                _owner.SetStarLocalSurfaceActive(_starIndex, isActive);
            }

            public void UpdateLod(in CelestialBodyLodContext context)
            {
                _owner.UpdateStarLocalSurfaceVisual(_starIndex, context);
            }

            public void Dispose()
            {
                _owner.DestroyStarLocalSurfaceVisual(_starIndex);
            }
        }

        /// <summary>
        /// Black holes share the stellar-system body key, but own a separate
        /// detailed renderer: an event-horizon sphere, optional accretion
        /// disk, and a screen-colour lensing shell.
        /// </summary>
        private sealed class BlackHoleLocalLodRenderer :
            ICelestialBodyLodRenderer,
            IDisposable
        {
            private readonly SolarSystemScaledSpaceRenderer _owner;
            private readonly int _starIndex;

            public BlackHoleLocalLodRenderer(
                SolarSystemScaledSpaceRenderer owner,
                int starIndex)
            {
                _owner = owner;
                _starIndex = starIndex;
            }

            public CelestialBodyRenderKey Key => new(
                CelestialBodyKind.Star,
                _starIndex);

            public bool IsActive =>
                _owner.IsBlackHoleLocalVisualActive(_starIndex);

            public void SetLodActive(bool isActive)
            {
                _owner.SetBlackHoleLocalVisualActive(_starIndex, isActive);
            }

            public void UpdateLod(in CelestialBodyLodContext context)
            {
                _owner.UpdateBlackHoleLocalVisual(_starIndex, context);
            }

            public void Dispose()
            {
                _owner.DestroyBlackHoleLocalVisual(_starIndex);
            }
        }

        private readonly Transform ownerTransform;
        private Camera celestialCamera;
        private SeamlessSpaceAnchor spaceAnchor;
        private Transform visualRoot;
        private Mesh sphereMesh;
        private Material starMaterial;
        private Material planetMaterial;
        private Material coronaMaterial;
        private LayerMask scaledSpaceLayer = 0;
        private double scaledSpaceMetersPerUnityUnit = 10_000_000.0;
        private float minimumStarAngularDiameterDegrees = 0.06f;
        private float minimumPlanetAngularDiameterDegrees = 0.004f;
        private float minimumPlanetDiameterInUnityUnits = 1.0f;
        private float minimumStarDiameterInUnityUnits = 8.0f;
        // The visible magnetic corona hugs the photosphere.
        private float coronaRadiusMultiplier = 1.055f;
        private float starLod1LightRadiusMultiplier = 10.0f;
        private float starLod1LightIntensity = 8.0f;
        private float starLod1LightRayStrength = 0.65f;
        private float starLod1LightRayCount = 8.0f;
        private bool starLod2LocalSurfaceEnabled = true;
        private double starLod2SurfaceActivationDistanceInRadii = 64.0;
        private double starLod2SurfaceDeactivationDistanceInRadii = 80.0;
        private bool planetLod2LocalSurfaceEnabled = true;
        private double planetLod2SurfaceActivationDistanceInRadii = 24.0;
        private double planetLod2SurfaceDeactivationDistanceInRadii = 32.0;

        private readonly List<StarVisual> _stars = new();
        private readonly List<PlanetVisual> _planets = new();
        private readonly Dictionary<CelestialBodyRenderKey,
            ICelestialBodyLodRenderer> _lod2Renderers = new();
        private readonly List<ICelestialBodyLodRenderer>
            _ownedLod2Renderers = new();

        private SolarSystemData _solarSystem;
        private ulong _loadedSystemSeed;
        private double _simulationTimeSeconds;
        private double _totalStarMassKg;
        private bool _isVisible;
        private bool _isNearStarSurface;

        private Material _runtimeFallbackMaterial;
        private Mesh _runtimeSphereMesh;
        private Mesh _runtimeLod1LightQuadMesh;
        private Mesh _runtimeCloseStarSurfaceMesh;
        private Mesh _runtimeBlackHoleAccretionDiskMesh;
        private CloseStarSurfaceVisual _closeStarSurfaceVisual;
        private CloseBlackHoleVisual _closeBlackHoleVisual;

        /// <summary>
        /// Fires when the nearest star enters or leaves the high-detail
        /// local-surface LOD 2 range. The body data and coordinate frames
        /// remain unchanged across the transition.
        /// </summary>
        public event Action<bool> StarSurfaceLodChanged;

        public bool IsVisible => _isVisible;

        public bool IsNearStarSurface => _isNearStarSurface;

        public long LoadedSolarSystemID =>
            spaceAnchor != null && spaceAnchor.IsConfigured
                ? spaceAnchor.Coordinates.SolarSystemID
                : 0L;

        /// <summary>
        /// Registers a body-specific LOD 2 renderer for the active solar
        /// system. Planet, moon and asteroid generators call this after they
        /// have created their own mesh/material implementation.
        /// </summary>
        public bool RegisterLod2Renderer(
            ICelestialBodyLodRenderer lodRenderer)
        {
            if (lodRenderer == null)
                return false;

            var key = lodRenderer.Key;

            if (_lod2Renderers.TryGetValue(key, out var existing) &&
                !ReferenceEquals(existing, lodRenderer))
            {
                return false;
            }

            _lod2Renderers[key] = lodRenderer;
            return true;
        }

        public bool UnregisterLod2Renderer(
            ICelestialBodyLodRenderer lodRenderer)
        {
            if (lodRenderer == null ||
                !_lod2Renderers.TryGetValue(
                    lodRenderer.Key,
                    out var registered) ||
                !ReferenceEquals(registered, lodRenderer))
            {
                return false;
            }

            registered.SetLodActive(false);
            _lod2Renderers.Remove(lodRenderer.Key);
            _ownedLod2Renderers.Remove(lodRenderer);
            return true;
        }

        public SolarSystemScaledSpaceRenderer(
            Transform ownerTransform)
        {
            this.ownerTransform = ownerTransform ??
                throw new ArgumentNullException(nameof(ownerTransform));
            ValidateLodSettings();
            EnsureVisualRoot();
        }

        internal void Configure(
            SeamlessSpaceAnchor anchor,
            Camera frameCamera,
            LayerMask frameLayer,
            float minimumStarAngularDiameter,
            float minimumPlanetAngularDiameter,
            double presentationMetersPerUnityUnit,
            float minimumStarDiameterInUnits,
            float minimumPlanetDiameterInUnits,
            float lod1LightRadiusMultiplier,
            float lod1LightIntensity,
            float lod1LightRayStrength,
            float lod1LightRayCount,
            bool enableStarLod2LocalSurface,
            double lod2StarSurfaceActivationDistanceInRadii,
            double lod2StarSurfaceDeactivationDistanceInRadii,
            bool enablePlanetLod2LocalSurface,
            double lod2PlanetSurfaceActivationDistanceInRadii,
            double lod2PlanetSurfaceDeactivationDistanceInRadii)
        {
            spaceAnchor = anchor;
            celestialCamera = frameCamera;
            scaledSpaceLayer = frameLayer;
            minimumStarAngularDiameterDegrees = Mathf.Max(
                0.001f,
                minimumStarAngularDiameter);
            minimumPlanetAngularDiameterDegrees = Mathf.Max(
                0.0f,
                minimumPlanetAngularDiameter);
            scaledSpaceMetersPerUnityUnit = presentationMetersPerUnityUnit;
            minimumStarDiameterInUnityUnits = minimumStarDiameterInUnits;
            minimumPlanetDiameterInUnityUnits = minimumPlanetDiameterInUnits;

            starLod1LightRadiusMultiplier = lod1LightRadiusMultiplier;
            starLod1LightIntensity = lod1LightIntensity;
            starLod1LightRayStrength = lod1LightRayStrength;
            starLod1LightRayCount = lod1LightRayCount;

            starLod2LocalSurfaceEnabled = enableStarLod2LocalSurface;
            starLod2SurfaceActivationDistanceInRadii =
                lod2StarSurfaceActivationDistanceInRadii;
            starLod2SurfaceDeactivationDistanceInRadii =
                lod2StarSurfaceDeactivationDistanceInRadii;

            planetLod2LocalSurfaceEnabled = enablePlanetLod2LocalSurface;
            planetLod2SurfaceActivationDistanceInRadii =
                lod2PlanetSurfaceActivationDistanceInRadii;
            planetLod2SurfaceDeactivationDistanceInRadii =
                lod2PlanetSurfaceDeactivationDistanceInRadii;

            ValidateLodSettings();
            EnsureVisualRoot();

            if (visualRoot != null)
            {
                SetLayerRecursively(
                    visualRoot.gameObject,
                    ReferenceFrameLayerUtility
                        .GetSingleLayerIndexOrDefault(scaledSpaceLayer));
            }
        }




        public void Tick(double simulationTimeSeconds)
        {
            if (!_isVisible || spaceAnchor == null ||
                !spaceAnchor.IsConfigured)
            {
                return;
            }

            // The shared physics implementation owns simulation time.
            _simulationTimeSeconds = simulationTimeSeconds;

            EnsureSystemData();
            UpdateBodyTransforms(immediate: false);
        }

        public void Dispose()
        {
            ClearVisuals();

            if (_runtimeFallbackMaterial != null)
                UnityEngine.Object.Destroy(_runtimeFallbackMaterial);

            if (_runtimeLod1LightQuadMesh != null)
                UnityEngine.Object.Destroy(_runtimeLod1LightQuadMesh);

            if (_runtimeCloseStarSurfaceMesh != null)
                UnityEngine.Object.Destroy(_runtimeCloseStarSurfaceMesh);

            if (_runtimeBlackHoleAccretionDiskMesh != null)
                UnityEngine.Object.Destroy(_runtimeBlackHoleAccretionDiskMesh);
        }

        public void SetScaledSpaceVisible(bool isVisible)
        {
            _isVisible = isVisible;
            EnsureVisualRoot();

            if (visualRoot != null)
                visualRoot.gameObject.SetActive(isVisible);

            if (isVisible)
                RefreshNow();
        }

        public void RefreshNow()
        {
            if (spaceAnchor == null || !spaceAnchor.IsConfigured)
                return;

            EnsureSystemData();

            if (_isVisible)
                UpdateBodyTransforms(immediate: true);
        }

        public bool TryGetNearestStar(
            out StarProximityData proximity)
        {
            proximity = default;

            if (spaceAnchor == null || !spaceAnchor.IsConfigured)
                return false;

            EnsureSystemData();

            if (_stars.Count == 0)
                return false;

            var nearestIndex = -1;
            var nearestDistance = double.PositiveInfinity;

            for (var i = 0; i < _stars.Count; i++)
            {
                var star = _stars[i];
                var starPosition = GetStarPositionMeters(star.Data);
                var relativePosition = starPosition -
                                       spaceAnchor
                                           .SolarSystemLocalPositionMeters;

                var distance = math.length(relativePosition);

                if (distance >= nearestDistance)
                    continue;

                nearestIndex = i;
                nearestDistance = distance;
            }

            if (nearestIndex < 0)
                return false;

            var nearestStar = _stars[nearestIndex];

            proximity = new StarProximityData(
                nearestIndex,
                nearestDistance,
                nearestDistance - nearestStar.Data.RadiusMeters,
                nearestStar.Data.RadiusMeters);

            return true;
        }

        /// <summary>
        /// Returns true once the scaled-star representation is visibly large
        /// enough to replace the distant Stellar LOD point without a gap.
        /// </summary>
        internal bool IsNearestStarLod1VisibleAt(
            float requiredAngularDiameterDegrees)
        {
            if (!_isVisible || requiredAngularDiameterDegrees <= 0.0f)
                return false;

            if (!TryGetNearestStar(out var proximity))
                return false;

            var nearestStar = _stars[proximity.StarIndex];

            var renderedRadius = GetStarLod1VisibilityRadiusMeters(
                nearestStar.Data,
                proximity.DistanceToCentreMeters);

            return GetAngularDiameterDegrees(
                       renderedRadius,
                       proximity.DistanceToCentreMeters) >=
                   requiredAngularDiameterDegrees;
        }

        private void EnsureSystemData()
        {
            var coordinates = spaceAnchor.Coordinates;
            var requestedSeed = coordinates.GetSolarSystemSeed();

            if (_loadedSystemSeed == requestedSeed && _stars.Count > 0)
                return;

            _solarSystem = SolarSystemGenerator.Generate(coordinates);
            _loadedSystemSeed = requestedSeed;

            RebuildVisuals();
        }

        private void RebuildVisuals()
        {
            ClearVisuals();
            _totalStarMassKg = 0.0;

            for (var i = 0; i < _solarSystem.Stars.Length; i++)
                _totalStarMassKg += _solarSystem.Stars[i].MassKg;

            for (var i = 0; i < _solarSystem.Stars.Length; i++)
            {
                var star = _solarSystem.Stars[i];
                _stars.Add(CreateStarVisual(star, i));

                ICelestialBodyLodRenderer lod2Renderer =
                    star.Type == StarType.BlackHole
                        ? new BlackHoleLocalLodRenderer(this, i)
                        : new StarLocalSurfaceLodRenderer(this, i);

                RegisterLod2Renderer(lod2Renderer);
                _ownedLod2Renderers.Add(lod2Renderer);
            }

            for (var i = 0; i < _solarSystem.Planets.Length; i++)
            {
                var planet = _solarSystem.Planets[i];
                _planets.Add(CreatePlanetVisual(planet, i));
            }
        }

        private StarVisual CreateStarVisual(StarData data, int index)
        {
            var body = CreateBodyTransform($"Star {index}");
            var renderer = body.gameObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            body.gameObject.AddComponent<MeshFilter>().sharedMesh =
                ResolveSphereMesh();

            var material = CreateMaterial(
                starMaterial,
                GetStarColor(data.Type),
                data.Type != StarType.BlackHole);

            renderer.sharedMaterial = material;

            var visual = new StarVisual
            {
                Data = data,
                Transform = body,
                Renderer = renderer,
                Material = material
            };

            if (data.Type == StarType.BlackHole &&
                data.HasAccretionDisk)
            {
                CreateBlackHoleProxyDiskVisual(visual, index);
            }

            if (coronaMaterial != null &&
                data.Type != StarType.BlackHole)
            {
                var corona = CreateBodyTransform($"Star {index} Corona");
                corona.SetParent(body, false);

                corona.gameObject.AddComponent<MeshFilter>().sharedMesh =
                    ResolveSphereMesh();

                var coronaRenderer =
                    corona.gameObject.AddComponent<MeshRenderer>();

                coronaRenderer.shadowCastingMode = ShadowCastingMode.Off;
                coronaRenderer.receiveShadows = false;

                var coronaInstance = CreateMaterial(
                    coronaMaterial,
                    GetStarColor(data.Type),
                    true);

                coronaRenderer.sharedMaterial = coronaInstance;
                visual.CoronaTransform = corona;
                visual.CoronaRenderer = coronaRenderer;
                visual.CoronaMaterial = coronaInstance;
            }

            CreateStarLod1LightVisual(visual, index);
            return visual;
        }

        private void CreateBlackHoleProxyDiskVisual(
            StarVisual visual,
            int starIndex)
        {
            if (visual.Transform == null)
                return;

            var layer = ReferenceFrameLayerUtility
                .GetSingleLayerIndexOrDefault(scaledSpaceLayer);

            var diskObject = new GameObject("Accretion Disk")
            {
                layer = layer
            };

            var diskTransform = diskObject.transform;
            diskTransform.SetParent(visual.Transform, false);
            diskTransform.localPosition = Vector3.zero;
            diskTransform.localRotation = GetBlackHoleDiskRotation(starIndex);
            diskTransform.localScale = Vector3.one;

            diskObject.AddComponent<MeshFilter>().sharedMesh =
                ResolveBlackHoleAccretionDiskMesh();

            var diskRenderer = diskObject.AddComponent<MeshRenderer>();
            diskRenderer.shadowCastingMode = ShadowCastingMode.Off;
            diskRenderer.receiveShadows = false;

            var diskMaterial = CreateBlackHoleAccretionDiskMaterial(
                visual.Data,
                starIndex,
                detailed: false);

            diskRenderer.sharedMaterial = diskMaterial;
            visual.AccretionDiskTransform = diskTransform;
            visual.AccretionDiskRenderer = diskRenderer;
            visual.AccretionDiskMaterial = diskMaterial;
        }

        private void UpdateBlackHoleProxyVisual(
            StarVisual visual,
            int starIndex,
            double distanceToCentreMeters)
        {
            UpdateBlackHoleProxyDiskScale(
                visual,
                distanceToCentreMeters);

            if (visual.AccretionDiskMaterial == null)
                return;

            UpdateBlackHoleAccretionDiskMaterial(
                visual.AccretionDiskMaterial,
                visual.Data,
                starIndex,
                detailed: false);
        }

        private void CreateStarLod1LightVisual(
            StarVisual visual,
            int starIndex)
        {
            if (visual.Data.Type == StarType.BlackHole ||
                visual.Transform == null)
            {
                return;
            }

            var layer = ReferenceFrameLayerUtility
                .GetSingleLayerIndexOrDefault(scaledSpaceLayer);

            var lightObject = new GameObject($"Star {starIndex} LOD 1 Light")
            {
                layer = layer
            };

            var lightTransform = lightObject.transform;
            lightTransform.SetParent(visual.Transform, false);
            lightTransform.localPosition = Vector3.zero;
            lightTransform.localRotation = Quaternion.identity;
            lightTransform.localScale = Vector3.one;

            lightObject.AddComponent<MeshFilter>().sharedMesh =
                ResolveLod1LightQuadMesh();

            var lightRenderer = lightObject.AddComponent<MeshRenderer>();
            lightRenderer.shadowCastingMode = ShadowCastingMode.Off;
            lightRenderer.receiveShadows = false;

            var lightMaterial = CreateStarLod1LightMaterial(
                visual.Data,
                starIndex);

            lightRenderer.sharedMaterial = lightMaterial;

            visual.Lod1LightTransform = lightTransform;
            visual.Lod1LightRenderer = lightRenderer;
            visual.Lod1LightMaterial = lightMaterial;
        }

        private PlanetVisual CreatePlanetVisual(PlanetData data, int index)
        {
            var body = CreateBodyTransform($"Planet {index}");
            var renderer = body.gameObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            body.gameObject.AddComponent<MeshFilter>().sharedMesh =
                ResolveSphereMesh();

            var material = CreateMaterial(
                planetMaterial,
                GetPlanetColor(data.Type),
                false);

            renderer.sharedMaterial = material;

            return new PlanetVisual
            {
                Data = data,
                Transform = body,
                Material = material
            };
        }

        private Transform CreateBodyTransform(string bodyName)
        {
            EnsureVisualRoot();

            var bodyObject = new GameObject(bodyName)
            {
                layer = ReferenceFrameLayerUtility
                    .GetSingleLayerIndexOrDefault(scaledSpaceLayer)
            };

            bodyObject.transform.SetParent(visualRoot, false);
            return bodyObject.transform;
        }

        private void UpdateBodyTransforms(bool immediate)
        {
            if (_totalStarMassKg <= 0.0)
                return;

            var gravitationalParameter =
                SolarSystemOrbitUtility.GravitationalConstant *
                _totalStarMassKg;

            var nearestStarIndex = -1;
            var nearestStarDistanceMeters = double.PositiveInfinity;

            // LOD 1 keeps true scaled-space positions. Stars render their
            // bright HDR glare here; LOD 2 is a separate fixed-scale mesh.
            for (var i = 0; i < _stars.Count; i++)
            {
                var star = _stars[i];
                var barycentricPosition = GetStarPositionMeters(star.Data);
                star.BarycentricPositionMeters = barycentricPosition;

                var relativeMeters = barycentricPosition -
                                     spaceAnchor
                                         .SolarSystemLocalPositionMeters;

                var distanceToCentre = math.length(relativeMeters);
                var renderRadius = GetStarRenderRadiusMeters(
                    star.Data,
                    distanceToCentre);

                ApplyTransform(
                    star.Transform,
                    relativeMeters,
                    renderRadius);

                if (star.CoronaTransform != null)
                {
                    star.CoronaTransform.localPosition = Vector3.zero;
                    star.CoronaTransform.localRotation = Quaternion.identity;
                    star.CoronaTransform.localScale =
                        Vector3.one * coronaRadiusMultiplier;
                }

                if (star.Data.Type == StarType.BlackHole)
                {
                    UpdateBlackHoleProxyVisual(
                        star,
                        i,
                        distanceToCentre);
                }

                if (distanceToCentre < nearestStarDistanceMeters)
                {
                    nearestStarIndex = i;
                    nearestStarDistanceMeters = distanceToCentre;
                }
            }

            for (var i = 0; i < _planets.Count; i++)
            {
                var planet = _planets[i];
                var barycentricPosition =
                    SolarSystemOrbitUtility.GetPositionMeters(
                        planet.Data.Orbit,
                        gravitationalParameter,
                        _simulationTimeSeconds);

                planet.BarycentricPositionMeters = barycentricPosition;

                var relativeMeters = barycentricPosition -
                                     spaceAnchor
                                         .SolarSystemLocalPositionMeters;

                var distanceToCentre = math.length(relativeMeters);
                var renderRadius = GetPlanetRenderRadiusMeters(
                    planet.Data.RadiusMeters,
                    distanceToCentre);

                ApplyTransform(
                    planet.Transform,
                    relativeMeters,
                    renderRadius);
            }

            var nearStarSurface = UpdateNearestStarLod2(
                nearestStarIndex,
                immediate);

            UpdateRegisteredPlanetLod2s(immediate);
            UpdateStarLod1Lights();

            if (_isNearStarSurface != nearStarSurface)
            {
                _isNearStarSurface = nearStarSurface;
                StarSurfaceLodChanged?.Invoke(_isNearStarSurface);
            }
        }

        private double3 GetStarPositionMeters(StarData data)
        {
            return SolarSystemOrbitUtility.GetPositionMeters(
                data.BarycentricOrbit,
                SolarSystemOrbitUtility.GravitationalConstant *
                _totalStarMassKg,
                _simulationTimeSeconds);
        }

        private double GetStarRenderRadiusMeters(
            StarData star,
            double distanceToCentreMeters)
        {
            if (star.Type == StarType.BlackHole)
            {
                return GetBlackHoleProxyRenderRadiusMeters(
                    star.RadiusMeters);
            }

            var presentationRadius = GetStarPresentationRadiusMeters(
                star.RadiusMeters);

            var angularRadius = GetMinimumAngularRadiusMeters(
                distanceToCentreMeters,
                minimumStarAngularDiameterDegrees);

            return Math.Max(presentationRadius, angularRadius);
        }

        /// <summary>
        /// Returns the LOD 1 silhouette radius that participates in the
        /// LOD 0-to-LOD 1 handoff. An accretion disk may be visible long
        /// before the physical event horizon, so the handoff must consider
        /// the disk's real outer edge rather than only the tiny horizon.
        /// </summary>
        private double GetStarLod1VisibilityRadiusMeters(
            StarData star,
            double distanceToCentreMeters)
        {
            if (star.Type != StarType.BlackHole)
            {
                return GetStarRenderRadiusMeters(
                    star,
                    distanceToCentreMeters);
            }

            if (!star.HasAccretionDisk)
            {
                return GetBlackHoleProxyRenderRadiusMeters(
                    star.RadiusMeters);
            }

            return GetBlackHoleLod1DiskOuterRadiusMeters(
                star.RadiusMeters,
                distanceToCentreMeters);
        }

        /// <summary>
        /// Keeps the LOD 1 event horizon at its physical Schwarzschild radius.
        /// The distant visibility floor is applied only to the accretion disk,
        /// whose full outer silhouette can then shrink continuously into the
        /// same LOD 0 point used by ordinary stars.
        /// </summary>
        private static double GetBlackHoleProxyRenderRadiusMeters(
            double physicalRadiusMeters)
        {
            return Math.Max(0.0, physicalRadiusMeters);
        }

        private double GetBlackHoleLod1DiskOuterRadiusMeters(
            double physicalHorizonRadiusMeters,
            double distanceToCentreMeters)
        {
            var physicalDiskOuterRadius =
                GetBlackHolePhysicalDiskOuterRadiusMeters(
                    physicalHorizonRadiusMeters);

            var minimumVisibleDiskOuterRadius =
                GetMinimumAngularRadiusMeters(
                    distanceToCentreMeters,
                    minimumStarAngularDiameterDegrees);

            return Math.Max(
                physicalDiskOuterRadius,
                minimumVisibleDiskOuterRadius);
        }

        private static double GetBlackHolePhysicalDiskOuterRadiusMeters(
            double physicalHorizonRadiusMeters)
        {
            // The root transform represents a sphere with a local radius of
            // 0.5, while the disk mesh is authored in root-local units.
            return Math.Max(
                0.0,
                physicalHorizonRadiusMeters *
                2.0 *
                BlackHoleAccretionDiskOuterMeshRadius);
        }

        private void UpdateBlackHoleProxyDiskScale(
            StarVisual visual,
            double distanceToCentreMeters)
        {
            if (visual == null ||
                visual.AccretionDiskTransform == null)
            {
                return;
            }

            var physicalDiskOuterRadius =
                GetBlackHolePhysicalDiskOuterRadiusMeters(
                    visual.Data.RadiusMeters);

            if (physicalDiskOuterRadius <= 0.0)
            {
                visual.AccretionDiskTransform.localScale = Vector3.one;
                return;
            }

            var targetDiskOuterRadius =
                GetBlackHoleLod1DiskOuterRadiusMeters(
                    visual.Data.RadiusMeters,
                    distanceToCentreMeters);

            var scale = (float)Math.Max(
                1.0,
                targetDiskOuterRadius / physicalDiskOuterRadius);

            visual.AccretionDiskTransform.localScale =
                Vector3.one * scale;
        }

        private double GetPlanetRenderRadiusMeters(
            double physicalRadiusMeters,
            double distanceToCentreMeters)
        {
            var presentationRadius = GetPlanetPresentationRadiusMeters(
                physicalRadiusMeters);

            if (minimumPlanetAngularDiameterDegrees <= 0.0f)
                return presentationRadius;

            var angularRadius = GetMinimumAngularRadiusMeters(
                distanceToCentreMeters,
                minimumPlanetAngularDiameterDegrees);

            return Math.Max(presentationRadius, angularRadius);
        }

        private double GetStarPresentationRadiusMeters(
            double physicalRadiusMeters)
        {
            return Math.Max(
                physicalRadiusMeters,
                GetMinimumPresentationRadiusMeters(
                    minimumStarDiameterInUnityUnits));
        }

        private double GetPlanetPresentationRadiusMeters(
            double physicalRadiusMeters)
        {
            return Math.Max(
                physicalRadiusMeters,
                GetMinimumPresentationRadiusMeters(
                    minimumPlanetDiameterInUnityUnits));
        }

        private double GetMinimumPresentationRadiusMeters(
            float minimumDiameterInUnityUnits)
        {
            return scaledSpaceMetersPerUnityUnit *
                   Mathf.Max(1.0f, minimumDiameterInUnityUnits) *
                   0.5;
        }

        private bool UpdateNearestStarLod2(
            int nearestStarIndex,
            bool immediate)
        {
            if (nearestStarIndex < 0 ||
                nearestStarIndex >= _stars.Count)
            {
                return false;
            }

            var key = new CelestialBodyRenderKey(
                CelestialBodyKind.Star,
                nearestStarIndex);

            if (!_lod2Renderers.TryGetValue(key, out var lodRenderer))
                return false;

            var star = _stars[nearestStarIndex];
            var physicalRelativeMeters =
                star.BarycentricPositionMeters -
                spaceAnchor.SolarSystemLocalPositionMeters;

            var distanceToCentreMeters = math.length(
                physicalRelativeMeters);

            var distanceInRadii = star.Data.RadiusMeters > 0.0
                ? distanceToCentreMeters / star.Data.RadiusMeters
                : double.PositiveInfinity;

            var shouldBeActive =
                starLod2LocalSurfaceEnabled &&
                star.Data.RadiusMeters > 0.0 &&
                IsWithinLodRange(
                    lodRenderer.IsActive,
                    distanceInRadii,
                    starLod2SurfaceActivationDistanceInRadii,
                    starLod2SurfaceDeactivationDistanceInRadii);

            if (!shouldBeActive)
            {
                lodRenderer.SetLodActive(false);
                SetBaseStarVisualVisible(star, true);
                return false;
            }

            var context = new CelestialBodyLodContext(
                key,
                visualRoot,
                physicalRelativeMeters,
                star.Data.RadiusMeters,
                star.Data.Type == StarType.BlackHole
                    ? star.Data.RadiusMeters
                    : GetStarPresentationRadiusMeters(
                        star.Data.RadiusMeters),
                distanceToCentreMeters,
                distanceInRadii,
                scaledSpaceMetersPerUnityUnit,
                (float)_simulationTimeSeconds,
                immediate);

            lodRenderer.SetLodActive(true);
            lodRenderer.UpdateLod(context);
            SetBaseStarVisualVisible(star, false);

            return lodRenderer.IsActive;
        }

        private void UpdateRegisteredPlanetLod2s(bool immediate)
        {
            for (var index = 0; index < _planets.Count; index++)
            {
                var key = new CelestialBodyRenderKey(
                    CelestialBodyKind.Planet,
                    index);

                if (!_lod2Renderers.TryGetValue(key, out var lodRenderer))
                    continue;

                var planet = _planets[index];
                var physicalRelativeMeters =
                    planet.BarycentricPositionMeters -
                    spaceAnchor.SolarSystemLocalPositionMeters;

                var distanceToCentreMeters = math.length(
                    physicalRelativeMeters);

                var distanceInRadii = planet.Data.RadiusMeters > 0.0
                    ? distanceToCentreMeters / planet.Data.RadiusMeters
                    : double.PositiveInfinity;

                var shouldBeActive =
                    planetLod2LocalSurfaceEnabled &&
                    planet.Data.RadiusMeters > 0.0 &&
                    IsWithinLodRange(
                        lodRenderer.IsActive,
                        distanceInRadii,
                        planetLod2SurfaceActivationDistanceInRadii,
                        planetLod2SurfaceDeactivationDistanceInRadii);

                if (!shouldBeActive)
                {
                    lodRenderer.SetLodActive(false);
                    SetBasePlanetVisualVisible(planet, true);
                    continue;
                }

                var context = new CelestialBodyLodContext(
                    key,
                    visualRoot,
                    physicalRelativeMeters,
                    planet.Data.RadiusMeters,
                    GetPlanetPresentationRadiusMeters(
                        planet.Data.RadiusMeters),
                    distanceToCentreMeters,
                    distanceInRadii,
                    scaledSpaceMetersPerUnityUnit,
                    (float)_simulationTimeSeconds,
                    immediate);

                lodRenderer.SetLodActive(true);
                lodRenderer.UpdateLod(context);
                SetBasePlanetVisualVisible(planet, false);
            }
        }

        private void UpdateStarLod1Lights()
        {
            for (var index = 0; index < _stars.Count; index++)
            {
                var star = _stars[index];
                var physicalRelativeMeters =
                    star.BarycentricPositionMeters -
                    spaceAnchor.SolarSystemLocalPositionMeters;

                var key = new CelestialBodyRenderKey(
                    CelestialBodyKind.Star,
                    index);

                var isLod2Active =
                    _lod2Renderers.TryGetValue(key, out var lodRenderer) &&
                    lodRenderer.IsActive;

                UpdateStarLod1Light(
                    star,
                    physicalRelativeMeters,
                    isLod2Active);
            }
        }

        private void UpdateStarLod1Light(
            StarVisual star,
            double3 physicalRelativeMeters,
            bool isLod2Active)
        {
            if (star.Lod1LightTransform == null ||
                star.Lod1LightRenderer == null ||
                star.Data.Type == StarType.BlackHole)
            {
                return;
            }

            // The rays, halo and corona belong to LOD 1. Once the detailed
            // LOD 2 surface is active, it owns the near-star presentation.
            if (isLod2Active)
            {
                star.Lod1LightRenderer.enabled = false;
                star.Lod1LightTransform.gameObject.SetActive(false);
                return;
            }

            star.Lod1LightTransform.gameObject.SetActive(true);
            star.Lod1LightRenderer.enabled = true;
            star.Lod1LightTransform.localPosition = Vector3.zero;
            star.Lod1LightTransform.localScale = Vector3.one *
                                                 starLod1LightRadiusMultiplier;
            star.Lod1LightTransform.localRotation =
                GetCameraFacingRotation(physicalRelativeMeters);

            if (star.Lod1LightMaterial == null)
                return;

            SetColorIfPresent(
                star.Lod1LightMaterial,
                "_BaseColor",
                GetStarColor(star.Data.Type));

            SetFloatIfPresent(
                star.Lod1LightMaterial,
                "_Intensity",
                starLod1LightIntensity);

            SetFloatIfPresent(
                star.Lod1LightMaterial,
                "_Opacity",
                1.0f);

            SetFloatIfPresent(
                star.Lod1LightMaterial,
                "_RayStrength",
                starLod1LightRayStrength);

            SetFloatIfPresent(
                star.Lod1LightMaterial,
                "_RayCount",
                starLod1LightRayCount);

            SetFloatIfPresent(
                star.Lod1LightMaterial,
                "_DiscRadius",
                1.0f / Mathf.Max(
                    1.0f,
                    starLod1LightRadiusMultiplier));

            SetFloatIfPresent(
                star.Lod1LightMaterial,
                "_SurfaceTime",
                (float)_simulationTimeSeconds);
        }

        private static bool IsWithinLodRange(
            bool isAlreadyActive,
            double distanceInRadii,
            double activationDistanceInRadii,
            double deactivationDistanceInRadii)
        {
            var limit = isAlreadyActive
                ? deactivationDistanceInRadii
                : activationDistanceInRadii;

            return distanceInRadii <= limit;
        }

        private static Quaternion GetCameraFacingRotation(
            double3 relativeMeters)
        {
            var directionToCamera = new Vector3(
                (float)-relativeMeters.x,
                (float)-relativeMeters.y,
                (float)-relativeMeters.z);

            if (directionToCamera.sqrMagnitude <= 0.000001f)
                return Quaternion.identity;

            directionToCamera.Normalize();
            var up = Mathf.Abs(Vector3.Dot(directionToCamera, Vector3.up)) >
                     0.99f
                ? Vector3.right
                : Vector3.up;

            return Quaternion.LookRotation(directionToCamera, up);
        }

        private void ValidateLodSettings()
        {
            minimumStarAngularDiameterDegrees = Mathf.Max(
                0.001f,
                minimumStarAngularDiameterDegrees);

            minimumPlanetAngularDiameterDegrees = Mathf.Max(
                0.0f,
                minimumPlanetAngularDiameterDegrees);

            scaledSpaceMetersPerUnityUnit = Math.Max(
                1.0,
                scaledSpaceMetersPerUnityUnit);

            minimumPlanetDiameterInUnityUnits = Mathf.Max(
                1.0f,
                minimumPlanetDiameterInUnityUnits);

            minimumStarDiameterInUnityUnits = Mathf.Max(
                minimumPlanetDiameterInUnityUnits,
                minimumStarDiameterInUnityUnits);

            coronaRadiusMultiplier = Mathf.Max(
                1.01f,
                coronaRadiusMultiplier);


            starLod1LightRadiusMultiplier = Mathf.Clamp(
                starLod1LightRadiusMultiplier,
                1.0f,
                64.0f);

            starLod1LightIntensity = Mathf.Clamp(
                starLod1LightIntensity,
                0.0f,
                32.0f);

            starLod1LightRayStrength = Mathf.Clamp(
                starLod1LightRayStrength,
                0.0f,
                3.0f);

            starLod1LightRayCount = Mathf.Clamp(
                starLod1LightRayCount,
                4.0f,
                32.0f);

            starLod2SurfaceActivationDistanceInRadii = Math.Max(
                1.0,
                starLod2SurfaceActivationDistanceInRadii);

            starLod2SurfaceDeactivationDistanceInRadii = Math.Max(
                starLod2SurfaceActivationDistanceInRadii,
                starLod2SurfaceDeactivationDistanceInRadii);


            planetLod2SurfaceActivationDistanceInRadii = Math.Max(
                1.0,
                planetLod2SurfaceActivationDistanceInRadii);

            planetLod2SurfaceDeactivationDistanceInRadii = Math.Max(
                planetLod2SurfaceActivationDistanceInRadii,
                planetLod2SurfaceDeactivationDistanceInRadii);

        }

        private static double GetMinimumAngularRadiusMeters(
            double distanceToCentreMeters,
            float minimumAngularDiameterDegrees)
        {
            if (distanceToCentreMeters <= 0.0 ||
                minimumAngularDiameterDegrees <= 0.0f)
            {
                return 0.0;
            }

            var halfAngleRadians =
                minimumAngularDiameterDegrees *
                Math.PI / 360.0;

            return distanceToCentreMeters *
                   Math.Tan(halfAngleRadians);
        }

        private static float GetAngularDiameterDegrees(
            double radiusMeters,
            double distanceToCentreMeters)
        {
            if (radiusMeters <= 0.0 || distanceToCentreMeters <= 0.0)
                return 180.0f;

            if (distanceToCentreMeters <= radiusMeters)
                return 180.0f;

            // A sphere's visible silhouette uses asin(radius / distance),
            // rather than the small-angle atan approximation.
            var ratio = Math.Min(
                1.0,
                radiusMeters / distanceToCentreMeters);

            return (float)(
                2.0 * Math.Asin(ratio) * 180.0 / Math.PI);
        }

        private void ApplyTransform(
            Transform body,
            double3 relativeMeters,
            double radiusMeters)
        {
            body.localPosition = new Vector3(
                (float)(relativeMeters.x /
                        scaledSpaceMetersPerUnityUnit),
                (float)(relativeMeters.y /
                        scaledSpaceMetersPerUnityUnit),
                (float)(relativeMeters.z /
                        scaledSpaceMetersPerUnityUnit));

            var diameterInUnityUnits =
                (float)(radiusMeters * 2.0 /
                        scaledSpaceMetersPerUnityUnit);

            body.localScale = Vector3.one *
                              Mathf.Max(0.000001f,
                                        diameterInUnityUnits);
        }


        private bool IsStarLocalSurfaceActive(int starIndex)
        {
            return _closeStarSurfaceVisual != null &&
                   _closeStarSurfaceVisual.StarIndex == starIndex &&
                   _closeStarSurfaceVisual.Root != null &&
                   _closeStarSurfaceVisual.Root.gameObject.activeSelf;
        }

        private void SetStarLocalSurfaceActive(
            int starIndex,
            bool isActive)
        {
            if (starIndex < 0 || starIndex >= _stars.Count)
                return;

            if (!isActive)
            {
                if (_closeStarSurfaceVisual != null &&
                    _closeStarSurfaceVisual.StarIndex == starIndex)
                {
                    DeactivateCloseStarSurfaceLod(destroyVisual: false);
                }

                return;
            }

            EnsureCloseStarSurfaceVisual(starIndex, _stars[starIndex]);
        }

        private void UpdateStarLocalSurfaceVisual(
            int starIndex,
            in CelestialBodyLodContext context)
        {
            if (starIndex < 0 || starIndex >= _stars.Count)
                return;

            EnsureCloseStarSurfaceVisual(starIndex, _stars[starIndex]);

            if (_closeStarSurfaceVisual == null ||
                _closeStarSurfaceVisual.StarIndex != starIndex ||
                _closeStarSurfaceVisual.Root == null)
            {
                return;
            }

            CelestialBodyLodTransformUtility.ApplyPhysicalTransform(
                _closeStarSurfaceVisual.Root,
                context);

            UpdateCloseStarSurfaceMaterials(_stars[starIndex].Data, starIndex);
        }

        private void DestroyStarLocalSurfaceVisual(int starIndex)
        {
            if (_closeStarSurfaceVisual != null &&
                _closeStarSurfaceVisual.StarIndex == starIndex)
            {
                DeactivateCloseStarSurfaceLod(destroyVisual: true);
            }
        }

        private void EnsureCloseStarSurfaceVisual(
            int starIndex,
            StarVisual star)
        {
            if (_closeStarSurfaceVisual != null &&
                _closeStarSurfaceVisual.StarIndex == starIndex &&
                _closeStarSurfaceVisual.Root != null)
            {
                if (_closeStarSurfaceVisual.Root.parent != visualRoot)
                {
                    _closeStarSurfaceVisual.Root.SetParent(
                        visualRoot,
                        worldPositionStays: false);
                }

                _closeStarSurfaceVisual.Root.gameObject.SetActive(true);
                SetBaseStarVisualVisible(star, false);
                return;
            }

            DeactivateCloseStarSurfaceLod(destroyVisual: true);

            var layer = ReferenceFrameLayerUtility
                .GetSingleLayerIndexOrDefault(scaledSpaceLayer);

            var rootObject = new GameObject("Star LOD 2 Local Surface")
            {
                layer = layer
            };

            var root = rootObject.transform;
            root.SetParent(visualRoot, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            var surfaceFilter = rootObject.AddComponent<MeshFilter>();
            surfaceFilter.sharedMesh = ResolveCloseStarSurfaceMesh();

            var surfaceRenderer = rootObject.AddComponent<MeshRenderer>();
            surfaceRenderer.shadowCastingMode = ShadowCastingMode.Off;
            surfaceRenderer.receiveShadows = false;

            var visual = new CloseStarSurfaceVisual
            {
                StarIndex = starIndex,
                Root = root,
                SurfaceRenderer = surfaceRenderer,
                SurfaceMaterial = CreateCloseSurfaceMaterial(
                    star.Data,
                    starIndex)
            };

            surfaceRenderer.sharedMaterial = visual.SurfaceMaterial;

            var coronaObject = new GameObject("Dynamic Corona")
            {
                layer = layer
            };

            coronaObject.transform.SetParent(root, false);
            coronaObject.transform.localPosition = Vector3.zero;
            coronaObject.transform.localRotation = Quaternion.identity;
            coronaObject.transform.localScale =
                Vector3.one * Mathf.Max(
                    coronaRadiusMultiplier,
                    1.035f);

            coronaObject.AddComponent<MeshFilter>().sharedMesh =
                ResolveCloseStarSurfaceMesh();

            var coronaRenderer =
                coronaObject.AddComponent<MeshRenderer>();

            coronaRenderer.shadowCastingMode = ShadowCastingMode.Off;
            coronaRenderer.receiveShadows = false;

            visual.CoronaTransform = coronaObject.transform;
            visual.CoronaRenderer = coronaRenderer;
            visual.CoronaMaterial = CreateCloseCoronaMaterial(
                star.Data,
                starIndex);

            coronaRenderer.sharedMaterial = visual.CoronaMaterial;

            CreateProminences(visual, star.Data, starIndex, layer);
            _closeStarSurfaceVisual = visual;

            SetBaseStarVisualVisible(star, false);
        }

        private void UpdateCloseStarSurfaceMaterials(
            StarData star,
            int starIndex)
        {
            if (_closeStarSurfaceVisual == null ||
                _closeStarSurfaceVisual.StarIndex != starIndex)
            {
                return;
            }

            var colors = GetCloseSurfaceColors(star.Type);
            var time = (float)_simulationTimeSeconds;

            if (_closeStarSurfaceVisual.SurfaceMaterial != null)
            {
                SetColorIfPresent(
                    _closeStarSurfaceVisual.SurfaceMaterial,
                    "_BaseColor",
                    colors.Base);

                SetColorIfPresent(
                    _closeStarSurfaceVisual.SurfaceMaterial,
                    "_SurfaceColor",
                    colors.Surface);

                SetColorIfPresent(
                    _closeStarSurfaceVisual.SurfaceMaterial,
                    "_HotColor",
                    colors.Hot);

                SetColorIfPresent(
                    _closeStarSurfaceVisual.SurfaceMaterial,
                    "_SpotColor",
                    colors.Spot);

                SetFloatIfPresent(
                    _closeStarSurfaceVisual.SurfaceMaterial,
                    "_SurfaceTime",
                    time);
            }

            if (_closeStarSurfaceVisual.CoronaMaterial != null)
            {
                SetColorIfPresent(
                    _closeStarSurfaceVisual.CoronaMaterial,
                    "_BaseColor",
                    colors.Hot);

                SetFloatIfPresent(
                    _closeStarSurfaceVisual.CoronaMaterial,
                    "_SurfaceTime",
                    time);
            }

            if (_closeStarSurfaceVisual.ProminenceMaterial != null)
            {
                SetColorIfPresent(
                    _closeStarSurfaceVisual.ProminenceMaterial,
                    "_BaseColor",
                    colors.Hot);

                SetFloatIfPresent(
                    _closeStarSurfaceVisual.ProminenceMaterial,
                    "_SurfaceTime",
                    time);
            }
        }

        private bool IsBlackHoleLocalVisualActive(int starIndex)
        {
            return _closeBlackHoleVisual != null &&
                   _closeBlackHoleVisual.StarIndex == starIndex &&
                   _closeBlackHoleVisual.Root != null &&
                   _closeBlackHoleVisual.Root.gameObject.activeSelf;
        }

        private void SetBlackHoleLocalVisualActive(
            int starIndex,
            bool isActive)
        {
            if (starIndex < 0 || starIndex >= _stars.Count)
                return;

            if (!isActive)
            {
                if (_closeBlackHoleVisual != null &&
                    _closeBlackHoleVisual.StarIndex == starIndex)
                {
                    DeactivateCloseBlackHoleLod(destroyVisual: false);
                }

                return;
            }

            EnsureCloseBlackHoleVisual(starIndex, _stars[starIndex]);
        }

        private void UpdateBlackHoleLocalVisual(
            int starIndex,
            in CelestialBodyLodContext context)
        {
            if (starIndex < 0 || starIndex >= _stars.Count)
                return;

            var star = _stars[starIndex];

            if (star.Data.Type != StarType.BlackHole)
                return;

            EnsureCloseBlackHoleVisual(starIndex, star);

            if (_closeBlackHoleVisual == null ||
                _closeBlackHoleVisual.StarIndex != starIndex ||
                _closeBlackHoleVisual.Root == null)
            {
                return;
            }

            // Unlike a normal star's close LOD, a black hole must preserve its
            // generated horizon radius. It is already large enough in angular
            // terms by the time this local LOD activates.
            CelestialBodyLodTransformUtility.ApplyPhysicalTransform(
                _closeBlackHoleVisual.Root,
                context);

            UpdateCloseBlackHoleMaterials(
                _closeBlackHoleVisual,
                star.Data,
                starIndex);
        }

        private void DestroyBlackHoleLocalVisual(int starIndex)
        {
            if (_closeBlackHoleVisual != null &&
                _closeBlackHoleVisual.StarIndex == starIndex)
            {
                DeactivateCloseBlackHoleLod(destroyVisual: true);
            }
        }

        private void EnsureCloseBlackHoleVisual(
            int starIndex,
            StarVisual star)
        {
            if (_closeBlackHoleVisual != null &&
                _closeBlackHoleVisual.StarIndex == starIndex &&
                _closeBlackHoleVisual.Root != null)
            {
                if (_closeBlackHoleVisual.Root.parent != visualRoot)
                {
                    _closeBlackHoleVisual.Root.SetParent(
                        visualRoot,
                        worldPositionStays: false);
                }

                _closeBlackHoleVisual.Root.gameObject.SetActive(true);
                SetBaseStarVisualVisible(star, false);
                return;
            }

            DeactivateCloseBlackHoleLod(destroyVisual: true);

            var layer = ReferenceFrameLayerUtility
                .GetSingleLayerIndexOrDefault(scaledSpaceLayer);

            var rootObject = new GameObject("Black Hole LOD 2")
            {
                layer = layer
            };

            var root = rootObject.transform;
            root.SetParent(visualRoot, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            rootObject.AddComponent<MeshFilter>().sharedMesh =
                ResolveCloseStarSurfaceMesh();

            var horizonRenderer = rootObject.AddComponent<MeshRenderer>();
            horizonRenderer.shadowCastingMode = ShadowCastingMode.Off;
            horizonRenderer.receiveShadows = false;

            var visual = new CloseBlackHoleVisual
            {
                StarIndex = starIndex,
                Root = root,
                HorizonRenderer = horizonRenderer,
                HorizonMaterial = CreateBlackHoleHorizonMaterial(
                    star.Data,
                    starIndex)
            };

            horizonRenderer.sharedMaterial = visual.HorizonMaterial;

            var lensingObject = new GameObject("Gravitational Lensing Shell")
            {
                layer = layer
            };

            var lensingTransform = lensingObject.transform;
            lensingTransform.SetParent(root, false);
            lensingTransform.localPosition = Vector3.zero;
            lensingTransform.localRotation = Quaternion.identity;
            lensingTransform.localScale = Vector3.one * 12.0f;

            lensingObject.AddComponent<MeshFilter>().sharedMesh =
                ResolveCloseStarSurfaceMesh();

            var lensingRenderer = lensingObject.AddComponent<MeshRenderer>();
            lensingRenderer.shadowCastingMode = ShadowCastingMode.Off;
            lensingRenderer.receiveShadows = false;

            visual.LensingShellTransform = lensingTransform;
            visual.LensingShellRenderer = lensingRenderer;
            visual.LensingMaterial = CreateBlackHoleLensingMaterial(starIndex);
            lensingRenderer.sharedMaterial = visual.LensingMaterial;
            lensingObject.SetActive(visual.LensingMaterial != null);

            if (star.Data.HasAccretionDisk)
            {
                var diskObject = new GameObject("Accretion Disk")
                {
                    layer = layer
                };

                var diskTransform = diskObject.transform;
                diskTransform.SetParent(root, false);
                diskTransform.localPosition = Vector3.zero;
                diskTransform.localRotation = GetBlackHoleDiskRotation(starIndex);
                diskTransform.localScale = Vector3.one;

                diskObject.AddComponent<MeshFilter>().sharedMesh =
                    ResolveBlackHoleAccretionDiskMesh();

                var diskRenderer = diskObject.AddComponent<MeshRenderer>();
                diskRenderer.shadowCastingMode = ShadowCastingMode.Off;
                diskRenderer.receiveShadows = false;

                visual.AccretionDiskTransform = diskTransform;
                visual.AccretionDiskRenderer = diskRenderer;
                visual.AccretionDiskMaterial =
                    CreateBlackHoleAccretionDiskMaterial(
                        star.Data,
                        starIndex,
                        detailed: true);

                diskRenderer.sharedMaterial = visual.AccretionDiskMaterial;
            }

            _closeBlackHoleVisual = visual;
            UpdateCloseBlackHoleMaterials(visual, star.Data, starIndex);
            SetBaseStarVisualVisible(star, false);
        }

        private void UpdateCloseBlackHoleMaterials(
            CloseBlackHoleVisual visual,
            StarData star,
            int starIndex)
        {
            if (visual == null)
                return;

            var hasDisk = star.HasAccretionDisk;
            var time = (float)_simulationTimeSeconds;
            var photonColor = hasDisk
                ? new Color(1.0f, 0.30f, 0.055f, 1.0f)
                : new Color(0.07f, 0.12f, 0.20f, 1.0f);

            if (visual.HorizonMaterial != null)
            {
                SetColorIfPresent(
                    visual.HorizonMaterial,
                    "_PhotonRingColor",
                    photonColor);
                SetFloatIfPresent(
                    visual.HorizonMaterial,
                    "_PhotonRingIntensity",
                    hasDisk ? 0.72f : 0.08f);
            }

            if (visual.LensingMaterial != null)
            {
                SetFloatIfPresent(
                    visual.LensingMaterial,
                    "_SurfaceTime",
                    time);
                SetFloatIfPresent(
                    visual.LensingMaterial,
                    "_LensingStrength",
                    hasDisk ? 0.86f : 0.74f);
                SetFloatIfPresent(
                    visual.LensingMaterial,
                    "_SceneColorAvailable",
                    Shader.GetGlobalTexture("_CameraOpaqueTexture") != null
                        ? 1.0f
                        : 0.0f);

                UpdateBlackHoleLensingScreenData(visual);
            }

            UpdateBlackHoleAccretionDiskMaterial(
                visual.AccretionDiskMaterial,
                star,
                starIndex,
                detailed: true);
        }

        private void UpdateBlackHoleLensingScreenData(
            CloseBlackHoleVisual visual)
        {
            if (visual == null ||
                visual.Root == null ||
                visual.LensingShellTransform == null ||
                visual.LensingMaterial == null)
            {
                return;
            }

            var camera = celestialCamera != null
                ? celestialCamera
                : Camera.main;

            if (camera == null)
                return;

            var centre = camera.WorldToViewportPoint(visual.Root.position);

            if (centre.z <= 0.0f)
                return;

            var horizonWorldRadius = visual.Root.lossyScale.x * 0.5f;
            var lensingRadius = horizonWorldRadius *
                                Mathf.Abs(
                                    visual.LensingShellTransform
                                        .localScale.x);

            var edge = camera.WorldToViewportPoint(
                visual.Root.position +
                camera.transform.right * lensingRadius);

            var viewportRadius = Mathf.Abs(edge.x - centre.x);

            if (viewportRadius <= 0.00001f)
            {
                var verticalEdge = camera.WorldToViewportPoint(
                    visual.Root.position +
                    camera.transform.up * lensingRadius);

                viewportRadius = Mathf.Abs(verticalEdge.y - centre.y);
            }

            SetVectorIfPresent(
                visual.LensingMaterial,
                "_LensCenterViewport",
                new Vector4(centre.x, centre.y, 0.0f, 0.0f));

            SetFloatIfPresent(
                visual.LensingMaterial,
                "_LensViewportRadius",
                Mathf.Clamp(viewportRadius, 0.0001f, 2.0f));
        }

        private void DeactivateCloseBlackHoleLod(bool destroyVisual)
        {
            if (_closeBlackHoleVisual == null)
                return;

            var activeStarIndex = _closeBlackHoleVisual.StarIndex;

            if (activeStarIndex >= 0 &&
                activeStarIndex < _stars.Count)
            {
                SetBaseStarVisualVisible(
                    _stars[activeStarIndex],
                    true);
            }

            if (destroyVisual)
            {
                if (_closeBlackHoleVisual.Root != null)
                {
                    DestroyVisualObject(_closeBlackHoleVisual.Root);
                }

                DestroyMaterial(_closeBlackHoleVisual.HorizonMaterial);
                DestroyMaterial(_closeBlackHoleVisual.LensingMaterial);
                DestroyMaterial(
                    _closeBlackHoleVisual.AccretionDiskMaterial);

                _closeBlackHoleVisual = null;
                return;
            }

            if (_closeBlackHoleVisual.Root != null)
                _closeBlackHoleVisual.Root.gameObject.SetActive(false);
        }

        private Material CreateBlackHoleHorizonMaterial(
            StarData star,
            int starIndex)
        {
            var shader = Shader.Find(
                "SpaceEngine/Streaming/Black Hole Horizon");

            if (shader == null)
            {
                return CreateMaterial(
                    starMaterial,
                    Color.black,
                    enableEmission: false);
            }

            var material = new Material(shader)
            {
                renderQueue = 3110
            };

            var photonColor = star.HasAccretionDisk
                ? new Color(1.0f, 0.30f, 0.055f, 1.0f)
                : new Color(0.07f, 0.12f, 0.20f, 1.0f);

            SetColorIfPresent(material, "_PhotonRingColor", photonColor);
            SetFloatIfPresent(
                material,
                "_PhotonRingIntensity",
                star.HasAccretionDisk ? 0.72f : 0.08f);
            SetFloatIfPresent(material, "_Seed", GetSeedValue(starIndex, 43));
            return material;
        }

        private Material CreateBlackHoleLensingMaterial(int starIndex)
        {
            var shader = Shader.Find(
                "SpaceEngine/Streaming/Black Hole Lens");

            if (shader == null)
                return null;

            var material = new Material(shader)
            {
                renderQueue = 3100
            };

            SetFloatIfPresent(material, "_Seed", GetSeedValue(starIndex, 47));
            SetFloatIfPresent(material, "_LensingStrength", 0.82f);
            SetFloatIfPresent(material, "_LensViewportRadius", 0.10f);
            SetFloatIfPresent(material, "_SceneColorAvailable", 0.0f);
            return material;
        }

        private Material CreateBlackHoleAccretionDiskMaterial(
            StarData star,
            int starIndex,
            bool detailed)
        {
            var shader = Shader.Find(
                "SpaceEngine/Streaming/Black Hole Accretion Disk");

            if (shader == null)
            {
                return CreateMaterial(
                    coronaMaterial != null
                        ? coronaMaterial
                        : starMaterial,
                    new Color(1.0f, 0.32f, 0.06f, 1.0f),
                    enableEmission: true);
            }

            var material = new Material(shader)
            {
                renderQueue = detailed ? 3200 : 3000
            };

            SetFloatIfPresent(material, "_Seed", GetSeedValue(starIndex, 53));
            SetFloatIfPresent(
                material,
                "_RotationSpeed",
                Mathf.Lerp(
                    0.34f,
                    1.25f,
                    GetSeedValue(starIndex, 59)));
            SetFloatIfPresent(
                material,
                "_Intensity",
                detailed ? 3.4f : 2.0f);
            UpdateBlackHoleAccretionDiskMaterial(
                material,
                star,
                starIndex,
                detailed);

            return material;
        }

        private void UpdateBlackHoleAccretionDiskMaterial(
            Material material,
            StarData star,
            int starIndex,
            bool detailed)
        {
            if (material == null)
                return;

            SetFloatIfPresent(
                material,
                "_SurfaceTime",
                (float)_simulationTimeSeconds);
            SetFloatIfPresent(
                material,
                "_Seed",
                GetSeedValue(starIndex, 53));
            SetFloatIfPresent(
                material,
                "_Intensity",
                detailed ? 3.4f : 2.0f);
        }

        private Quaternion GetBlackHoleDiskRotation(int starIndex)
        {
            var normal = GetUnitDirection(
                _loadedSystemSeed,
                starIndex,
                97);

            var spin = Mathf.Lerp(
                0.0f,
                360.0f,
                GetSeedValue(starIndex, 101));

            return Quaternion.AngleAxis(spin, normal) *
                   Quaternion.FromToRotation(Vector3.up, normal);
        }

        private void CreateProminences(
            CloseStarSurfaceVisual visual,
            StarData star,
            int starIndex,
            int layer)
        {
            if (visual.Root == null ||
                star.Type == StarType.WhiteDwarf ||
                star.Type == StarType.NeutronStar ||
                star.Type == StarType.Pulsar)
            {
                return;
            }

            visual.ProminenceMaterial = CreateProminenceMaterial(
                star,
                starIndex);

            var prominenceRoot = new GameObject("Prominence Gas")
            {
                layer = layer
            };

            prominenceRoot.transform.SetParent(visual.Root, false);
            prominenceRoot.transform.localPosition = Vector3.zero;
            prominenceRoot.transform.localRotation = Quaternion.identity;
            prominenceRoot.transform.localScale = Vector3.one;

            // Every prominence is a stack of crossed, softly shaded gas
            // sheets. The shader breaks the sheets into moving plasma wisps,
            // so no solid loop, cylinder or tube remains visible.
            var count = 5 + (int)(_loadedSystemSeed % 5UL);

            for (var index = 0; index < count; index++)
            {
                var prominenceObject = new GameObject(
                    $"Prominence Gas {index}")
                {
                    layer = layer
                };

                prominenceObject.transform.SetParent(
                    prominenceRoot.transform,
                    false);

                var mesh = CreateProminenceMesh(
                    _loadedSystemSeed,
                    starIndex,
                    index);

                prominenceObject.AddComponent<MeshFilter>().sharedMesh = mesh;

                var renderer = prominenceObject.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sharedMaterial = visual.ProminenceMaterial;

                visual.ProminenceMeshes.Add(mesh);
            }
        }

        private void DeactivateCloseStarSurfaceLod(bool destroyVisual)
        {
            if (_closeStarSurfaceVisual == null)
                return;

            var activeStarIndex = _closeStarSurfaceVisual.StarIndex;

            if (activeStarIndex >= 0 &&
                activeStarIndex < _stars.Count)
            {
                SetBaseStarVisualVisible(
                    _stars[activeStarIndex],
                    true);
            }

            if (destroyVisual)
            {
                if (_closeStarSurfaceVisual.Root != null)
                {
                    DestroyVisualObject(_closeStarSurfaceVisual.Root);
                }

                DestroyMaterial(_closeStarSurfaceVisual.SurfaceMaterial);
                DestroyMaterial(_closeStarSurfaceVisual.CoronaMaterial);
                DestroyMaterial(_closeStarSurfaceVisual.ProminenceMaterial);

                for (var i = 0;
                     i < _closeStarSurfaceVisual.ProminenceMeshes.Count;
                     i++)
                {
                    var mesh = _closeStarSurfaceVisual.ProminenceMeshes[i];

                    if (mesh == null)
                        continue;

                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(mesh);
                    else
                        UnityEngine.Object.DestroyImmediate(mesh);
                }

                _closeStarSurfaceVisual = null;
                return;
            }

            if (_closeStarSurfaceVisual.Root != null)
                _closeStarSurfaceVisual.Root.gameObject.SetActive(false);
        }

        private static void SetBaseStarVisualVisible(
            StarVisual star,
            bool isVisible)
        {
            if (star.Renderer != null)
                star.Renderer.enabled = isVisible;

            if (star.CoronaRenderer != null)
                star.CoronaRenderer.enabled = isVisible;

            if (star.Lod1LightTransform != null)
                star.Lod1LightTransform.gameObject.SetActive(isVisible);

            if (star.Lod1LightRenderer != null)
                star.Lod1LightRenderer.enabled = isVisible;

            if (star.AccretionDiskTransform != null)
                star.AccretionDiskTransform.gameObject.SetActive(isVisible);

            if (star.AccretionDiskRenderer != null)
                star.AccretionDiskRenderer.enabled = isVisible;
        }

        private static void SetBasePlanetVisualVisible(
            PlanetVisual planet,
            bool isVisible)
        {
            if (planet.Transform == null)
                return;

            var renderer = planet.Transform.GetComponent<MeshRenderer>();

            if (renderer != null)
                renderer.enabled = isVisible;
        }

        private Material CreateStarLod1LightMaterial(
            StarData star,
            int starIndex)
        {
            var shader = Shader.Find(
                "SpaceEngine/Streaming/Star LOD 1 Light");

            if (shader == null)
            {
                return CreateMaterial(
                    coronaMaterial != null
                        ? coronaMaterial
                        : starMaterial,
                    GetStarColor(star.Type),
                    enableEmission: true);
            }

            var material = new Material(shader);
            SetColorIfPresent(material, "_BaseColor", GetStarColor(star.Type));
            SetFloatIfPresent(material, "_Intensity", 8.0f);
            SetFloatIfPresent(material, "_Opacity", 1.0f);
            SetFloatIfPresent(material, "_RayStrength", 0.65f);
            SetFloatIfPresent(material, "_RayCount", 8.0f);
            SetFloatIfPresent(material, "_DiscRadius", 0.1f);
            SetFloatIfPresent(material, "_Seed", GetSeedValue(starIndex, 17));
            return material;
        }

        private Material CreateCloseSurfaceMaterial(
            StarData star,
            int starIndex)
        {
            var shader = Shader.Find(
                "SpaceEngine/Streaming/Star Surface");

            if (shader == null)
            {
                return CreateMaterial(
                    starMaterial,
                    GetStarColor(star.Type),
                    enableEmission: true);
            }

            var material = new Material(shader);
            var colors = GetCloseSurfaceColors(star.Type);
            var granulationScale = GetGranulationScale(star);

            SetColorIfPresent(material, "_BaseColor", colors.Base);
            SetColorIfPresent(material, "_SurfaceColor", colors.Surface);
            SetColorIfPresent(material, "_HotColor", colors.Hot);
            SetColorIfPresent(material, "_SpotColor", colors.Spot);
            SetFloatIfPresent(material, "_Seed", GetSeedValue(starIndex, 0));
            SetFloatIfPresent(material, "_GranulationScale", granulationScale);
            SetFloatIfPresent(material, "_DetailScale",
                granulationScale * 4.6f);
            SetFloatIfPresent(material, "_SpotScale", GetSpotScale(star));
            SetFloatIfPresent(material, "_SpotStrength",
                GetSpotStrength(star));
            SetFloatIfPresent(material, "_FlowSpeed", GetFlowSpeed(star));
            SetFloatIfPresent(material, "_Intensity",
                GetCloseSurfaceIntensity(star));

            return material;
        }

        private Material CreateCloseCoronaMaterial(
            StarData star,
            int starIndex)
        {
            var shader = Shader.Find(
                "SpaceEngine/Streaming/Star Corona");

            if (shader == null)
            {
                return CreateMaterial(
                    coronaMaterial != null
                        ? coronaMaterial
                        : starMaterial,
                    GetStarColor(star.Type),
                    enableEmission: true);
            }

            var material = new Material(shader);
            var colors = GetCloseSurfaceColors(star.Type);

            SetColorIfPresent(material, "_BaseColor", colors.Hot);
            SetFloatIfPresent(material, "_Seed", GetSeedValue(starIndex, 1));
            SetFloatIfPresent(material, "_Intensity",
                GetCoronaIntensity(star));
            SetFloatIfPresent(material, "_RimPower", 2.15f);
            SetFloatIfPresent(material, "_FlowSpeed",
                GetFlowSpeed(star) * 0.5f);
            SetFloatIfPresent(material, "_TurbulenceScale",
                Mathf.Max(10.0f, GetGranulationScale(star) * 0.72f));
            SetFloatIfPresent(material, "_ShellDisplacement", 0.035f);

            return material;
        }

        private Material CreateProminenceMaterial(
            StarData star,
            int starIndex)
        {
            var shader = Shader.Find(
                "SpaceEngine/Streaming/Plasma Additive");

            if (shader == null)
            {
                return CreateMaterial(
                    coronaMaterial != null
                        ? coronaMaterial
                        : starMaterial,
                    GetStarColor(star.Type),
                    enableEmission: true);
            }

            var material = new Material(shader);
            var colors = GetCloseSurfaceColors(star.Type);

            SetColorIfPresent(material, "_BaseColor", colors.Hot);
            SetFloatIfPresent(material, "_Seed", GetSeedValue(starIndex, 2));
            SetFloatIfPresent(material, "_Intensity",
                GetCoronaIntensity(star) * 1.1f);
            SetFloatIfPresent(material, "_FlowSpeed",
                GetFlowSpeed(star) * 1.8f);
            SetFloatIfPresent(material, "_TurbulenceScale",
                Mathf.Max(8.0f, GetGranulationScale(star) * 0.32f));
            SetFloatIfPresent(material, "_GasSoftness", 1.65f);
            SetFloatIfPresent(material, "_FlickerStrength", 0.30f);

            return material;
        }

        private Mesh ResolveBlackHoleAccretionDiskMesh()
        {
            if (_runtimeBlackHoleAccretionDiskMesh != null)
                return _runtimeBlackHoleAccretionDiskMesh;

            const int angularSegments = 160;
            const int radialSegments = 18;

            var vertexCount = (angularSegments + 1) *
                              (radialSegments + 1);
            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];

            for (var radial = 0; radial <= radialSegments; radial++)
            {
                var radialFraction = radial / (float)radialSegments;
                var radius = Mathf.Lerp(
                    BlackHoleAccretionDiskInnerMeshRadius,
                    BlackHoleAccretionDiskOuterMeshRadius,
                    radialFraction * radialFraction);

                for (var angular = 0;
                     angular <= angularSegments;
                     angular++)
                {
                    var angularFraction =
                        angular / (float)angularSegments;
                    var angle = angularFraction * Mathf.PI * 2.0f;
                    var index = radial * (angularSegments + 1) +
                                angular;

                    vertices[index] = new Vector3(
                        Mathf.Cos(angle) * radius,
                        0.0f,
                        Mathf.Sin(angle) * radius);

                    normals[index] = Vector3.up;
                    uvs[index] = new Vector2(
                        angularFraction,
                        radialFraction);
                }
            }

            var triangles = new int[
                angularSegments * radialSegments * 6];
            var triangleIndex = 0;

            for (var radial = 0; radial < radialSegments; radial++)
            {
                for (var angular = 0;
                     angular < angularSegments;
                     angular++)
                {
                    var current = radial * (angularSegments + 1) +
                                  angular;
                    var next = current + angularSegments + 1;

                    triangles[triangleIndex++] = current;
                    triangles[triangleIndex++] = next;
                    triangles[triangleIndex++] = current + 1;

                    triangles[triangleIndex++] = current + 1;
                    triangles[triangleIndex++] = next;
                    triangles[triangleIndex++] = next + 1;
                }
            }

            _runtimeBlackHoleAccretionDiskMesh = new Mesh
            {
                name = "Runtime Black Hole Accretion Disk",
                indexFormat = IndexFormat.UInt32
            };

            _runtimeBlackHoleAccretionDiskMesh.vertices = vertices;
            _runtimeBlackHoleAccretionDiskMesh.normals = normals;
            _runtimeBlackHoleAccretionDiskMesh.uv = uvs;
            _runtimeBlackHoleAccretionDiskMesh.triangles = triangles;
            _runtimeBlackHoleAccretionDiskMesh.RecalculateBounds();

            return _runtimeBlackHoleAccretionDiskMesh;
        }

        private Mesh ResolveCloseStarSurfaceMesh()
        {
            if (_runtimeCloseStarSurfaceMesh != null)
                return _runtimeCloseStarSurfaceMesh;

            const int longitudeSegments = 256;
            const int latitudeSegments = 128;

            var vertexCount =
                (longitudeSegments + 1) *
                (latitudeSegments + 1);

            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];

            for (var latitude = 0;
                 latitude <= latitudeSegments;
                 latitude++)
            {
                var v = latitude / (float)latitudeSegments;
                var phi = v * Mathf.PI;
                var y = Mathf.Cos(phi) * 0.5f;
                var ringRadius = Mathf.Sin(phi) * 0.5f;

                for (var longitude = 0;
                     longitude <= longitudeSegments;
                     longitude++)
                {
                    var u = longitude / (float)longitudeSegments;
                    var theta = u * Mathf.PI * 2.0f;
                    var position = new Vector3(
                        Mathf.Cos(theta) * ringRadius,
                        y,
                        Mathf.Sin(theta) * ringRadius);

                    var index =
                        latitude * (longitudeSegments + 1) +
                        longitude;

                    vertices[index] = position;
                    normals[index] = position.normalized;
                    uvs[index] = new Vector2(u, v);
                }
            }

            var triangles = new int[
                longitudeSegments * latitudeSegments * 6];

            var triangleIndex = 0;

            for (var latitude = 0;
                 latitude < latitudeSegments;
                 latitude++)
            {
                for (var longitude = 0;
                     longitude < longitudeSegments;
                     longitude++)
                {
                    var current =
                        latitude * (longitudeSegments + 1) +
                        longitude;

                    var next = current + longitudeSegments + 1;

                    triangles[triangleIndex++] = current;
                    triangles[triangleIndex++] = next;
                    triangles[triangleIndex++] = current + 1;

                    triangles[triangleIndex++] = current + 1;
                    triangles[triangleIndex++] = next;
                    triangles[triangleIndex++] = next + 1;
                }
            }

            _runtimeCloseStarSurfaceMesh = new Mesh
            {
                name = "Runtime LOD 2 Star Surface Sphere",
                indexFormat = IndexFormat.UInt32
            };

            _runtimeCloseStarSurfaceMesh.vertices = vertices;
            _runtimeCloseStarSurfaceMesh.normals = normals;
            _runtimeCloseStarSurfaceMesh.uv = uvs;
            _runtimeCloseStarSurfaceMesh.triangles = triangles;
            _runtimeCloseStarSurfaceMesh.RecalculateBounds();

            return _runtimeCloseStarSurfaceMesh;
        }

        private Mesh CreateProminenceMesh(
            ulong systemSeed,
            int starIndex,
            int prominenceIndex)
        {
            const int pathSegments = 48;
            const int sheetCount = 4;
            const int verticesPerSheet = (pathSegments + 1) * 2;

            var direction = GetUnitDirection(
                systemSeed,
                starIndex,
                prominenceIndex);

            var reference = Mathf.Abs(direction.y) > 0.9f
                ? Vector3.right
                : Vector3.up;

            var planeTangent = Vector3.Cross(
                reference,
                direction).normalized;
            var planeNormal = Vector3.Cross(
                direction,
                planeTangent).normalized;

            var phase = GetHash01(
                systemSeed ^
                ((ulong)(starIndex + 17) * 0x9E3779B97F4A7C15UL) ^
                ((ulong)(prominenceIndex + 29) * 0xD1B54A32D192ED03UL));

            var loopHalfWidth = Mathf.Lerp(
                0.030f,
                0.110f,
                GetSeedValue(starIndex, prominenceIndex * 11 + 3));

            var loopHeight = Mathf.Lerp(
                0.070f,
                0.260f,
                GetSeedValue(starIndex, prominenceIndex * 11 + 4));

            var baseWidth = Mathf.Lerp(
                0.0060f,
                0.0170f,
                GetSeedValue(starIndex, prominenceIndex * 11 + 5));

            var lateralWave = Mathf.Lerp(
                0.006f,
                0.026f,
                GetSeedValue(starIndex, prominenceIndex * 11 + 6));

            var path = new Vector3[pathSegments + 1];
            var pathTangents = new Vector3[pathSegments + 1];

            for (var pathIndex = 0;
                 pathIndex <= pathSegments;
                 pathIndex++)
            {
                var t = pathIndex / (float)pathSegments;
                var arch = Mathf.Sin(Mathf.PI * t);
                var lateral = Mathf.Cos(Mathf.PI * t) * loopHalfWidth +
                              Mathf.Sin(Mathf.PI * 2.0f * t +
                                        phase * Mathf.PI * 2.0f) *
                              lateralWave * arch;

                var verticalDistortion = Mathf.Sin(
                    Mathf.PI * 3.0f * t + phase * 5.7f) *
                    lateralWave * 0.55f * arch;

                path[pathIndex] =
                    direction * (0.5f + arch * loopHeight) +
                    planeTangent * lateral +
                    planeNormal * verticalDistortion;
            }

            for (var pathIndex = 0;
                 pathIndex <= pathSegments;
                 pathIndex++)
            {
                var previous = path[Mathf.Max(0, pathIndex - 1)];
                var next = path[Mathf.Min(pathSegments, pathIndex + 1)];
                var tangent = (next - previous).normalized;

                if (tangent.sqrMagnitude <= 0.000001f)
                    tangent = planeTangent;

                pathTangents[pathIndex] = tangent;
            }

            var vertexCount = sheetCount * verticesPerSheet;
            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];
            var uv2s = new Vector2[vertexCount];
            var colors = new Color[vertexCount];

            for (var sheetIndex = 0;
                 sheetIndex < sheetCount;
                 sheetIndex++)
            {
                var sheetPhase = GetHash01(
                    systemSeed ^
                    ((ulong)(starIndex + 7) * 0x94D049BB133111EBUL) ^
                    ((ulong)(prominenceIndex * 13 + sheetIndex + 1) *
                     0xBF58476D1CE4E5B9UL));

                for (var pathIndex = 0;
                     pathIndex <= pathSegments;
                     pathIndex++)
                {
                    var t = pathIndex / (float)pathSegments;
                    var arch = Mathf.Sin(Mathf.PI * t);
                    var pathTangent = pathTangents[pathIndex];
                    var radial = path[pathIndex].normalized;
                    var baseNormal = Vector3.Cross(
                        pathTangent,
                        radial).normalized;

                    if (baseNormal.sqrMagnitude <= 0.000001f)
                        baseNormal = planeNormal;

                    var sheetAngle =
                        (sheetIndex / (float)sheetCount + sheetPhase) *
                        360.0f;

                    var sheetNormal = Quaternion.AngleAxis(
                        sheetAngle,
                        pathTangent) * baseNormal;
                    sheetNormal.Normalize();

                    var sheetSide = Vector3.Cross(
                        sheetNormal,
                        pathTangent).normalized;

                    var width = baseWidth *
                                (0.34f + arch * 1.08f) *
                                (0.82f + 0.28f * Mathf.Sin(
                                    t * Mathf.PI * 4.0f +
                                    sheetPhase * Mathf.PI * 2.0f));

                    for (var sideIndex = 0;
                         sideIndex < 2;
                         sideIndex++)
                    {
                        var side = sideIndex == 0 ? -1.0f : 1.0f;
                        var vertexIndex =
                            sheetIndex * verticesPerSheet +
                            pathIndex * 2 +
                            sideIndex;

                        vertices[vertexIndex] = path[pathIndex] +
                                                sheetSide * side * width;
                        normals[vertexIndex] = sheetNormal;
                        uvs[vertexIndex] = new Vector2(t, sideIndex);
                        uv2s[vertexIndex] = new Vector2(
                            sheetPhase,
                            sheetIndex / (float)(sheetCount - 1));
                        colors[vertexIndex] = new Color(
                            sheetPhase,
                            arch,
                            width / Mathf.Max(baseWidth, 0.0001f),
                            1.0f);
                    }
                }
            }

            var triangles = new int[sheetCount * pathSegments * 6];
            var triangleIndex = 0;

            for (var sheetIndex = 0;
                 sheetIndex < sheetCount;
                 sheetIndex++)
            {
                var sheetStart = sheetIndex * verticesPerSheet;

                for (var pathIndex = 0;
                     pathIndex < pathSegments;
                     pathIndex++)
                {
                    var a = sheetStart + pathIndex * 2;
                    var b = a + 1;
                    var c = a + 2;
                    var d = a + 3;

                    triangles[triangleIndex++] = a;
                    triangles[triangleIndex++] = c;
                    triangles[triangleIndex++] = b;

                    triangles[triangleIndex++] = b;
                    triangles[triangleIndex++] = c;
                    triangles[triangleIndex++] = d;
                }
            }

            var mesh = new Mesh
            {
                name = "Runtime Star Prominence Gas Sheets",
                indexFormat = IndexFormat.UInt32
            };

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.uv2 = uv2s;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            return mesh;
        }

        private static CloseSurfaceColors GetCloseSurfaceColors(
            StarType type)
        {
            switch (type)
            {
                case StarType.RedDwarf:
                    return new CloseSurfaceColors(
                        new Color(1.00f, 0.10f, 0.018f),
                        new Color(1.18f, 0.30f, 0.050f),
                        new Color(1.75f, 0.82f, 0.18f),
                        new Color(0.10f, 0.004f, 0.001f));

                case StarType.OrangeDwarf:
                    return new CloseSurfaceColors(
                        new Color(1.00f, 0.31f, 0.045f),
                        new Color(1.18f, 0.56f, 0.16f),
                        new Color(1.65f, 1.00f, 0.42f),
                        new Color(0.13f, 0.016f, 0.002f));

                case StarType.YellowDwarf:
                    return new CloseSurfaceColors(
                        new Color(1.00f, 0.62f, 0.16f),
                        new Color(1.18f, 0.86f, 0.43f),
                        new Color(1.55f, 1.28f, 0.78f),
                        new Color(0.12f, 0.045f, 0.006f));

                case StarType.WhiteDwarf:
                    return new CloseSurfaceColors(
                        new Color(0.62f, 0.76f, 1.00f),
                        new Color(0.86f, 0.94f, 1.16f),
                        new Color(1.55f, 1.72f, 2.00f),
                        new Color(0.08f, 0.14f, 0.24f));

                case StarType.RedGiant:
                    return new CloseSurfaceColors(
                        new Color(0.92f, 0.09f, 0.014f),
                        new Color(1.18f, 0.34f, 0.055f),
                        new Color(1.65f, 0.86f, 0.20f),
                        new Color(0.11f, 0.004f, 0.001f));

                case StarType.NeutronStar:
                case StarType.Pulsar:
                    return new CloseSurfaceColors(
                        new Color(0.28f, 0.56f, 1.00f),
                        new Color(0.56f, 0.78f, 1.20f),
                        new Color(1.28f, 1.68f, 2.20f),
                        new Color(0.018f, 0.055f, 0.16f));

                default:
                {
                    var baseColor = GetStarColor(type);
                    return new CloseSurfaceColors(
                        baseColor,
                        Color.Lerp(baseColor, Color.white, 0.35f),
                        Color.Lerp(baseColor, Color.white, 0.78f),
                        Color.Lerp(baseColor, Color.black, 0.78f));
                }
            }
        }

        private static float GetCloseSurfaceIntensity(StarData star)
        {
            switch (star.Type)
            {
                case StarType.RedDwarf:
                case StarType.RedGiant:
                    return 1.35f;

                case StarType.WhiteDwarf:
                case StarType.NeutronStar:
                case StarType.Pulsar:
                    return 1.60f;

                default:
                    return 1.20f;
            }
        }

        private static float GetSpotStrength(StarData star)
        {
            switch (star.Type)
            {
                case StarType.RedDwarf:
                    return 0.74f;

                case StarType.RedGiant:
                    return 0.52f;

                case StarType.YellowDwarf:
                    return 0.34f;

                case StarType.OrangeDwarf:
                    return 0.42f;

                case StarType.WhiteDwarf:
                case StarType.NeutronStar:
                case StarType.Pulsar:
                    return 0.12f;

                default:
                    return 0.30f;
            }
        }

        private static float GetGranulationScale(StarData star)
        {
            switch (star.Type)
            {
                case StarType.RedGiant:
                    return 16.0f;

                case StarType.WhiteDwarf:
                case StarType.NeutronStar:
                case StarType.Pulsar:
                    return 92.0f;

                default:
                    return 52.0f;
            }
        }

        private static float GetSpotScale(StarData star)
        {
            return star.Type == StarType.RedGiant
                ? 7.0f
                : 18.0f;
        }

        private static float GetFlowSpeed(StarData star)
        {
            var rotationHours =
                Math.Max(1.0, star.RotationPeriodSeconds / 3600.0);

            return Mathf.Clamp(
                (float)(36.0 / rotationHours),
                0.03f,
                1.4f);
        }

        private static float GetCoronaIntensity(StarData star)
        {
            switch (star.Type)
            {
                case StarType.RedGiant:
                    return 4.0f;

                case StarType.WhiteDwarf:
                case StarType.NeutronStar:
                case StarType.Pulsar:
                    return 3.4f;

                default:
                    return 2.2f;
            }
        }

        private float GetSeedValue(int starIndex, int salt)
        {
            unchecked
            {
                var value = _loadedSystemSeed ^
                            ((ulong)(starIndex + 1) *
                             0x9E3779B97F4A7C15UL) ^
                            ((ulong)(salt + 1) *
                             0xBF58476D1CE4E5B9UL);

                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;

                return (float)((value >> 40) /
                               (double)(1UL << 24));
            }
        }

        private Vector3 GetUnitDirection(
            ulong systemSeed,
            int starIndex,
            int prominenceIndex)
        {
            unchecked
            {
                var thetaSeed = systemSeed ^
                                ((ulong)(starIndex + 3) *
                                 0x9E3779B97F4A7C15UL) ^
                                ((ulong)(prominenceIndex + 11) *
                                 0xD1B54A32D192ED03UL);

                var phiSeed = thetaSeed ^ 0x94D049BB133111EBUL;

                var theta = Mathf.PI * 2.0f *
                            GetHash01(thetaSeed);

                var y = Mathf.Lerp(
                    -0.92f,
                    0.92f,
                    GetHash01(phiSeed));

                var horizontal = Mathf.Sqrt(
                    Mathf.Max(0.0f, 1.0f - y * y));

                return new Vector3(
                    Mathf.Cos(theta) * horizontal,
                    y,
                    Mathf.Sin(theta) * horizontal);
            }
        }

        private static float GetHash01(ulong value)
        {
            unchecked
            {
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;

                return (float)((value >> 40) /
                               (double)(1UL << 24));
            }
        }

        private static void SetColorIfPresent(
            Material material,
            string propertyName,
            Color value)
        {
            if (material != null && material.HasProperty(propertyName))
                material.SetColor(propertyName, value);
        }

        private static void SetFloatIfPresent(
            Material material,
            string propertyName,
            float value)
        {
            if (material != null && material.HasProperty(propertyName))
                material.SetFloat(propertyName, value);
        }

        private static void SetVectorIfPresent(
            Material material,
            string propertyName,
            Vector4 value)
        {
            if (material != null && material.HasProperty(propertyName))
                material.SetVector(propertyName, value);
        }

        private readonly struct CloseSurfaceColors
        {
            public readonly Color Base;
            public readonly Color Surface;
            public readonly Color Hot;
            public readonly Color Spot;

            public CloseSurfaceColors(
                Color baseColor,
                Color surface,
                Color hot,
                Color spot)
            {
                Base = baseColor;
                Surface = surface;
                Hot = hot;
                Spot = spot;
            }
        }

        private void EnsureVisualRoot()
        {
            if (visualRoot != null && visualRoot != ownerTransform)
                return;

            var root = new GameObject("Scaled Solar System");
            root.transform.SetParent(ownerTransform, false);
            root.layer = ReferenceFrameLayerUtility
                .GetSingleLayerIndexOrDefault(scaledSpaceLayer);
            visualRoot = root.transform;
        }

        private static void SetLayerRecursively(
            GameObject target,
            int layer)
        {
            target.layer = layer;

            for (var childIndex = 0;
                 childIndex < target.transform.childCount;
                 childIndex++)
            {
                SetLayerRecursively(
                    target.transform.GetChild(childIndex).gameObject,
                    layer);
            }
        }

        private void ClearVisuals()
        {
            ClearLod2Renderers();
            DeactivateCloseStarSurfaceLod(destroyVisual: true);
            DeactivateCloseBlackHoleLod(destroyVisual: true);

            for (var i = 0; i < _stars.Count; i++)
            {
                DestroyVisualObject(_stars[i].Transform);
                DestroyMaterial(_stars[i].Material);
                DestroyMaterial(_stars[i].CoronaMaterial);
                DestroyMaterial(_stars[i].Lod1LightMaterial);
                DestroyMaterial(_stars[i].AccretionDiskMaterial);
            }

            for (var i = 0; i < _planets.Count; i++)
            {
                DestroyVisualObject(_planets[i].Transform);
                DestroyMaterial(_planets[i].Material);
            }

            _stars.Clear();
            _planets.Clear();
            _totalStarMassKg = 0.0;

            if (_isNearStarSurface)
            {
                _isNearStarSurface = false;
                StarSurfaceLodChanged?.Invoke(false);
            }
        }

        private void ClearLod2Renderers()
        {
            var registeredRenderers = new List<ICelestialBodyLodRenderer>(
                _lod2Renderers.Values);

            for (var index = 0;
                 index < registeredRenderers.Count;
                 index++)
            {
                registeredRenderers[index].SetLodActive(false);
            }

            for (var index = 0;
                 index < _ownedLod2Renderers.Count;
                 index++)
            {
                if (_ownedLod2Renderers[index] is IDisposable disposable)
                    disposable.Dispose();
            }

            _lod2Renderers.Clear();
            _ownedLod2Renderers.Clear();
        }

        private static void DestroyVisualObject(Transform body)
        {
            if (body == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(body.gameObject);
            else
                UnityEngine.Object.DestroyImmediate(body.gameObject);
        }

        private static void DestroyMaterial(Material material)
        {
            if (material == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(material);
            else
                UnityEngine.Object.DestroyImmediate(material);
        }

        private Material CreateMaterial(
            Material sourceMaterial,
            Color color,
            bool enableEmission)
        {
            var source = sourceMaterial != null
                ? sourceMaterial
                : ResolveFallbackMaterial();

            if (source == null)
                return null;

            var material = new Material(source);
            ApplyMaterialColor(material, color, enableEmission);
            return material;
        }

        private static void ApplyMaterialColor(
            Material material,
            Color color,
            bool enableEmission)
        {
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);

            if (!enableEmission ||
                !material.HasProperty("_EmissionColor"))
            {
                return;
            }

            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color);
        }

        private Material ResolveFallbackMaterial()
        {
            if (_runtimeFallbackMaterial != null)
                return _runtimeFallbackMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader == null)
                shader = Shader.Find("Standard");

            if (shader == null)
                return null;

            _runtimeFallbackMaterial = new Material(shader);
            return _runtimeFallbackMaterial;
        }

        private Mesh ResolveSphereMesh()
        {
            if (sphereMesh != null)
                return sphereMesh;

            if (_runtimeSphereMesh != null)
                return _runtimeSphereMesh;

            var temporaryObject = GameObject.CreatePrimitive(
                PrimitiveType.Sphere);

            temporaryObject.hideFlags = HideFlags.HideAndDontSave;

            _runtimeSphereMesh = temporaryObject
                .GetComponent<MeshFilter>()
                .sharedMesh;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(temporaryObject);
            else
                UnityEngine.Object.DestroyImmediate(temporaryObject);

            return _runtimeSphereMesh;
        }

        private Mesh ResolveLod1LightQuadMesh()
        {
            if (_runtimeLod1LightQuadMesh != null)
                return _runtimeLod1LightQuadMesh;

            _runtimeLod1LightQuadMesh = new Mesh
            {
                name = "Runtime Star LOD 1 Light Quad"
            };

            _runtimeLod1LightQuadMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0.0f),
                new Vector3( 0.5f, -0.5f, 0.0f),
                new Vector3( 0.5f,  0.5f, 0.0f),
                new Vector3(-0.5f,  0.5f, 0.0f)
            };

            _runtimeLod1LightQuadMesh.uv = new[]
            {
                new Vector2(0.0f, 0.0f),
                new Vector2(1.0f, 0.0f),
                new Vector2(1.0f, 1.0f),
                new Vector2(0.0f, 1.0f)
            };

            _runtimeLod1LightQuadMesh.triangles = new[]
            {
                0, 1, 2,
                0, 2, 3
            };

            _runtimeLod1LightQuadMesh.RecalculateBounds();
            return _runtimeLod1LightQuadMesh;
        }

        private static Color GetStarColor(StarType type)
        {
            switch (type)
            {
                case StarType.RedDwarf:
                    return new Color(1.0f, 0.23f, 0.10f);

                case StarType.OrangeDwarf:
                    return new Color(1.0f, 0.55f, 0.18f);

                case StarType.YellowDwarf:
                    return new Color(1.0f, 0.92f, 0.60f);

                case StarType.WhiteDwarf:
                    return new Color(0.74f, 0.86f, 1.0f);

                case StarType.RedGiant:
                    return new Color(1.0f, 0.20f, 0.08f);

                case StarType.NeutronStar:
                case StarType.Pulsar:
                    return new Color(0.45f, 0.75f, 1.0f);

                case StarType.BlackHole:
                    return Color.black;

                default:
                    return Color.white;
            }
        }

        private static Color GetPlanetColor(PlanetType type)
        {
            switch (type)
            {
                case PlanetType.Terrestrial:
                    return new Color(0.36f, 0.55f, 0.28f);

                case PlanetType.Ocean:
                    return new Color(0.18f, 0.45f, 0.88f);

                case PlanetType.GasGiant:
                    return new Color(0.86f, 0.60f, 0.32f);

                case PlanetType.IceGiant:
                    return new Color(0.45f, 0.82f, 1.0f);

                case PlanetType.DwarfPlanet:
                    return new Color(0.56f, 0.56f, 0.58f);

                default:
                    return Color.white;
            }
        }
    }

/// <summary>
    /// Exact player or camera position in one galaxy-local reference frame.
    /// Configure it from a known CoordinatesData before enabling a
    /// GalaxySpaceStreamer.
    /// </summary>
    public sealed class GalaxySpaceAnchor
    {
        private CoordinatesData _coordinates;
        private GalaxyData _galaxy;
        private double3 _galaxyLocalPositionLightYears;
        private bool _hasResolvedGalaxy;

        public CoordinatesData Coordinates => _coordinates;

        public GalaxyData Galaxy => _galaxy;

        public bool HasResolvedGalaxy => _hasResolvedGalaxy;

        public double3 GalaxyLocalPositionLightYears =>
            _galaxyLocalPositionLightYears;

        /// <summary>
        /// Resolves the selected galaxy and sets an exact local position.
        /// </summary>
        public void Configure(
            CoordinatesData coordinates,
            double3 galaxyLocalPositionLightYears)
        {
            Configure(
                coordinates,
                GalaxyGenerator.Generate(
                    coordinates.UniverseID,
                    coordinates.GalaxyID),
                galaxyLocalPositionLightYears);
        }

        /// <summary>
        /// Configures a galaxy frame from data whose universe position has
        /// already been resolved by universe streaming. This keeps a physical
        /// approach to a map galaxy continuous when the anchor crosses from
        /// the universe frame into that galaxy's local frame.
        /// </summary>
        public void Configure(
            CoordinatesData coordinates,
            GalaxyData galaxy,
            double3 galaxyLocalPositionLightYears)
        {
            _coordinates = coordinates;
            _galaxy = galaxy;
            _galaxyLocalPositionLightYears =
                galaxyLocalPositionLightYears;
            _hasResolvedGalaxy = true;
        }

        /// <summary>
        /// Resolves the selected galaxy and places the anchor at a known
        /// solar system. The supplied SolarSystemID should originate from
        /// GalaxySectorGenerator, a scanner, a map or saved coordinates.
        /// </summary>
        public void ConfigureAtSolarSystem(
            CoordinatesData coordinates)
        {
            _coordinates = coordinates;

            _galaxy = GalaxyGenerator.Generate(
                coordinates.UniverseID,
                coordinates.GalaxyID);

            var location = SolarSystemLocationGenerator.Generate(
                _galaxy,
                coordinates.SolarSystemID);

            _galaxyLocalPositionLightYears =
                location.GalaxyLocalPositionLightYears;

            _hasResolvedGalaxy = true;
        }

        public void SetGalaxyLocalPosition(
            double3 galaxyLocalPositionLightYears)
        {
            _galaxyLocalPositionLightYears =
                galaxyLocalPositionLightYears;
        }

        public void MoveByLightYears(
            double3 deltaLightYears)
        {
            _galaxyLocalPositionLightYears +=
                deltaLightYears;
        }
    }

/// <summary>
    /// Authoritative hierarchical position for one ship or player.
    ///
    /// Local gameplay stays in metres relative to an active solar-system
    /// barycentre. Galaxy and universe positions are reconstructed only for
    /// large-scale streaming and rendering, where metre precision is neither
    /// needed nor representable in one coordinate value.
    /// </summary>
    public sealed class SeamlessSpaceAnchor
    {
        public const double MetersPerLightYear =
            9_460_730_472_580_800.0;

        public SeamlessSpaceAnchor(GalaxySpaceAnchor galaxySpaceAnchor)
        {
            this.galaxySpaceAnchor = galaxySpaceAnchor ??
                throw new ArgumentNullException(nameof(galaxySpaceAnchor));
        }
        private readonly GalaxySpaceAnchor galaxySpaceAnchor;

        internal GalaxySpaceAnchor GalaxyAnchor => galaxySpaceAnchor;

        private CoordinatesData _coordinates;
        private GalaxyData _galaxy;
        private SolarSystemLocationData _activeSolarSystem;
        private double3 _solarSystemLocalPositionMeters;
        private bool _isConfigured;

        /// <summary>
        /// Invoked after the active solar-system frame changes. The physical
        /// galaxy position remains unchanged during a rebase.
        /// </summary>
        public event Action<CoordinatesData> ActiveSolarSystemChanged;

        public bool IsConfigured => _isConfigured;

        public CoordinatesData Coordinates => _coordinates;

        public GalaxyData Galaxy => _galaxy;

        public SolarSystemLocationData ActiveSolarSystem =>
            _activeSolarSystem;

        /// <summary>
        /// Exact ship position relative to the active system barycentre.
        /// Unit: metres.
        /// </summary>
        public double3 SolarSystemLocalPositionMeters =>
            _solarSystemLocalPositionMeters;

        /// <summary>
        /// Galaxy-local position reconstructed for galaxy-scale work.
        /// Unit: light-years. Do not use this for close-range physics.
        /// </summary>
        public double3 GalaxyLocalPositionLightYears =>
            _activeSolarSystem.GalaxyLocalPositionLightYears +
            _solarSystemLocalPositionMeters / MetersPerLightYear;

        /// <summary>
        /// Universe-space position reconstructed for universe-scale rendering.
        /// Unit: light-years. It intentionally has only large-scale precision.
        /// </summary>
        public double3 UniversePositionLightYears =>
            _galaxy.UniversePositionLightYears +
            GalaxyLocalPositionLightYears;

        public void Configure(
            CoordinatesData coordinates,
            double3 solarSystemLocalPositionMeters)
        {
            _coordinates = coordinates;
            _galaxy = GalaxyGenerator.Generate(
                coordinates.UniverseID,
                coordinates.GalaxyID);

            _activeSolarSystem = SolarSystemLocationGenerator.Generate(
                _galaxy,
                coordinates.SolarSystemID);

            _solarSystemLocalPositionMeters =
                solarSystemLocalPositionMeters;

            _isConfigured = true;
            SynchronizeGalaxyAnchor(forceConfigure: true);

            ActiveSolarSystemChanged?.Invoke(_coordinates);
        }

        /// <summary>
        /// Moves the authoritative ship position. This is the method a ship
        /// controller should call; it never moves astronomical Unity objects.
        /// </summary>
        public void MoveByMeters(double3 deltaMeters)
        {
            if (!_isConfigured)
                return;

            _solarSystemLocalPositionMeters += deltaMeters;
            SynchronizeGalaxyAnchor(forceConfigure: false);
        }

        public void SetSolarSystemLocalPositionMeters(
            double3 positionMeters)
        {
            if (!_isConfigured)
                return;

            _solarSystemLocalPositionMeters = positionMeters;
            SynchronizeGalaxyAnchor(forceConfigure: false);
        }

        /// <summary>
        /// Switches the local reference frame to another known stellar system
        /// without moving the ship in galaxy space.
        /// </summary>
        public void RebaseToSolarSystem(long solarSystemID)
        {
            if (!_isConfigured ||
                solarSystemID == _coordinates.SolarSystemID)
            {
                return;
            }

            var galaxyPosition = GalaxyLocalPositionLightYears;
            var nextSystem =
                SolarSystemLocationGenerator.GenerateFromStreamingID(
                    _galaxy,
                    solarSystemID);

            _coordinates = new CoordinatesData(
                _coordinates.UniverseID,
                _coordinates.GalaxyID,
                solarSystemID);

            _activeSolarSystem = nextSystem;
            _solarSystemLocalPositionMeters =
                (galaxyPosition -
                 nextSystem.GalaxyLocalPositionLightYears) *
                MetersPerLightYear;

            SynchronizeGalaxyAnchor(forceConfigure: true);
            ActiveSolarSystemChanged?.Invoke(_coordinates);
        }

        /// <summary>
        /// Changes the active galaxy frame without moving the traveller in
        /// universe space. It is the universe-scale counterpart of
        /// RebaseToSolarSystem: an approaching external galaxy becomes the
        /// real active galaxy, so its gas and stellar streaming render rather
        /// than leaving the player on a never-ending proxy billboard.
        /// </summary>
        internal bool RebaseToGalaxy(in GalaxyLocationData galaxyLocation)
        {
            if (!_isConfigured ||
                galaxyLocation.GalaxyID == _coordinates.GalaxyID)
            {
                return false;
            }

            var universePosition = UniversePositionLightYears;
            var targetGalaxy = CreateGalaxyAtUniverseLocation(
                _coordinates.UniverseID,
                galaxyLocation);
            var targetGalaxyLocalPosition = universePosition -
                                            targetGalaxy
                                                .UniversePositionLightYears;

            // Keep the existing logical solar-system address. Every ID is a
            // deterministic valid system and its local-frame offset preserves
            // the traveller's exact universe position during the rebase.
            var targetCoordinates = new CoordinatesData(
                _coordinates.UniverseID,
                galaxyLocation.GalaxyID,
                _coordinates.SolarSystemID);
            var targetSystem = SolarSystemLocationGenerator.Generate(
                targetGalaxy,
                targetCoordinates.SolarSystemID);

            _coordinates = targetCoordinates;
            _galaxy = targetGalaxy;
            _activeSolarSystem = targetSystem;
            _solarSystemLocalPositionMeters =
                (targetGalaxyLocalPosition -
                 targetSystem.GalaxyLocalPositionLightYears) *
                MetersPerLightYear;

            SynchronizeGalaxyAnchor(forceConfigure: true);
            ActiveSolarSystemChanged?.Invoke(_coordinates);
            return true;
        }

        /// <summary>
        /// Returns the current ship-to-system-barycentre distance in metres.
        /// </summary>
        public double GetDistanceToActiveSolarSystemMeters()
        {
            return math.length(_solarSystemLocalPositionMeters);
        }

        /// <summary>
        /// Returns a position relative to the ship, in metres, for any known
        /// solar-system location in the same galaxy.
        /// </summary>
        public double3 GetRelativePositionToSolarSystemMeters(
            in SolarSystemLocationData solarSystem)
        {
            return (solarSystem.GalaxyLocalPositionLightYears -
                    _activeSolarSystem.GalaxyLocalPositionLightYears) *
                   MetersPerLightYear -
                   _solarSystemLocalPositionMeters;
        }

        /// <summary>
        /// Returns a position relative to the ship in universe-space
        /// light-years. Use it only for universe-frame rendering.
        /// </summary>
        public double3 GetRelativePositionToGalaxyLightYears(
            in GalaxyLocationData galaxy)
        {
            return galaxy.UniversePositionLightYears -
                   UniversePositionLightYears;
        }


        private static GalaxyData CreateGalaxyAtUniverseLocation(
            long universeID,
            in GalaxyLocationData location)
        {
            var generated = GalaxyGenerator.Generate(
                universeID,
                location.GalaxyID);

            // Galaxy morphology is seeded solely from the logical address;
            // only this map-issued physical location needs replacing.
            return new GalaxyData(
                generated.UniverseID,
                generated.GalaxyID,
                generated.Seed,
                generated.Type,
                location.UniversePositionLightYears,
                generated.RotationRadians,
                generated.RadiusLightYears,
                generated.CoreRadiusLightYears,
                generated.DiskThicknessLightYears,
                generated.MassKg,
                generated.BaseSystemDensityPerCubicLightYear,
                generated.GasDensity,
                generated.Metallicity,
                generated.SpiralArmCount,
                generated.SpiralArmTightness,
                generated.BarLengthLightYears,
                generated.Ellipticity,
                generated.RingRadiusLightYears,
                generated.RingWidthLightYears,
                generated.Irregularity);
        }

        private void SynchronizeGalaxyAnchor(bool forceConfigure)
        {
            if (galaxySpaceAnchor == null)
                return;

            if (forceConfigure ||
                !galaxySpaceAnchor.HasResolvedGalaxy ||
                galaxySpaceAnchor.Coordinates != _coordinates)
            {
                galaxySpaceAnchor.Configure(
                    _coordinates,
                    _galaxy,
                    GalaxyLocalPositionLightYears);

                return;
            }

            galaxySpaceAnchor.SetGalaxyLocalPosition(
                GalaxyLocalPositionLightYears);
        }
    }

/// <summary>
    /// Performs the real universe-to-galaxy frame handoff. The universe field
    /// first shows a distant galaxy marker/proxy; once the traveller reaches
    /// its generated outer radius, the anchor rebases into that galaxy and the
    /// normal gas, stellar field and solar-system streaming take over.
    /// </summary>
    public sealed class UniverseGalaxyStreamingController
    {
        private SeamlessSpaceAnchor spaceAnchor;
        private UniverseGalaxyFieldRenderer universeRenderer;
        private float proximityCheckIntervalSeconds = 0.15f;
        private double activationDistanceInRadii = 1.20;
        private float _nextProximityCheckTime;

        internal void Configure(
            SeamlessSpaceAnchor anchor,
            UniverseGalaxyFieldRenderer renderer,
            double activationDistanceMultiplier)
        {
            spaceAnchor = anchor;
            universeRenderer = renderer;
            activationDistanceInRadii = Math.Max(
                1.0,
                activationDistanceMultiplier);
            proximityCheckIntervalSeconds = Mathf.Max(
                0.01f,
                proximityCheckIntervalSeconds);
        }

        public void Tick(float unscaledTime)
        {
            if (spaceAnchor == null || !spaceAnchor.IsConfigured ||
                universeRenderer == null)
            {
                return;
            }

            if (unscaledTime < _nextProximityCheckTime)
                return;

            _nextProximityCheckTime = unscaledTime +
                                      proximityCheckIntervalSeconds;
            EvaluateNow();
        }

        public void EvaluateNow()
        {
            if (spaceAnchor == null || !spaceAnchor.IsConfigured ||
                universeRenderer == null)
            {
                return;
            }

            if (!universeRenderer.TryFindGalaxyForHandoff(
                    activationDistanceInRadii,
                    out var galaxyLocation))
            {
                return;
            }

            if (!spaceAnchor.RebaseToGalaxy(galaxyLocation))
                return;

            // The proxy list belongs to the old universe frame. Force a
            // rebuild immediately so the now-real active galaxy is excluded
            // and its full active-galaxy renderers take over cleanly.
            universeRenderer.ForceRefresh();
        }
    }

/// <summary>
    /// Activates the scaled solar-system representation around a real nearby
    /// stellar-system point. The point and the solar LOD share the same
    /// SolarSystemLocationData, so the visual handoff does not change place.
    /// </summary>
    public sealed class SeamlessSpaceStreamingController
    { private SeamlessSpaceAnchor spaceAnchor;
        private StellarFieldRenderer stellarFieldRenderer;
        private SolarSystemScaledSpaceRenderer solarSystemRenderer;
        private float proximityCheckIntervalSeconds = 0.25f;
        private int nearestSystemSectorSearchRadius = 1;
        private double solarSystemActivationDistanceLightYears = 0.02;
        private double solarSystemDeactivationDistanceLightYears = 0.03;
        private float stellarPointHideAfterLod1AngularDiameterDegrees =
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
            solarSystemRenderer?.SetScaledSpaceVisible(false);
            UpdateAnchorStellarPointAppearance();
        }

        public void Dispose()
        {
            solarSystemRenderer?.SetScaledSpaceVisible(false);
            SetStellarPointSuppression(false);
            stellarFieldRenderer?.ClearAnchorSolarSystemLocation();
            ClearAnchorPointStyleOverride();
            ApplyAnchorPointStyleOverride();
            _solarSystemLodActive = false;
        }

        internal void Configure(
            SeamlessSpaceAnchor anchor,
            StellarFieldRenderer renderer,
            SolarSystemScaledSpaceRenderer solarRenderer,
            double activationDistanceLightYears,
            double deactivationDistanceLightYears,
            float stellarPointHideAngularDiameterDegrees)
        {
            spaceAnchor = anchor;
            stellarFieldRenderer = renderer;
            solarSystemRenderer = solarRenderer;
            solarSystemActivationDistanceLightYears = Math.Max(
                0.000001,
                activationDistanceLightYears);
            solarSystemDeactivationDistanceLightYears = Math.Max(
                solarSystemActivationDistanceLightYears,
                deactivationDistanceLightYears);
            stellarPointHideAfterLod1AngularDiameterDegrees = Mathf.Max(
                0.001f,
                stellarPointHideAngularDiameterDegrees);
            proximityCheckIntervalSeconds = Mathf.Max(
                0.001f,
                proximityCheckIntervalSeconds);
            nearestSystemSectorSearchRadius = Mathf.Max(
                0,
                nearestSystemSectorSearchRadius);
        }





        public void Tick(float unscaledTime)
        {
            if (spaceAnchor == null || !spaceAnchor.IsConfigured)
                return;

            if (unscaledTime < _nextProximityCheckTime)
                return;

            _nextProximityCheckTime = unscaledTime +
                                      proximityCheckIntervalSeconds;
            EvaluateNow();
        }

        public void EvaluateNow()
        {
            if (spaceAnchor == null || !spaceAnchor.IsConfigured)
                return;

            UpdateAnchorStellarPointAppearance();

            // A CelestialAnchor addresses a concrete solar system directly.
            // It must render near that system even when its opaque ID was not
            // emitted by the density-driven nearby-star catalogue. The field
            // remains an optimisation for surrounding systems only.
            var activationDistanceMeters =
                solarSystemActivationDistanceLightYears *
                SeamlessSpaceAnchor.MetersPerLightYear;

            if (spaceAnchor.GetDistanceToActiveSolarSystemMeters() <=
                activationDistanceMeters)
            {
                ActivateSolarSystemLod(spaceAnchor.ActiveSolarSystem);
                return;
            }

            var hasNearest = SolarSystemProximityResolver.TryFindNearest(
                spaceAnchor.Galaxy,
                spaceAnchor.GalaxyLocalPositionLightYears,
                nearestSystemSectorSearchRadius,
                out var nearestSolarSystem,
                out var nearestDistanceMeters);

            var deactivationDistanceMeters =
                solarSystemDeactivationDistanceLightYears *
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
                spaceAnchor.Coordinates.SolarSystemID &&
                nearestDistanceMeters <= activationDistanceMeters)
            {
                ActivateSolarSystemLod(nearestSolarSystem);
                return;
            }

            if (spaceAnchor.GetDistanceToActiveSolarSystemMeters() >
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
            if (spaceAnchor.Coordinates.SolarSystemID !=
                solarSystem.SolarSystemID)
            {
                spaceAnchor.RebaseToSolarSystem(solarSystem.SolarSystemID);
            }

            if (solarSystemRenderer != null)
            {
                solarSystemRenderer.SetScaledSpaceVisible(true);
                solarSystemRenderer.RefreshNow();
            }

            UpdateAnchorStellarPointAppearance();

            // Keep LOD 0 visible until the scaled star sphere has reached
            // its configured apparent size. This provides an overlap instead
            // of the former one-frame / tiny-star gap.
            SetStellarPointSuppression(false);

            if (_solarSystemLodActive)
            {
                UpdateStellarPointSuppression();
                return;
            }

            _solarSystemLodActive = true;
            UpdateStellarPointSuppression();
            SolarSystemLodEntered?.Invoke(spaceAnchor.Coordinates);
        }

        private void DeactivateSolarSystemLod()
        {
            if (solarSystemRenderer != null)
                solarSystemRenderer.SetScaledSpaceVisible(false);

            UpdateAnchorStellarPointAppearance();
            SetStellarPointSuppression(false);

            if (!_solarSystemLodActive)
                return;

            _solarSystemLodActive = false;
            SolarSystemLodExited?.Invoke(spaceAnchor.Coordinates);
        }

        private void UpdateStellarPointSuppression()
        {
            UpdateAnchorStellarPointAppearance();

            var canHideStellarPoint =
                solarSystemRenderer != null &&
                solarSystemRenderer.IsNearestStarLod1VisibleAt(
                    stellarPointHideAfterLod1AngularDiameterDegrees);

            SetStellarPointSuppression(canHideStellarPoint);
        }

        private void SetStellarPointSuppression(bool suppress)
        {
            if (stellarFieldRenderer != null)
            {
                stellarFieldRenderer.SetAnchorSolarSystemPointSuppressed(
                    suppress);
            }
        }

        private void UpdateAnchorStellarPointAppearance()
        {
            if (spaceAnchor == null || !spaceAnchor.IsConfigured)
            {
                stellarFieldRenderer?.ClearAnchorSolarSystemLocation();
                return;
            }

            // The active system is authoritative even when it came from a
            // logical gameplay ID rather than a density-sector catalogue.
            // Feed its resolved location to LOD 0 every evaluation so the
            // point is always ready before the LOD 1 visual is unloaded.
            stellarFieldRenderer?.SetAnchorSolarSystemLocation(
                spaceAnchor.ActiveSolarSystem);

            ResolveAnchorPointStyleOverride();
            ApplyAnchorPointStyleOverride();
        }

        private void ResolveAnchorPointStyleOverride()
        {
            if (spaceAnchor == null || !spaceAnchor.IsConfigured)
            {
                ClearAnchorPointStyleOverride();
                return;
            }

            var solarSystemID = spaceAnchor.Coordinates.SolarSystemID;
            if (_anchorPointStyleSolarSystemID == solarSystemID)
                return;

            _anchorPointStyleSolarSystemID = solarSystemID;
            _hasAnchorPointStyleOverride = false;
            _anchorPointStyleColor = Color.white;
            _anchorPointStyleIntensity = 1.5f;

            var solarSystem = SolarSystemGenerator.Generate(
                spaceAnchor.Coordinates);

            if (solarSystem.Stars == null || solarSystem.Stars.Length == 0)
                return;

            var hasBlackHole = false;
            var hasAccretionDisk = false;
            var dominantBlackHoleMassKg = double.NegativeInfinity;

            for (var i = 0; i < solarSystem.Stars.Length; i++)
            {
                var star = solarSystem.Stars[i];
                if (star.Type != StarType.BlackHole ||
                    star.MassKg <= dominantBlackHoleMassKg)
                {
                    continue;
                }

                hasBlackHole = true;
                hasAccretionDisk = star.HasAccretionDisk;
                dominantBlackHoleMassKg = star.MassKg;
            }

            if (!hasBlackHole)
                return;

            _hasAnchorPointStyleOverride = true;

            if (hasAccretionDisk)
            {
                _anchorPointStyleColor = Color.white;
                _anchorPointStyleIntensity = 1.35f;
                return;
            }

            _anchorPointStyleColor = new Color(0.70f, 0.70f, 0.70f, 1.0f);
            _anchorPointStyleIntensity = 0.14f;
        }

        private void ApplyAnchorPointStyleOverride()
        {
            if (stellarFieldRenderer == null)
                return;

            if (_hasAnchorPointStyleOverride)
            {
                stellarFieldRenderer.SetAnchorSolarSystemPointOverride(
                    _anchorPointStyleColor,
                    _anchorPointStyleIntensity);

                return;
            }

            stellarFieldRenderer.ClearAnchorSolarSystemPointOverride();
        }

        private void ClearAnchorPointStyleOverride()
        {
            _anchorPointStyleSolarSystemID = long.MinValue;
            _hasAnchorPointStyleOverride = false;
            _anchorPointStyleColor = Color.white;
            _anchorPointStyleIntensity = 1.5f;
        }
    }


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
        public readonly float GalaxyGasBrightness;
        public readonly float GalaxyGasOpacity;
        public readonly float GalaxyGasDustStrength;
        public readonly float GalaxyGasDiskRadiusMultiplier;
        public readonly float GalaxyGasDiskThicknessMultiplier;
        public readonly int StellarFieldSectorRadius;
        public readonly int StellarFieldVerticalSectorRadius;
        public readonly int MaximumStellarPoints;
        public readonly float MinimumStarPointDiameterPixels;
        public readonly float Lod1MinimumStarAngularDiameterDegrees;
        public readonly float MinimumPlanetAngularDiameterDegrees;
        public readonly double ScaledSpaceMetersPerUnityUnit;
        public readonly float MinimumStarDiameterInUnityUnits;
        public readonly float MinimumPlanetDiameterInUnityUnits;
        public readonly float Lod1StarLightRadiusMultiplier;
        public readonly float Lod1StarLightIntensity;
        public readonly float Lod1StarLightRayStrength;
        public readonly float Lod1StarLightRayCount;
        public readonly bool EnableStarLod2LocalSurface;
        public readonly double Lod2StarSurfaceActivationDistanceInRadii;
        public readonly double Lod2StarSurfaceDeactivationDistanceInRadii;
        public readonly bool EnablePlanetLod2LocalSurface;
        public readonly double Lod2PlanetSurfaceActivationDistanceInRadii;
        public readonly double Lod2PlanetSurfaceDeactivationDistanceInRadii;
        public readonly double SolarSystemActivationDistanceLightYears;
        public readonly double SolarSystemDeactivationDistanceLightYears;
        public readonly float StellarPointHideAfterLod1AngularDiameterDegrees;

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
            float galaxyGasBrightness,
            float galaxyGasOpacity,
            float galaxyGasDustStrength,
            float galaxyGasDiskRadiusMultiplier,
            float galaxyGasDiskThicknessMultiplier,
            int stellarFieldSectorRadius,
            int stellarFieldVerticalSectorRadius,
            int maximumStellarPoints,
            float minimumStarPointDiameterPixels,
            float lod1MinimumStarAngularDiameterDegrees,
            float minimumPlanetAngularDiameterDegrees,
            double scaledSpaceMetersPerUnityUnit,
            float minimumStarDiameterInUnityUnits,
            float minimumPlanetDiameterInUnityUnits,
            float lod1StarLightRadiusMultiplier,
            float lod1StarLightIntensity,
            float lod1StarLightRayStrength,
            float lod1StarLightRayCount,
            bool enableStarLod2LocalSurface,
            double lod2StarSurfaceActivationDistanceInRadii,
            double lod2StarSurfaceDeactivationDistanceInRadii,
            bool enablePlanetLod2LocalSurface,
            double lod2PlanetSurfaceActivationDistanceInRadii,
            double lod2PlanetSurfaceDeactivationDistanceInRadii,
            double solarSystemActivationDistanceLightYears,
            double solarSystemDeactivationDistanceLightYears,
            float stellarPointHideAfterLod1AngularDiameterDegrees)
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
            MaximumLoadedExternalGalaxies =
                maximumLoadedExternalGalaxies;
            ExternalGalaxyStarfieldSampleCount =
                externalGalaxyStarfieldSampleCount;
            ExternalGalaxyStarPointDiameterPixels =
                externalGalaxyStarPointDiameterPixels;
            GalaxyActivationDistanceInRadii =
                galaxyActivationDistanceInRadii;
            EnableGalaxyGas = enableGalaxyGas;
            GalaxyGasRaymarchSteps = galaxyGasRaymarchSteps;
            GalaxyGasBrightness = galaxyGasBrightness;
            GalaxyGasOpacity = galaxyGasOpacity;
            GalaxyGasDustStrength = galaxyGasDustStrength;
            GalaxyGasDiskRadiusMultiplier = galaxyGasDiskRadiusMultiplier;
            GalaxyGasDiskThicknessMultiplier =
                galaxyGasDiskThicknessMultiplier;
            StellarFieldSectorRadius = stellarFieldSectorRadius;
            StellarFieldVerticalSectorRadius = stellarFieldVerticalSectorRadius;
            MaximumStellarPoints = maximumStellarPoints;
            MinimumStarPointDiameterPixels = minimumStarPointDiameterPixels;
            Lod1MinimumStarAngularDiameterDegrees =
                lod1MinimumStarAngularDiameterDegrees;
            MinimumPlanetAngularDiameterDegrees =
                minimumPlanetAngularDiameterDegrees;
            ScaledSpaceMetersPerUnityUnit = scaledSpaceMetersPerUnityUnit;
            MinimumStarDiameterInUnityUnits = minimumStarDiameterInUnityUnits;
            MinimumPlanetDiameterInUnityUnits =
                minimumPlanetDiameterInUnityUnits;
            Lod1StarLightRadiusMultiplier = lod1StarLightRadiusMultiplier;
            Lod1StarLightIntensity = lod1StarLightIntensity;
            Lod1StarLightRayStrength = lod1StarLightRayStrength;
            Lod1StarLightRayCount = lod1StarLightRayCount;
            EnableStarLod2LocalSurface = enableStarLod2LocalSurface;
            Lod2StarSurfaceActivationDistanceInRadii =
                lod2StarSurfaceActivationDistanceInRadii;
            Lod2StarSurfaceDeactivationDistanceInRadii =
                lod2StarSurfaceDeactivationDistanceInRadii;
            EnablePlanetLod2LocalSurface = enablePlanetLod2LocalSurface;
            Lod2PlanetSurfaceActivationDistanceInRadii =
                lod2PlanetSurfaceActivationDistanceInRadii;
            Lod2PlanetSurfaceDeactivationDistanceInRadii =
                lod2PlanetSurfaceDeactivationDistanceInRadii;
            SolarSystemActivationDistanceLightYears =
                solarSystemActivationDistanceLightYears;
            SolarSystemDeactivationDistanceLightYears =
                solarSystemDeactivationDistanceLightYears;
            StellarPointHideAfterLod1AngularDiameterDegrees =
                stellarPointHideAfterLod1AngularDiameterDegrees;
        }
    }

    /// <summary>
    /// Owns the non-MonoBehaviour 3D render state. CelestialRenderer3D
    /// drives this object through SpaceEngine's frame loop.
    /// </summary>
    internal sealed class CelestialRenderRuntime : IDisposable
    {
        private readonly SeamlessSpaceAnchor _spaceAnchor;
        private readonly UniverseGalaxyFieldRenderer _universeRenderer = new();
        private readonly UniverseGalaxyStreamingController
            _universeStreamingController = new();
        private readonly GalaxyGasRenderer _galaxyGasRenderer = new();
        private readonly GalaxyStarfieldRenderer _galaxyRenderer = new();
        private readonly StellarFieldRenderer _stellarRenderer = new();
        private readonly SolarSystemScaledSpaceRenderer _solarRenderer;
        private readonly SeamlessSpaceStreamingController _streamingController =
            new();

        public SeamlessSpaceAnchor SpaceAnchor => _spaceAnchor;
        public SeamlessSpaceStreamingController StreamingController =>
            _streamingController;
        public SolarSystemScaledSpaceRenderer SolarRenderer => _solarRenderer;

        public CelestialRenderRuntime(
            Transform celestialRoot,
            SeamlessSpaceAnchor anchor)
        {
            _spaceAnchor = anchor ??
                throw new ArgumentNullException(nameof(anchor));

            _solarRenderer = new SolarSystemScaledSpaceRenderer(celestialRoot);
        }

        public void Configure(in CelestialStreamingSettings settings)
        {
            _universeRenderer.Configure(
                _spaceAnchor,
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

            _universeStreamingController.Configure(
                _spaceAnchor,
                _universeRenderer,
                settings.GalaxyActivationDistanceInRadii);

            _galaxyGasRenderer.Configure(
                _spaceAnchor,
                settings.CelestialCamera,
                settings.CelestialLayer,
                settings.EnableGalaxyGas,
                settings.GalaxyGasRaymarchSteps,
                settings.GalaxyGasBrightness,
                settings.GalaxyGasOpacity,
                settings.GalaxyGasDustStrength,
                settings.GalaxyGasDiskRadiusMultiplier,
                settings.GalaxyGasDiskThicknessMultiplier);

            _galaxyRenderer.Configure(
                _spaceAnchor,
                settings.CelestialCamera,
                settings.CelestialLayer,
                settings.AggregateStarSampleCount,
                settings.StellarFieldSectorRadius * 10.0f);

            _stellarRenderer.Configure(
                _spaceAnchor.GalaxyAnchor,
                settings.CelestialCamera,
                settings.CelestialLayer,
                settings.StellarFieldSectorRadius,
                settings.StellarFieldVerticalSectorRadius,
                settings.MaximumStellarPoints,
                settings.MinimumStarPointDiameterPixels);

            _solarRenderer.Configure(
                _spaceAnchor,
                settings.CelestialCamera,
                settings.CelestialLayer,
                settings.Lod1MinimumStarAngularDiameterDegrees,
                settings.MinimumPlanetAngularDiameterDegrees,
                settings.ScaledSpaceMetersPerUnityUnit,
                settings.MinimumStarDiameterInUnityUnits,
                settings.MinimumPlanetDiameterInUnityUnits,
                settings.Lod1StarLightRadiusMultiplier,
                settings.Lod1StarLightIntensity,
                settings.Lod1StarLightRayStrength,
                settings.Lod1StarLightRayCount,
                settings.EnableStarLod2LocalSurface,
                settings.Lod2StarSurfaceActivationDistanceInRadii,
                settings.Lod2StarSurfaceDeactivationDistanceInRadii,
                settings.EnablePlanetLod2LocalSurface,
                settings.Lod2PlanetSurfaceActivationDistanceInRadii,
                settings.Lod2PlanetSurfaceDeactivationDistanceInRadii);

            _streamingController.Configure(
                _spaceAnchor,
                _stellarRenderer,
                _solarRenderer,
                settings.SolarSystemActivationDistanceLightYears,
                settings.SolarSystemDeactivationDistanceLightYears,
                settings.StellarPointHideAfterLod1AngularDiameterDegrees);
        }

        public void Initialize()
        {
            _streamingController.Initialize();
        }

        public void UpdateStreaming(float unscaledTime)
        {
            // Galaxy context must change before solar-system streaming runs,
            // otherwise the latter would evaluate one frame against the old
            // galaxy after crossing a real galaxy boundary.
            _universeStreamingController.Tick(unscaledTime);
            _streamingController.Tick(unscaledTime);
        }

        public void UpdateVisuals(double simulationTimeSeconds)
        {
            _universeRenderer.Tick();
            _galaxyGasRenderer.Tick();
            _galaxyRenderer.Tick();
            _stellarRenderer.Tick();
            _solarRenderer.Tick(simulationTimeSeconds);
        }

        public void EvaluateNow()
        {
            _universeStreamingController.EvaluateNow();
            _streamingController.EvaluateNow();
            _solarRenderer.RefreshNow();
        }

        public void Dispose()
        {
            _streamingController.Dispose();
            _solarRenderer.Dispose();
            _stellarRenderer.Dispose();
            _galaxyRenderer.Dispose();
            _galaxyGasRenderer.Dispose();
            _universeRenderer.Dispose();
        }
    }
}
