using System;
using System.Collections.Generic;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Galaxy;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Runtime.Streaming
{
    /// <summary>
    /// Streams real SolarSystemLocationData around the current galaxy-space
    /// anchor and draws it as camera-facing star points.
    ///
    /// Every rendered point comes from GalaxySectorGenerator. The same ID and
    /// location can later resolve into a full solar-system LOD, so points do
    /// not jump to another position during a handoff.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GalaxySpaceAnchor))]
    public sealed class StellarFieldRenderer : MonoBehaviour
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

        [SerializeField, HideInInspector] private GalaxySpaceAnchor galaxyAnchor;
        [SerializeField, HideInInspector] private Camera stellarFrameCamera;
        [SerializeField, HideInInspector] private LayerMask stellarFrameLayer;
        [SerializeField, HideInInspector] private Mesh starPointMesh;
        [SerializeField, HideInInspector] private Material starPointMaterial;

        private readonly Dictionary<StreamingSectorKey, List<SolarSystemLocationData>>
            _loadedSectors = new();

        private readonly HashSet<StreamingSectorKey> _desiredSectors = new();
        private readonly HashSet<StreamingSectorKey> _loadingSectors = new();
        private readonly Queue<int3> _sectorQueue = new();

        private readonly List<VisibleStar> _visibleStars = new();
        private readonly List<SolarSystemLocationData> _redStars = new();
        private readonly List<SolarSystemLocationData> _orangeStars = new();
        private readonly List<SolarSystemLocationData> _yellowStars = new();
        private readonly List<SolarSystemLocationData> _blueStars = new();

        private Matrix4x4[][] _redMatrices = Array.Empty<Matrix4x4[]>();
        private Matrix4x4[][] _orangeMatrices = Array.Empty<Matrix4x4[]>();
        private Matrix4x4[][] _yellowMatrices = Array.Empty<Matrix4x4[]>();
        private Matrix4x4[][] _blueMatrices = Array.Empty<Matrix4x4[]>();

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
                stellarFrameCamera != frameCamera ||
                stellarFrameLayer.value != frameLayer.value ||
                _horizontalSectorRadius != horizontalSectorRadius ||
                _verticalSectorRadius != verticalSectorRadius ||
                _maximumVisibleStars != maximumVisibleStars ||
                !Mathf.Approximately(
                    _minimumStarPixels,
                    clampedMinimumStarPixels);

            galaxyAnchor = anchor;
            stellarFrameCamera = frameCamera;
            stellarFrameLayer = frameLayer;
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

        private void Awake()
        {
            galaxyAnchor ??= GetComponent<GalaxySpaceAnchor>();
        }

        private void OnEnable()
        {
            galaxyAnchor ??= GetComponent<GalaxySpaceAnchor>();
        }

        private void OnDisable()
        {
            CompleteAndDisposePendingGeneration();
            ClearCachedSectors();
            _loadedGalaxySeed = 0UL;
            _hasCenterSector = false;
        }

        private void OnDestroy()
        {
            if (_runtimePointMesh != null)
                Destroy(_runtimePointMesh);

            if (_runtimePointMaterial != null)
                Destroy(_runtimePointMaterial);

            if (_runtimePointTexture != null)
                Destroy(_runtimePointTexture);
        }

        public void ForceRefresh()
        {
            CompleteAndDisposePendingGeneration();
            ClearCachedSectors();
            _hasCenterSector = false;
        }

        private void Update()
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
            _redStars.Clear();
            _orangeStars.Clear();
            _yellowStars.Clear();
            _blueStars.Clear();
            _redMatrices = Array.Empty<Matrix4x4[]>();
            _orangeMatrices = Array.Empty<Matrix4x4[]>();
            _yellowMatrices = Array.Empty<Matrix4x4[]>();
            _blueMatrices = Array.Empty<Matrix4x4[]>();
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

            _redStars.Clear();
            _orangeStars.Clear();
            _yellowStars.Clear();
            _blueStars.Clear();

            for (var i = 0; i < _visibleStars.Count; i++)
            {
                var star = _visibleStars[i].Location;
                AddStarToColorBand(star);
            }

            EnsureMatrixStorage(_redStars.Count, ref _redMatrices);
            EnsureMatrixStorage(_orangeStars.Count, ref _orangeMatrices);
            EnsureMatrixStorage(_yellowStars.Count, ref _yellowMatrices);
            EnsureMatrixStorage(_blueStars.Count, ref _blueMatrices);

            _propertyBlock ??= new MaterialPropertyBlock();
            var anchorPosition = galaxyAnchor.GalaxyLocalPositionLightYears;
            var cameraRotation = camera.transform.rotation;

            RenderColorBand(
                _redStars,
                ref _redMatrices,
                new Color(1f, 0.30f, 0.18f),
                anchorPosition,
                camera,
                cameraRotation,
                mesh,
                material);

            RenderColorBand(
                _orangeStars,
                ref _orangeMatrices,
                new Color(1f, 0.62f, 0.22f),
                anchorPosition,
                camera,
                cameraRotation,
                mesh,
                material);

            RenderColorBand(
                _yellowStars,
                ref _yellowMatrices,
                new Color(1f, 0.93f, 0.58f),
                anchorPosition,
                camera,
                cameraRotation,
                mesh,
                material);

            RenderColorBand(
                _blueStars,
                ref _blueMatrices,
                new Color(0.58f, 0.82f, 1f),
                anchorPosition,
                camera,
                cameraRotation,
                mesh,
                material);
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

        private void AddStarToColorBand(SolarSystemLocationData star)
        {
            var mass = star.EstimatedSystemMassSolarMasses;

            if (mass < 0.45)
            {
                _redStars.Add(star);
                return;
            }

            if (mass < 0.85)
            {
                _orangeStars.Add(star);
                return;
            }

            if (mass < 1.40)
            {
                _yellowStars.Add(star);
                return;
            }

            _blueStars.Add(star);
        }

        private void RenderColorBand(
            List<SolarSystemLocationData> stars,
            ref Matrix4x4[][] matrices,
            Color color,
            double3 anchorPosition,
            Camera camera,
            Quaternion cameraRotation,
            Mesh mesh,
            Material material)
        {
            if (stars.Count == 0)
                return;

            var visibleCount = 0;

            for (var i = 0; i < stars.Count; i++)
            {
                var star = stars[i];
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

                matrices[batchIndex][instanceIndex] = Matrix4x4.TRS(
                    position,
                    cameraRotation,
                    Vector3.one * diameter);

                visibleCount++;
            }

            if (visibleCount == 0)
                return;

            _propertyBlock.Clear();
            _propertyBlock.SetColor("_Color", color);
            _propertyBlock.SetColor("_BaseColor", color);
            _propertyBlock.SetColor("_EmissionColor", color * 1.5f);

            var drawn = 0;
            for (var batchIndex = 0;
                 batchIndex < matrices.Length && drawn < visibleCount;
                 batchIndex++)
            {
                var count = Mathf.Min(
                    MaximumInstancesPerDrawCall,
                    visibleCount - drawn);

                Graphics.DrawMeshInstanced(
                    mesh,
                    0,
                    material,
                    matrices[batchIndex],
                    count,
                    _propertyBlock,
                    ShadowCastingMode.Off,
                    false,
                    ReferenceFrameLayerUtility.GetSingleLayerIndexOrDefault(
                        stellarFrameLayer),
                    null,
                    LightProbeUsage.Off,
                    null);

                drawn += count;
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
            return stellarFrameCamera != null
                ? stellarFrameCamera
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
                renderQueue = (int)RenderQueue.Transparent
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
}
