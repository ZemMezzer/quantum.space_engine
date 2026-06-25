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
    public static class SolarSystemSpawnResolver
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
    public static class UniverseSpawnResolver
    {
        public static bool TryResolveExisting(
            ulong universeID,
            ulong galaxyID,
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
            ulong universeID,
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
    /// Renders real procedurally generated galaxies from nearby universe
    /// sectors as distant galaxy proxies. Each proxy corresponds to one
    /// GalaxyLocationData and therefore has the same universe position as the
    /// full galaxy that can later become active.
    /// </summary>
    public sealed class UniverseGalaxyFieldRenderer
    {
        private const int MaximumInstancesPerDrawCall = 1023;
        private SeamlessSpaceAnchor spaceAnchor;
        private Camera celestialCamera;
        private Mesh proxyMesh;
        private Material proxyMaterial;
        private LayerMask celestialLayer = 0;
        private float unityUnitsPerLightYear = 0.000001f;
        private int horizontalSectorRadius = 1;
        private int verticalSectorRadius = 1;
        private bool useCircularFootprint = true;
        private int maximumGalaxyProxies = 512;
        private float minimumGalaxyPixels = 1.5f;
        private float maximumGalaxyDiameter = 4f;
        private float brightnessMultiplier = 0.75f;

        private readonly List<GalaxyLocationData> _galaxies = new();
        private Matrix4x4[][] _matrices = Array.Empty<Matrix4x4[]>();

        private MaterialPropertyBlock _propertyBlock;
        private Mesh _runtimeProxyMesh;
        private Material _runtimeProxyMaterial;

        private int3 _lastCenterSector;
        private bool _hasCenterSector;

        internal void Configure(
            SeamlessSpaceAnchor anchor,
            Camera frameCamera,
            LayerMask frameLayer,
            int maximumProxies)
        {
            var changed =
                spaceAnchor != anchor ||
                celestialCamera != frameCamera ||
                celestialLayer.value != frameLayer.value ||
                maximumGalaxyProxies != maximumProxies;

            spaceAnchor = anchor;
            celestialCamera = frameCamera;
            celestialLayer = frameLayer;
            maximumGalaxyProxies = Mathf.Clamp(
                maximumProxies,
                16,
                4_096);

            if (changed)
                ForceRefresh();
        }



        public void Tick()
        {
            if (spaceAnchor == null || !spaceAnchor.IsConfigured)
                return;

            var centerSector = UniverseSectorUtility.GetCoordinates(
                spaceAnchor.UniversePositionLightYears);

            if (!_hasCenterSector ||
                !centerSector.Equals(_lastCenterSector))
            {
                _lastCenterSector = centerSector;
                _hasCenterSector = true;
                RebuildGalaxyList(centerSector);
            }

            RenderGalaxyProxies();
        }

        public void Dispose()
        {
            if (_runtimeProxyMesh != null)
                UnityEngine.Object.Destroy(_runtimeProxyMesh);

            if (_runtimeProxyMaterial != null)
                UnityEngine.Object.Destroy(_runtimeProxyMaterial);
        }

        public void ForceRefresh()
        {
            _hasCenterSector = false;
        }

        private void RebuildGalaxyList(int3 centerSector)
        {
            _galaxies.Clear();

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

                        var sectorCoordinates = centerSector +
                                                new int3(x, y, z);

                        if (!GalaxyIDUtility.IsSectorCoordinateInRange(
                                sectorCoordinates))
                        {
                            continue;
                        }

                        var sector = UniverseSectorGenerator.Generate(
                            spaceAnchor.Coordinates.UniverseID,
                            sectorCoordinates);

                        for (var i = 0;
                             i < sector.Galaxies.Length &&
                             _galaxies.Count < maximumGalaxyProxies;
                             i++)
                        {
                            var galaxy = sector.Galaxies[i];

                            if (galaxy.GalaxyID ==
                                spaceAnchor.Coordinates.GalaxyID)
                            {
                                continue;
                            }

                            _galaxies.Add(galaxy);
                        }

                        if (_galaxies.Count >= maximumGalaxyProxies)
                            break;
                    }

                    if (_galaxies.Count >= maximumGalaxyProxies)
                        break;
                }

                if (_galaxies.Count >= maximumGalaxyProxies)
                    break;
            }

            EnsureMatrixStorage(_galaxies.Count, ref _matrices);
        }

        private void RenderGalaxyProxies()
        {
            if (_galaxies.Count == 0)
                return;

            var mesh = ResolveProxyMesh();
            var material = ResolveProxyMaterial();
            var camera = ResolveCamera();

            if (mesh == null || material == null || camera == null)
                return;

            var visibleCount = 0;
            var cameraRotation = camera.transform.rotation;

            for (var i = 0; i < _galaxies.Count; i++)
            {
                var galaxy = _galaxies[i];
                var relativeLightYears =
                    spaceAnchor.GetRelativePositionToGalaxyLightYears(
                        galaxy);

                var position = ToUnityPosition(relativeLightYears);
                if (!IsInCameraFrustum(camera, position))
                    continue;

                var batchIndex = visibleCount / MaximumInstancesPerDrawCall;
                var instanceIndex = visibleCount % MaximumInstancesPerDrawCall;

                var diameter = GetGalaxyDiameter(
                    camera,
                    position.magnitude,
                    galaxy.RadiusLightYears);

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

            var color = new Color(
                brightnessMultiplier,
                brightnessMultiplier * 0.94f,
                brightnessMultiplier * 0.88f);

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
                    ReferenceFrameLayerUtility
                        .GetSingleLayerIndexOrDefault(celestialLayer),
                    null,
                    LightProbeUsage.Off,
                    null);

                drawn += count;
            }
        }

        private float GetGalaxyDiameter(
            Camera camera,
            float distance,
            double radiusLightYears)
        {
            var physicalDiameter =
                (float)(radiusLightYears * 2.0 *
                        unityUnitsPerLightYear);

            var halfFovRadians =
                camera.fieldOfView * Mathf.Deg2Rad * 0.5f;

            var unitsPerPixel =
                2f * Mathf.Max(distance, camera.nearClipPlane) *
                Mathf.Tan(halfFovRadians) /
                Mathf.Max(1, Screen.height);

            var pixelDiameter = unitsPerPixel * minimumGalaxyPixels;

            return Mathf.Min(
                Mathf.Max(physicalDiameter, pixelDiameter),
                maximumGalaxyDiameter);
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

        private Material ResolveProxyMaterial()
        {
            var material = proxyMaterial != null
                ? proxyMaterial
                : CreateRuntimeProxyMaterialIfNeeded();

            if (material != null)
                material.enableInstancing = true;

            return material;
        }

        private Material CreateRuntimeProxyMaterialIfNeeded()
        {
            if (_runtimeProxyMaterial != null)
                return _runtimeProxyMaterial;

            var shader = Shader.Find("SpaceEngine/Streaming/Star Point");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader == null)
                return null;

            _runtimeProxyMaterial = new Material(shader)
            {
                name = "Runtime Universe Galaxy Proxy Material",
                enableInstancing = true,
                renderQueue = (int)RenderQueue.Background
            };

            if (_runtimeProxyMaterial.HasProperty("_Cull"))
                _runtimeProxyMaterial.SetFloat("_Cull", 0f);

            if (_runtimeProxyMaterial.HasProperty("_Intensity"))
                _runtimeProxyMaterial.SetFloat("_Intensity", 0.75f);

            if (_runtimeProxyMaterial.HasProperty("_Softness"))
                _runtimeProxyMaterial.SetFloat("_Softness", 2.0f);

            return _runtimeProxyMaterial;
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
            if (_visibleStars.Count == 0)
                return;

            _visibleStars.Sort((left, right) =>
                left.DistanceSquaredLightYears.CompareTo(
                    right.DistanceSquaredLightYears));

            EnsureMatrixStorage(_visibleStars.Count, ref _matrices);

            var anchorPosition = galaxyAnchor.GalaxyLocalPositionLightYears;
            var cameraRotation = camera.transform.rotation;
            var visibleCount = 0;

            for (var i = 0; i < _visibleStars.Count; i++)
            {
                var star = _visibleStars[i].Location;
                var relative =
                    star.GalaxyLocalPositionLightYears - anchorPosition;
                var position = ToUnityPosition(relative);

                if (!IsInCameraFrustum(camera, position))
                    continue;

                var batchIndex = visibleCount / MaximumInstancesPerDrawCall;
                var instanceIndex = visibleCount % MaximumInstancesPerDrawCall;
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

            if (visibleCount == 0)
                return;

            _propertyBlock ??= new MaterialPropertyBlock();
            _propertyBlock.Clear();
            _propertyBlock.SetColor("_Color", Color.white);
            _propertyBlock.SetColor("_BaseColor", Color.white);
            _propertyBlock.SetColor("_EmissionColor", Color.white * 1.5f);

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


        private void CollectVisibleStars()
        {
            _visibleStars.Clear();

            var anchorPosition = galaxyAnchor.GalaxyLocalPositionLightYears;
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

                    if (_suppressAnchorSolarSystemPoint &&
                        location.SolarSystemID ==
                        galaxyAnchor.Coordinates.SolarSystemID)
                    {
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
            public Material ProminenceMaterial;
            public readonly List<Mesh> ProminenceMeshes = new();
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

        private readonly Transform ownerTransform;
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
        private float coronaRadiusMultiplier = 1.12f;
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
        private double simulationTimeScale = 1.0;

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
        private CloseStarSurfaceVisual _closeStarSurfaceVisual;

        /// <summary>
        /// Fires when the nearest star enters or leaves the high-detail
        /// local-surface LOD 2 range. The body data and coordinate frames
        /// remain unchanged across the transition.
        /// </summary>
        public event Action<bool> StarSurfaceLodChanged;

        public bool IsVisible => _isVisible;

        public bool IsNearStarSurface => _isNearStarSurface;

        public ulong LoadedSolarSystemID =>
            spaceAnchor != null && spaceAnchor.IsConfigured
                ? spaceAnchor.Coordinates.SolarSystemID
                : 0UL;

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




        public void Tick(float deltaTime)
        {
            if (!_isVisible || spaceAnchor == null ||
                !spaceAnchor.IsConfigured)
            {
                return;
            }

            _simulationTimeSeconds += deltaTime * simulationTimeScale;

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

            var renderedRadius = GetStarRenderRadiusMeters(
                proximity.RadiusMeters,
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

                var lod2Renderer = new StarLocalSurfaceLodRenderer(
                    this,
                    i);

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
                    star.Data.RadiusMeters,
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
            double physicalRadiusMeters,
            double distanceToCentreMeters)
        {
            var presentationRadius = GetStarPresentationRadiusMeters(
                physicalRadiusMeters);

            var angularRadius = GetMinimumAngularRadiusMeters(
                distanceToCentreMeters,
                minimumStarAngularDiameterDegrees);

            return Math.Max(presentationRadius, angularRadius);
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
                star.Data.Type != StarType.BlackHole &&
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
                GetStarPresentationRadiusMeters(star.Data.RadiusMeters),
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
                1.0f,
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
                    1.06f);

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

            var prominenceRoot = new GameObject("Prominences")
            {
                layer = layer
            };

            prominenceRoot.transform.SetParent(visual.Root, false);
            prominenceRoot.transform.localPosition = Vector3.zero;
            prominenceRoot.transform.localRotation = Quaternion.identity;
            prominenceRoot.transform.localScale = Vector3.one;

            var count = 5 + (int)(_loadedSystemSeed % 5UL);

            for (var index = 0; index < count; index++)
            {
                var prominenceObject = new GameObject(
                    $"Prominence {index}")
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

            SetColorIfPresent(material, "_BaseColor", colors.Base);
            SetColorIfPresent(material, "_SurfaceColor", colors.Surface);
            SetColorIfPresent(material, "_HotColor", colors.Hot);
            SetColorIfPresent(material, "_SpotColor", colors.Spot);
            SetFloatIfPresent(material, "_Seed", GetSeedValue(starIndex, 0));
            SetFloatIfPresent(material, "_GranulationScale",
                GetGranulationScale(star));
            SetFloatIfPresent(material, "_SpotScale",
                GetSpotScale(star));
            SetFloatIfPresent(material, "_FlowSpeed",
                GetFlowSpeed(star));

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
            SetFloatIfPresent(material, "_RimPower", 2.2f);
            SetFloatIfPresent(material, "_FlowSpeed",
                GetFlowSpeed(star) * 0.5f);

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
                GetCoronaIntensity(star) * 1.4f);
            SetFloatIfPresent(material, "_PulseSpeed",
                GetFlowSpeed(star) * 2.0f);

            return material;
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
            const int pathSegments = 28;
            const int tubeSegments = 6;

            var direction = GetUnitDirection(
                systemSeed,
                starIndex,
                prominenceIndex);

            var reference = Mathf.Abs(direction.y) > 0.9f
                ? Vector3.right
                : Vector3.up;

            var tangent = Vector3.Cross(reference, direction).normalized;
            var bitangent = Vector3.Cross(direction, tangent).normalized;

            var loopHalfWidth = Mathf.Lerp(
                0.025f,
                0.105f,
                GetSeedValue(starIndex, prominenceIndex * 7 + 3));

            var loopHeight = Mathf.Lerp(
                0.045f,
                0.22f,
                GetSeedValue(starIndex, prominenceIndex * 7 + 4));

            var tubeRadius = Mathf.Lerp(
                0.0025f,
                0.0080f,
                GetSeedValue(starIndex, prominenceIndex * 7 + 5));

            var vertices = new Vector3[
                (pathSegments + 1) * tubeSegments];

            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];

            for (var pathIndex = 0;
                 pathIndex <= pathSegments;
                 pathIndex++)
            {
                var t = pathIndex / (float)pathSegments;
                var angle = Mathf.PI * t;

                var centre = direction * (0.5f +
                                          Mathf.Sin(angle) * loopHeight) +
                             tangent *
                             (Mathf.Cos(angle) * loopHalfWidth);

                var pathTangent =
                    direction * (Mathf.Cos(angle) * loopHeight) -
                    tangent * (Mathf.Sin(angle) * loopHalfWidth);

                pathTangent.Normalize();

                var side = Vector3.Cross(
                    pathTangent,
                    bitangent).normalized;

                for (var tubeIndex = 0;
                     tubeIndex < tubeSegments;
                     tubeIndex++)
                {
                    var tubeAngle =
                        Mathf.PI * 2.0f * tubeIndex / tubeSegments;

                    var radial = side * Mathf.Cos(tubeAngle) +
                                 bitangent * Mathf.Sin(tubeAngle);

                    var index = pathIndex * tubeSegments + tubeIndex;

                    vertices[index] = centre + radial * tubeRadius;
                    normals[index] = radial;
                    uvs[index] = new Vector2(
                        t,
                        tubeIndex / (float)tubeSegments);
                }
            }

            var triangles = new int[
                pathSegments * tubeSegments * 6];

            var triangleIndex = 0;

            for (var pathIndex = 0;
                 pathIndex < pathSegments;
                 pathIndex++)
            {
                for (var tubeIndex = 0;
                     tubeIndex < tubeSegments;
                     tubeIndex++)
                {
                    var nextTubeIndex =
                        (tubeIndex + 1) % tubeSegments;

                    var a = pathIndex * tubeSegments + tubeIndex;
                    var b = pathIndex * tubeSegments + nextTubeIndex;
                    var c = (pathIndex + 1) * tubeSegments + tubeIndex;
                    var d = (pathIndex + 1) * tubeSegments + nextTubeIndex;

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
                name = "Runtime Star Prominence",
                indexFormat = IndexFormat.UInt32
            };

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            return mesh;
        }

        private static CloseSurfaceColors GetCloseSurfaceColors(
            StarType type)
        {
            var baseColor = GetStarColor(type);
            var surface = Color.Lerp(baseColor, Color.white, 0.32f);
            var hot = Color.Lerp(baseColor, Color.white, 0.74f);
            var spot = Color.Lerp(baseColor, Color.black, 0.72f);

            return new CloseSurfaceColors(
                baseColor,
                surface,
                hot,
                spot);
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

            for (var i = 0; i < _stars.Count; i++)
            {
                DestroyVisualObject(_stars[i].Transform);
                DestroyMaterial(_stars[i].Material);
                DestroyMaterial(_stars[i].CoronaMaterial);
                DestroyMaterial(_stars[i].Lod1LightMaterial);
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
            _coordinates = coordinates;

            _galaxy = GalaxyGenerator.Generate(
                coordinates.UniverseID,
                coordinates.GalaxyID);

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
        public void RebaseToSolarSystem(ulong solarSystemID)
        {
            if (!_isConfigured ||
                solarSystemID == _coordinates.SolarSystemID)
            {
                return;
            }

            var galaxyPosition = GalaxyLocalPositionLightYears;
            var nextSystem = SolarSystemLocationGenerator.Generate(
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
                    GalaxyLocalPositionLightYears);

                return;
            }

            galaxySpaceAnchor.SetGalaxyLocalPosition(
                GalaxyLocalPositionLightYears);
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

        public event Action<CoordinatesData> SolarSystemLodEntered;
        public event Action<CoordinatesData> SolarSystemLodExited;

        public bool IsSolarSystemLodActive => _solarSystemLodActive;

        public void Initialize()
        {
            solarSystemRenderer?.SetScaledSpaceVisible(false);
        }

        public void Dispose()
        {
            solarSystemRenderer?.SetScaledSpaceVisible(false);
            SetStellarPointSuppression(false);
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

            var hasNearest = SolarSystemProximityResolver.TryFindNearest(
                spaceAnchor.Galaxy,
                spaceAnchor.GalaxyLocalPositionLightYears,
                nearestSystemSectorSearchRadius,
                out var nearestSolarSystem,
                out var nearestDistanceMeters);

            var activationDistanceMeters =
                solarSystemActivationDistanceLightYears *
                SeamlessSpaceAnchor.MetersPerLightYear;

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

            SetStellarPointSuppression(false);

            if (!_solarSystemLodActive)
                return;

            _solarSystemLodActive = false;
            SolarSystemLodExited?.Invoke(spaceAnchor.Coordinates);
        }

        private void UpdateStellarPointSuppression()
        {
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
    }


    internal readonly struct CelestialStreamingSettings
    {
        public readonly Camera CelestialCamera;
        public readonly LayerMask CelestialLayer;
        public readonly int AggregateStarSampleCount;
        public readonly int MaximumGalaxyProxies;
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
    /// Owns all non-MonoBehaviour celestial state. HierarchicalCelestialStreaming
    /// is the only component that calls this object every frame.
    /// </summary>
    internal sealed class CelestialStreamingRuntime : IDisposable
    {
        private readonly GalaxySpaceAnchor _galaxyAnchor = new();
        private readonly SeamlessSpaceAnchor _spaceAnchor;
        private readonly UniverseGalaxyFieldRenderer _universeRenderer = new();
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

        public CelestialStreamingRuntime(Transform celestialRoot)
        {
            _spaceAnchor = new SeamlessSpaceAnchor(_galaxyAnchor);
            _solarRenderer = new SolarSystemScaledSpaceRenderer(celestialRoot);
        }

        public void Configure(in CelestialStreamingSettings settings)
        {
            _universeRenderer.Configure(
                _spaceAnchor,
                settings.CelestialCamera,
                settings.CelestialLayer,
                settings.MaximumGalaxyProxies);

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
                _galaxyAnchor,
                settings.CelestialCamera,
                settings.CelestialLayer,
                settings.StellarFieldSectorRadius,
                settings.StellarFieldVerticalSectorRadius,
                settings.MaximumStellarPoints,
                settings.MinimumStarPointDiameterPixels);

            _solarRenderer.Configure(
                _spaceAnchor,
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
            _streamingController.Tick(unscaledTime);
        }

        public void UpdateVisuals(float deltaTime)
        {
            _universeRenderer.Tick();
            _galaxyGasRenderer.Tick();
            _galaxyRenderer.Tick();
            _stellarRenderer.Tick();
            _solarRenderer.Tick(deltaTime);
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
