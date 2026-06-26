using System;
using System.Collections.Generic;
using SpaceEngine.Runtime.Content;
using SpaceEngine.Runtime.Content.StellarObjects.Generation.Galaxies;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Galaxy;
using SpaceEngine.Runtime.Streaming.Runtime.Anchors;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Rendering.Runtime.Galaxy
{
    public sealed class StellarFieldRenderer
    {
        private static readonly int Surface = Shader.PropertyToID("_Surface");
        private static readonly int Blend = Shader.PropertyToID("_Blend");
        private static readonly int ZWrite = Shader.PropertyToID("_ZWrite");
        private static readonly int Cull = Shader.PropertyToID("_Cull");
        private static readonly int Intensity = Shader.PropertyToID("_Intensity");
        private static readonly int Softness = Shader.PropertyToID("_Softness");
        private static readonly int Color1 = Shader.PropertyToID("_Color");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private const int MAXIMUM_INSTANCES_PER_DRAW_CALL = 1023;
        private const int SECTORS_GENERATED_PER_FRAME = 192;
        private const double GALAXY_SECTOR_SIZE_LIGHT_YEARS = 10.0;
        private const int CACHED_BORDER_IN_SECTORS = 2;
        private const int IMMEDIATE_CORE_HORIZONTAL_RADIUS = 2;
        private const int IMMEDIATE_CORE_VERTICAL_RADIUS = 2;
        private const float UNITY_UNITS_PER_LIGHT_YEAR = 1f;
        private const float DEFAULT_MINIMUM_STAR_PIXELS = 2.5f;
        private const float MAXIMUM_STAR_DIAMETER = 1.5f;

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
        private SpaceEngineConfiguration configuration;
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
        private float _minimumStarPixels = DEFAULT_MINIMUM_STAR_PIXELS;
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
            SpaceEngineConfiguration contentConfiguration,
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
                configuration != contentConfiguration ||
                _horizontalSectorRadius != horizontalSectorRadius ||
                _verticalSectorRadius != verticalSectorRadius ||
                _maximumVisibleStars != maximumVisibleStars ||
                !Mathf.Approximately(
                    _minimumStarPixels,
                    clampedMinimumStarPixels);

            galaxyAnchor = anchor;
            celestialCamera = frameCamera;
            celestialLayer = frameLayer;
            configuration = contentConfiguration;
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

            GenerateQueuedSectors();

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
                IMMEDIATE_CORE_HORIZONTAL_RADIUS * IMMEDIATE_CORE_HORIZONTAL_RADIUS;

            for (var z = -IMMEDIATE_CORE_HORIZONTAL_RADIUS;
                 z <= IMMEDIATE_CORE_HORIZONTAL_RADIUS;
                 z++)
            {
                for (var y = -IMMEDIATE_CORE_VERTICAL_RADIUS;
                     y <= IMMEDIATE_CORE_VERTICAL_RADIUS;
                     y++)
                {
                    for (var x = -IMMEDIATE_CORE_HORIZONTAL_RADIUS;
                         x <= IMMEDIATE_CORE_HORIZONTAL_RADIUS;
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

                        AddSector(ResolveGalaxyGenerator().GenerateSector(
                            galaxyAnchor.Galaxy,
                            coordinates));
                    }
                }
            }
        }

        private void PruneDistantCachedSectors(int3 centerSector)
        {
            var retainedHorizontalRadius =
                _horizontalSectorRadius + CACHED_BORDER_IN_SECTORS;
            var retainedVerticalRadius =
                _verticalSectorRadius + CACHED_BORDER_IN_SECTORS;
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

        private void GenerateQueuedSectors()
        {
            if (_sectorQueue.Count == 0 || galaxyAnchor == null ||
                configuration == null)
            {
                return;
            }

            var generator = ResolveGalaxyGenerator();
            var generated = 0;

            while (_sectorQueue.Count > 0 &&
                   generated < SECTORS_GENERATED_PER_FRAME)
            {
                var coordinates = _sectorQueue.Dequeue();
                var key = new StreamingSectorKey(coordinates);

                if (!_desiredSectors.Contains(key) ||
                    _loadedSectors.ContainsKey(key))
                {
                    continue;
                }

                AddSector(generator.GenerateSector(
                    galaxyAnchor.Galaxy,
                    coordinates));
                generated++;
            }
        }

        private GalaxyGenerator
            ResolveGalaxyGenerator()
        {
            var galaxy = galaxyAnchor.Galaxy;
            return SpaceEngine.Runtime.Generation.Universe.UniverseGeneration.ResolveGalaxyGenerator(
                configuration.GalaxyGenerators,
                galaxy.UniverseID,
                galaxy.GalaxyID,
                galaxy.UniversePositionLightYears);
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
                        visibleCount / MAXIMUM_INSTANCES_PER_DRAW_CALL;
                    var instanceIndex =
                        visibleCount % MAXIMUM_INSTANCES_PER_DRAW_CALL;
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
                _propertyBlock.SetColor(Color1, Color.white);
                _propertyBlock.SetColor(BaseColor, Color.white);
                _propertyBlock.SetColor(
                    EmissionColor,
                    Color.white * 1.5f);
                _propertyBlock.SetFloat(Intensity, 1.0f);

                var drawn = 0;
                for (var batchIndex = 0;
                     batchIndex < _matrices.Length && drawn < visibleCount;
                     batchIndex++)
                {
                    var count = Mathf.Min(
                        MAXIMUM_INSTANCES_PER_DRAW_CALL,
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
                        LightProbeUsage.Off);

                    drawn += count;
                }
            }

            if (shouldRenderAnchorPoint)
                RenderAnchorSolarSystemPoint(camera, mesh, material);
        }


        private void CollectVisibleStars()
        {
            _visibleStars.Clear();
            
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
                (_horizontalSectorRadius + CACHED_BORDER_IN_SECTORS) *
                GALAXY_SECTOR_SIZE_LIGHT_YEARS;
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
            _propertyBlock.SetColor(Color1, _anchorSolarSystemPointColor);
            _propertyBlock.SetColor(
                BaseColor,
                _anchorSolarSystemPointColor);
            _propertyBlock.SetColor(
                EmissionColor,
                _anchorSolarSystemPointColor *
                _anchorSolarSystemPointIntensity);
            _propertyBlock.SetFloat(
                Intensity,
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
                LightProbeUsage.Off);
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
                MAXIMUM_STAR_DIAMETER);
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
                (float)(relativeLightYears.x * UNITY_UNITS_PER_LIGHT_YEAR),
                (float)(relativeLightYears.y * UNITY_UNITS_PER_LIGHT_YEAR),
                (float)(relativeLightYears.z * UNITY_UNITS_PER_LIGHT_YEAR));
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

                if (_runtimePointMaterial.HasProperty(BaseMap))
                    _runtimePointMaterial.SetTexture(BaseMap, _runtimePointTexture);

                if (_runtimePointMaterial.HasProperty(MainTex))
                    _runtimePointMaterial.SetTexture(MainTex, _runtimePointTexture);
            }

            if (_runtimePointMaterial.HasProperty(BaseColor))
                _runtimePointMaterial.SetColor(BaseColor, Color.white);

            if (_runtimePointMaterial.HasProperty(Color1))
                _runtimePointMaterial.SetColor(Color1, Color.white);

            if (_runtimePointMaterial.HasProperty(Surface))
                _runtimePointMaterial.SetFloat(Surface, 1f);

            if (_runtimePointMaterial.HasProperty(Blend))
                _runtimePointMaterial.SetFloat(Blend, 1f);

            if (_runtimePointMaterial.HasProperty(ZWrite))
                _runtimePointMaterial.SetFloat(ZWrite, 0f);

            if (_runtimePointMaterial.HasProperty(Cull))
                _runtimePointMaterial.SetFloat(Cull, 0f);

            if (_runtimePointMaterial.HasProperty(Intensity))
                _runtimePointMaterial.SetFloat(Intensity, 1.15f);

            if (_runtimePointMaterial.HasProperty(Softness))
                _runtimePointMaterial.SetFloat(Softness, 2.5f);

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
                (instanceCount + MAXIMUM_INSTANCES_PER_DRAW_CALL - 1) /
                MAXIMUM_INSTANCES_PER_DRAW_CALL;

            if (matrices.Length == requiredBatchCount)
                return;

            matrices = new Matrix4x4[requiredBatchCount][];

            for (var i = 0; i < requiredBatchCount; i++)
            {
                matrices[i] =
                    new Matrix4x4[MAXIMUM_INSTANCES_PER_DRAW_CALL];
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
}
