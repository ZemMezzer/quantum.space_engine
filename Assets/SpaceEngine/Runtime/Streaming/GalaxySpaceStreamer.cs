using System;
using System.Collections.Generic;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Galaxy;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace SpaceEngine.Runtime.Streaming
{
    /// <summary>
    /// Streams galaxy sectors around a GalaxySpaceAnchor.
    ///
    /// This is a galaxy-scale visual LOD: solar systems are rendered as
    /// GPU-instanced markers only. It intentionally creates no per-system
    /// GameObjects, no colliders and no pointer-selection logic.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GalaxySpaceAnchor))]
    [RequireComponent(typeof(GalaxyStarfieldRenderer))]
    public sealed class GalaxySpaceStreamer : MonoBehaviour
    {
        private readonly struct FarFieldMarkerData
        {
            public readonly StreamingSectorKey SectorKey;
            public readonly double3 PositionLightYears;

            public FarFieldMarkerData(
                StreamingSectorKey sectorKey,
                double3 positionLightYears)
            {
                SectorKey = sectorKey;
                PositionLightYears = positionLightYears;
            }
        }

        private const int MaximumInstancesPerDrawCall = 1023;
        // Selects a stable scan order for real sector candidates. It does not
        // create synthetic marker positions.
        private const ulong FarFieldCandidateOrderSalt =
            0x4641525F53544152UL;

        [Header("References")]
        [SerializeField, HideInInspector] private GalaxySpaceAnchor galaxyAnchor;
        [SerializeField, HideInInspector] private Material markerMaterial;
        [FormerlySerializedAs("farFieldMarkerMesh")]
        [SerializeField, HideInInspector] private Mesh markerMesh;
        [SerializeField, HideInInspector] private Material farFieldMaterial;

        [Header("Detailed sector streaming")]
        [SerializeField, HideInInspector, Min(0)]
        private int detailHorizontalSectorRadius = 2;
        [SerializeField, HideInInspector, Min(0)]
        private int detailVerticalSectorRadius = 1;
        [SerializeField, HideInInspector]
        private bool useCircularDetailFootprint = true;
        [SerializeField, HideInInspector, Min(1)]
        private int sectorsPerJob = 32;
        [SerializeField, HideInInspector, Min(1)]
        private int sectorsAppliedPerFrame = 4;
        [SerializeField, HideInInspector, Min(1)]
        private int jobBatchSize = 16;
        [SerializeField, HideInInspector, Min(1)]
        private int maximumDetailedMarkers = 4_000;

        [Header("Far visual field")]
        [FormerlySerializedAs("sectorRadius")]
        [SerializeField, HideInInspector, Min(0)]
        private int farHorizontalSectorRadius = 20;
        [SerializeField, HideInInspector, Min(0)]
        private int farVerticalSectorRadius = 2;
        [SerializeField, HideInInspector]
        private bool useCircularFarFootprint = true;
        [SerializeField, HideInInspector, Min(1)]
        private int farSectorStride = 2;
        [SerializeField, HideInInspector, Range(1, 8)]
        private int farSamplesPerSector = 2;
        [SerializeField, HideInInspector, Min(1)]
        private int maximumFarFieldMarkers = 20_000;
        [SerializeField, HideInInspector, Min(0.0001f)]
        private float farFieldMarkerDiameter = 0.08f;
        [SerializeField, HideInInspector, Range(0.1f, 4f)]
        private float farFieldDensityMultiplier = 1.25f;
        [SerializeField, HideInInspector]
        private Color farFieldColor = new Color(0.7f, 0.84f, 1f, 0.9f);

        [Header("World scale")]
        [SerializeField, HideInInspector]
        private LayerMask stellarFrameLayer;
        [SerializeField, HideInInspector, Min(0.0001f)]
        private float unityUnitsPerLightYear = 10f;

        [Header("Detailed marker visuals")]
        [SerializeField, HideInInspector, Min(0.001f)]
        private float baseSystemMarkerDiameter = 0.75f;
        [SerializeField, HideInInspector]
        private bool hideAnchorSolarSystem = true;

        private readonly Dictionary<StreamingSectorKey, List<ulong>>
            _sectorObjectIDs = new();

        private readonly Dictionary<ulong, SolarSystemLocationData>
            _solarSystems = new();

        private readonly HashSet<StreamingSectorKey>
            _desiredDetailedSectors = new();

        private readonly HashSet<StreamingSectorKey>
            _queuedDetailedSectors = new();

        private readonly HashSet<StreamingSectorKey>
            _loadingDetailedSectors = new();

        private readonly Queue<int3> _detailSectorQueue = new();

        // Every far marker is an actual generated solar-system candidate.
        // It must therefore resolve to the exact same location at detail LOD.
        private readonly List<FarFieldMarkerData> _farFieldMarkers = new();

        private readonly List<SolarSystemLocationData>
            _redDetailedSystems = new();

        private readonly List<SolarSystemLocationData>
            _orangeDetailedSystems = new();

        private readonly List<SolarSystemLocationData>
            _yellowDetailedSystems = new();

        private readonly List<SolarSystemLocationData>
            _blueDetailedSystems = new();

        private NativeArray<int3> _pendingSectorCoordinates;
        private NativeArray<GalaxySectorData> _pendingResults;
        private JobHandle _pendingJob;
        private bool _hasPendingJob;
        private bool _pendingJobCompleted;
        private int _pendingResultIndex;

        private Matrix4x4[][] _farFieldMatrices = Array.Empty<Matrix4x4[]>();
        private Matrix4x4[][] _redDetailedMatrices = Array.Empty<Matrix4x4[]>();
        private Matrix4x4[][] _orangeDetailedMatrices = Array.Empty<Matrix4x4[]>();
        private Matrix4x4[][] _yellowDetailedMatrices = Array.Empty<Matrix4x4[]>();
        private Matrix4x4[][] _blueDetailedMatrices = Array.Empty<Matrix4x4[]>();

        private MaterialPropertyBlock _farFieldPropertyBlock;
        private MaterialPropertyBlock _detailedPropertyBlock;

        private Material _runtimeFallbackMaterial;
        private Mesh _runtimeFallbackMarkerMesh;
        private ulong _loadedGalaxySeed;

        private int3 _lastDetailedCenterCoordinates;
        private bool _hasDetailedCenterCoordinates;

        private int3 _lastFarCenterCoordinates;
        private bool _hasFarCenterCoordinates;

        private bool _detailedDrawDataDirty = true;

        // The seamless controller overrides the inspector default while a
        // solar-system visual is handing off to or from the galaxy marker.
        private bool _hasAnchorSystemMarkerSuppressionOverride;
        private bool _suppressAnchorSystemMarkerAtRuntime;

        internal void Configure(
            GalaxySpaceAnchor anchor,
            LayerMask frameLayer,
            int requestedFarSectorRadius,
            int requestedFarSamplesPerSector,
            int requestedMaximumFarMarkers)
        {
            var changed =
                galaxyAnchor != anchor ||
                stellarFrameLayer.value != frameLayer.value ||
                farHorizontalSectorRadius != requestedFarSectorRadius ||
                farSamplesPerSector != requestedFarSamplesPerSector ||
                maximumFarFieldMarkers != requestedMaximumFarMarkers;

            galaxyAnchor = anchor;
            stellarFrameLayer = frameLayer;
            farHorizontalSectorRadius = Mathf.Max(
                0,
                requestedFarSectorRadius);
            farVerticalSectorRadius = 2;
            farSamplesPerSector = Mathf.Clamp(
                requestedFarSamplesPerSector,
                1,
                8);
            maximumFarFieldMarkers = Mathf.Max(
                1,
                requestedMaximumFarMarkers);

            if (changed)
                ForceRefresh();
        }

        private void OnEnable()
        {
            galaxyAnchor ??= GetComponent<GalaxySpaceAnchor>();
        }

        private void OnValidate()
        {
            detailHorizontalSectorRadius = Mathf.Max(0, detailHorizontalSectorRadius);
            detailVerticalSectorRadius = Mathf.Max(0, detailVerticalSectorRadius);
            farHorizontalSectorRadius = Mathf.Max(0, farHorizontalSectorRadius);
            farVerticalSectorRadius = Mathf.Max(0, farVerticalSectorRadius);
            farSectorStride = Mathf.Max(1, farSectorStride);
            farSamplesPerSector = Mathf.Clamp(farSamplesPerSector, 1, 8);
            maximumDetailedMarkers = Mathf.Max(1, maximumDetailedMarkers);
            maximumFarFieldMarkers = Mathf.Max(1, maximumFarFieldMarkers);
            sectorsPerJob = Mathf.Max(1, sectorsPerJob);
            sectorsAppliedPerFrame = Mathf.Max(1, sectorsAppliedPerFrame);
            jobBatchSize = Mathf.Max(1, jobBatchSize);
            unityUnitsPerLightYear = Mathf.Max(0.0001f, unityUnitsPerLightYear);
            baseSystemMarkerDiameter = Mathf.Max(0.001f, baseSystemMarkerDiameter);
            farFieldMarkerDiameter = Mathf.Max(0.0001f, farFieldMarkerDiameter);
        }

        private void Update()
        {
            if (galaxyAnchor == null || !galaxyAnchor.HasResolvedGalaxy)
                return;

            if (_loadedGalaxySeed != galaxyAnchor.Galaxy.Seed)
            {
                ResetForGalaxy();
                _loadedGalaxySeed = galaxyAnchor.Galaxy.Seed;
            }

            CompletePendingGenerationIfReady();
            UpdateDetailedSectorStreaming();
            UpdateFarFieldIfNeeded();
            RebuildDetailedDrawDataIfNeeded();
            RenderDetailedMarkers();
            RenderFarField();
        }

        private void OnDisable()
        {
            CompleteAndDisposePendingGeneration();
            ClearStreamedObjects();
            ClearFarField();
            _loadedGalaxySeed = 0UL;
        }

        private void OnDestroy()
        {
            if (_runtimeFallbackMaterial != null)
                Destroy(_runtimeFallbackMaterial);
        }

        /// <summary>
        /// Rebuilds the current detailed sector set and far visual field.
        /// Call after changing streamer settings from code at runtime.
        /// </summary>
        public void ForceRefresh()
        {
            CompleteAndDisposePendingGeneration();
            ClearStreamedObjects();
            ClearFarField();

            _hasDetailedCenterCoordinates = false;
            _hasFarCenterCoordinates = false;
        }

        /// <summary>
        /// Overrides the anchor-system marker visibility at runtime without
        /// changing the serialized inspector setting. The seamless controller
        /// uses this to overlap galaxy and solar-system LODs instead of
        /// deleting one representation before the next one is ready.
        /// </summary>
        public void SetAnchorSolarSystemMarkerSuppressed(bool suppressed)
        {
            if (_hasAnchorSystemMarkerSuppressionOverride &&
                _suppressAnchorSystemMarkerAtRuntime == suppressed)
            {
                return;
            }

            _hasAnchorSystemMarkerSuppressionOverride = true;
            _suppressAnchorSystemMarkerAtRuntime = suppressed;
            _detailedDrawDataDirty = true;
        }

        public void ClearAnchorSolarSystemMarkerSuppressionOverride()
        {
            if (!_hasAnchorSystemMarkerSuppressionOverride)
                return;

            _hasAnchorSystemMarkerSuppressionOverride = false;
            _detailedDrawDataDirty = true;
        }

        private void ResetForGalaxy()
        {
            ForceRefresh();
        }

        private void UpdateDetailedSectorStreaming()
        {
            var centerCoordinates = GalaxySectorUtility.GetCoordinates(
                galaxyAnchor.GalaxyLocalPositionLightYears);

            if (!_hasDetailedCenterCoordinates ||
                !centerCoordinates.Equals(_lastDetailedCenterCoordinates))
            {
                _lastDetailedCenterCoordinates = centerCoordinates;
                _hasDetailedCenterCoordinates = true;

                RebuildDetailedSectorRequest(centerCoordinates);
            }

            if (!_hasPendingJob)
                ScheduleNextDetailedSectorBatch();
        }

        private void RebuildDetailedSectorRequest(int3 centerCoordinates)
        {
            var desired = BuildDetailedSectorSet(centerCoordinates);

            UnloadSectorsOutside(desired);

            _desiredDetailedSectors.Clear();
            foreach (var key in desired)
                _desiredDetailedSectors.Add(key);

            _detailSectorQueue.Clear();
            _queuedDetailedSectors.Clear();

            var missingCoordinates = new List<int3>();

            foreach (var key in _desiredDetailedSectors)
            {
                if (_sectorObjectIDs.ContainsKey(key) ||
                    _loadingDetailedSectors.Contains(key))
                {
                    continue;
                }

                missingCoordinates.Add(new int3(key.X, key.Y, key.Z));
            }

            missingCoordinates.Sort((left, right) =>
                GetSectorDistanceSquared(left, centerCoordinates).CompareTo(
                    GetSectorDistanceSquared(right, centerCoordinates)));

            for (var i = 0; i < missingCoordinates.Count; i++)
            {
                var coordinates = missingCoordinates[i];
                var key = new StreamingSectorKey(coordinates);

                _detailSectorQueue.Enqueue(coordinates);
                _queuedDetailedSectors.Add(key);
            }
        }

        private HashSet<StreamingSectorKey> BuildDetailedSectorSet(
            int3 centerCoordinates)
        {
            var desired = new HashSet<StreamingSectorKey>();
            var horizontalRadiusSquared =
                detailHorizontalSectorRadius * detailHorizontalSectorRadius;

            for (var z = -detailHorizontalSectorRadius;
                 z <= detailHorizontalSectorRadius;
                 z++)
            {
                for (var y = -detailVerticalSectorRadius;
                     y <= detailVerticalSectorRadius;
                     y++)
                {
                    for (var x = -detailHorizontalSectorRadius;
                         x <= detailHorizontalSectorRadius;
                         x++)
                    {
                        if (useCircularDetailFootprint &&
                            x * x + z * z > horizontalRadiusSquared)
                        {
                            continue;
                        }

                        var coordinates = centerCoordinates +
                                          new int3(x, y, z);

                        if (!SolarSystemIDUtility.IsSectorCoordinateInRange(
                                coordinates))
                        {
                            continue;
                        }

                        desired.Add(new StreamingSectorKey(coordinates));
                    }
                }
            }

            return desired;
        }

        private void ScheduleNextDetailedSectorBatch()
        {
            if (_detailSectorQueue.Count == 0)
                return;

            var batchCoordinates = new List<int3>(sectorsPerJob);

            while (_detailSectorQueue.Count > 0 &&
                   batchCoordinates.Count < sectorsPerJob)
            {
                var coordinates = _detailSectorQueue.Dequeue();
                var key = new StreamingSectorKey(coordinates);

                _queuedDetailedSectors.Remove(key);

                if (!_desiredDetailedSectors.Contains(key) ||
                    _sectorObjectIDs.ContainsKey(key) ||
                    _loadingDetailedSectors.Contains(key))
                {
                    continue;
                }

                batchCoordinates.Add(coordinates);
                _loadingDetailedSectors.Add(key);
            }

            if (batchCoordinates.Count == 0)
                return;

            _pendingSectorCoordinates = new NativeArray<int3>(
                batchCoordinates.Count,
                Allocator.Persistent);

            _pendingResults = new NativeArray<GalaxySectorData>(
                batchCoordinates.Count,
                Allocator.Persistent);

            for (var i = 0; i < batchCoordinates.Count; i++)
                _pendingSectorCoordinates[i] = batchCoordinates[i];

            _pendingResultIndex = 0;
            _pendingJobCompleted = false;

            _pendingJob = GalaxySectorBatchGenerator.Schedule(
                galaxyAnchor.Galaxy,
                _pendingSectorCoordinates,
                _pendingResults,
                jobBatchSize);

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

            var processedSectors = 0;

            while (_pendingResultIndex < _pendingResults.Length &&
                   processedSectors < sectorsAppliedPerFrame)
            {
                var sector = _pendingResults[_pendingResultIndex++];
                var key = new StreamingSectorKey(sector.Coordinates);

                _loadingDetailedSectors.Remove(key);

                if (_desiredDetailedSectors.Contains(key))
                    AddSector(sector);

                processedSectors++;
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
            if (_pendingSectorCoordinates.IsCreated)
                _pendingSectorCoordinates.Dispose();

            if (_pendingResults.IsCreated)
                _pendingResults.Dispose();

            _hasPendingJob = false;
            _pendingJobCompleted = false;
            _pendingResultIndex = 0;
        }

        private void AddSector(GalaxySectorData sector)
        {
            var key = new StreamingSectorKey(sector.Coordinates);

            if (_sectorObjectIDs.ContainsKey(key))
                return;

            var objectIDs = new List<ulong>(sector.SolarSystems.Length);

            for (var i = 0; i < sector.SolarSystems.Length; i++)
            {
                var solarSystem = sector.SolarSystems[i];

                objectIDs.Add(solarSystem.SolarSystemID);
                if (!_solarSystems.ContainsKey(
                        solarSystem.SolarSystemID))
                {
                    _solarSystems.Add(
                        solarSystem.SolarSystemID,
                        solarSystem);
                }
            }

            _sectorObjectIDs.Add(key, objectIDs);
            _detailedDrawDataDirty = true;
        }

        private void UnloadSectorsOutside(
            HashSet<StreamingSectorKey> desiredSectors)
        {
            var sectorsToUnload = new List<StreamingSectorKey>();

            foreach (var pair in _sectorObjectIDs)
            {
                if (!desiredSectors.Contains(pair.Key))
                    sectorsToUnload.Add(pair.Key);
            }

            for (var i = 0; i < sectorsToUnload.Count; i++)
                UnloadSector(sectorsToUnload[i]);
        }

        private void UnloadSector(StreamingSectorKey key)
        {
            if (!_sectorObjectIDs.TryGetValue(key, out var objectIDs))
                return;

            for (var i = 0; i < objectIDs.Count; i++)
                _solarSystems.Remove(objectIDs[i]);

            _sectorObjectIDs.Remove(key);
            _detailedDrawDataDirty = true;
        }

        private void ClearStreamedObjects()
        {
            _solarSystems.Clear();
            _sectorObjectIDs.Clear();
            _desiredDetailedSectors.Clear();
            _queuedDetailedSectors.Clear();
            _loadingDetailedSectors.Clear();
            _detailSectorQueue.Clear();

            _redDetailedSystems.Clear();
            _orangeDetailedSystems.Clear();
            _yellowDetailedSystems.Clear();
            _blueDetailedSystems.Clear();

            _redDetailedMatrices = Array.Empty<Matrix4x4[]>();
            _orangeDetailedMatrices = Array.Empty<Matrix4x4[]>();
            _yellowDetailedMatrices = Array.Empty<Matrix4x4[]>();
            _blueDetailedMatrices = Array.Empty<Matrix4x4[]>();

            _detailedDrawDataDirty = true;
        }

        private void RebuildDetailedDrawDataIfNeeded()
        {
            if (!_detailedDrawDataDirty)
                return;

            _redDetailedSystems.Clear();
            _orangeDetailedSystems.Clear();
            _yellowDetailedSystems.Clear();
            _blueDetailedSystems.Clear();

            var visibleSystemCount = 0;

            foreach (var solarSystem in _solarSystems.Values)
            {
                if (ShouldHideAnchorSolarSystemMarker(solarSystem))
                    continue;

                if (visibleSystemCount >= maximumDetailedMarkers)
                    break;

                AddDetailedSystemToColorGroup(solarSystem);
                visibleSystemCount++;
            }

            EnsureMatrixStorage(
                _redDetailedSystems.Count,
                ref _redDetailedMatrices);

            EnsureMatrixStorage(
                _orangeDetailedSystems.Count,
                ref _orangeDetailedMatrices);

            EnsureMatrixStorage(
                _yellowDetailedSystems.Count,
                ref _yellowDetailedMatrices);

            EnsureMatrixStorage(
                _blueDetailedSystems.Count,
                ref _blueDetailedMatrices);

            _detailedDrawDataDirty = false;
        }

        private bool ShouldHideAnchorSolarSystemMarker(
            SolarSystemLocationData solarSystem)
        {
            if (galaxyAnchor == null ||
                solarSystem.SolarSystemID !=
                galaxyAnchor.Coordinates.SolarSystemID)
            {
                return false;
            }

            return _hasAnchorSystemMarkerSuppressionOverride
                ? _suppressAnchorSystemMarkerAtRuntime
                : hideAnchorSolarSystem;
        }

        private void AddDetailedSystemToColorGroup(
            SolarSystemLocationData solarSystem)
        {
            var mass = solarSystem.EstimatedSystemMassSolarMasses;

            if (mass < 0.45)
            {
                _redDetailedSystems.Add(solarSystem);
                return;
            }

            if (mass < 0.85)
            {
                _orangeDetailedSystems.Add(solarSystem);
                return;
            }

            if (mass < 1.40)
            {
                _yellowDetailedSystems.Add(solarSystem);
                return;
            }

            _blueDetailedSystems.Add(solarSystem);
        }

        private void UpdateFarFieldIfNeeded()
        {
            var centerCoordinates = GalaxySectorUtility.GetCoordinates(
                galaxyAnchor.GalaxyLocalPositionLightYears);

            if (_hasFarCenterCoordinates &&
                centerCoordinates.Equals(_lastFarCenterCoordinates))
            {
                return;
            }

            _lastFarCenterCoordinates = centerCoordinates;
            _hasFarCenterCoordinates = true;

            RebuildFarField(centerCoordinates);
        }

        private void RebuildFarField(int3 centerCoordinates)
        {
            _farFieldMarkers.Clear();

            var horizontalRadiusSquared =
                farHorizontalSectorRadius * farHorizontalSectorRadius;

            for (var z = centerCoordinates.z - farHorizontalSectorRadius;
                 z <= centerCoordinates.z + farHorizontalSectorRadius;
                 z++)
            {
                var localZ = z - centerCoordinates.z;

                for (var y = centerCoordinates.y - farVerticalSectorRadius;
                     y <= centerCoordinates.y + farVerticalSectorRadius;
                     y++)
                {
                    for (var x = centerCoordinates.x -
                                 farHorizontalSectorRadius;
                         x <= centerCoordinates.x +
                              farHorizontalSectorRadius;
                         x++)
                    {
                        var localX = x - centerCoordinates.x;

                        if (useCircularFarFootprint &&
                            localX * localX + localZ * localZ >
                            horizontalRadiusSquared)
                        {
                            continue;
                        }

                        var sectorCoordinates = new int3(x, y, z);

                        if (!SolarSystemIDUtility.IsSectorCoordinateInRange(
                                sectorCoordinates))
                        {
                            continue;
                        }

                        // Keep an unstrided handoff area around the detailed
                        // region. This prevents a visible point from vanishing
                        // merely because the player crossed a LOD boundary.
                        if (!IsInsideFarHandoffFootprint(
                                sectorCoordinates,
                                centerCoordinates) &&
                            (!IsFarGridCoordinate(x) ||
                             !IsFarGridCoordinate(z)))
                        {
                            continue;
                        }

                        AddFarFieldSamples(sectorCoordinates);

                        if (_farFieldMarkers.Count >=
                            maximumFarFieldMarkers)
                        {
                            EnsureMatrixStorage(
                                _farFieldMarkers.Count,
                                ref _farFieldMatrices);

                            return;
                        }
                    }
                }
            }

            EnsureMatrixStorage(
                _farFieldMarkers.Count,
                ref _farFieldMatrices);
        }

        private void AddFarFieldSamples(int3 sectorCoordinates)
        {
            var sectorKey = new StreamingSectorKey(sectorCoordinates);
            var sectorSeed = SolarSystemIDUtility.GetGalaxySectorSeed(
                galaxyAnchor.Galaxy.Seed,
                sectorCoordinates);

            // Start from a stable pseudo-random slot so the sparse far view
            // samples different actual stars across adjacent sectors.
            var samplingRandom = new SpaceEngine.Runtime.Utils.QuantumRandom(
                GalaxyIDUtility.Combine(
                    sectorSeed,
                    FarFieldCandidateOrderSalt));

            var firstCandidateIndex = samplingRandom.NextInt(
                0,
                GalaxySectorData.MAXIMUM_SOLAR_SYSTEMS);

            var maximumSamples = Mathf.Clamp(
                (int)(farSamplesPerSector * farFieldDensityMultiplier),
                1,
                GalaxySectorData.MAXIMUM_SOLAR_SYSTEMS);

            var addedSamples = 0;

            for (var offset = 0;
                 offset < GalaxySectorData.MAXIMUM_SOLAR_SYSTEMS &&
                 addedSamples < maximumSamples;
                 offset++)
            {
                if (_farFieldMarkers.Count >= maximumFarFieldMarkers)
                    return;

                var candidateIndex = (byte)(
                    (firstCandidateIndex + offset) %
                    GalaxySectorData.MAXIMUM_SOLAR_SYSTEMS);

                var candidate = GalaxySectorGenerator.GenerateCandidate(
                    galaxyAnchor.Galaxy,
                    sectorCoordinates,
                    candidateIndex);

                if (!candidate.IsPresent)
                    continue;

                _farFieldMarkers.Add(new FarFieldMarkerData(
                    sectorKey,
                    candidate.Location
                        .GalaxyLocalPositionLightYears));

                addedSamples++;
            }
        }

        private bool IsInsideFarHandoffFootprint(
            int3 sectorCoordinates,
            int3 centerCoordinates)
        {
            var delta = sectorCoordinates - centerCoordinates;

            var horizontalRadius = detailHorizontalSectorRadius + 1;
            var verticalRadius = detailVerticalSectorRadius + 1;

            if (math.abs(delta.y) > verticalRadius ||
                math.abs(delta.x) > horizontalRadius ||
                math.abs(delta.z) > horizontalRadius)
            {
                return false;
            }

            if (!useCircularDetailFootprint)
                return true;

            return delta.x * delta.x + delta.z * delta.z <=
                   horizontalRadius * horizontalRadius;
        }

        private bool IsInsideDetailedFootprint(
            int3 sectorCoordinates,
            int3 centerCoordinates)
        {
            var delta = sectorCoordinates - centerCoordinates;

            if (math.abs(delta.y) > detailVerticalSectorRadius ||
                math.abs(delta.x) > detailHorizontalSectorRadius ||
                math.abs(delta.z) > detailHorizontalSectorRadius)
            {
                return false;
            }

            if (!useCircularDetailFootprint)
                return true;

            return delta.x * delta.x + delta.z * delta.z <=
                   detailHorizontalSectorRadius *
                   detailHorizontalSectorRadius;
        }

        private bool IsFarGridCoordinate(int coordinate)
        {
            return coordinate % farSectorStride == 0;
        }

        private void RenderDetailedMarkers()
        {
            var mesh = ResolveMarkerMesh();
            var material = ResolveDetailedMarkerMaterial();

            if (mesh == null || material == null)
                return;

            _detailedPropertyBlock ??= new MaterialPropertyBlock();

            var anchorPosition =
                galaxyAnchor.GalaxyLocalPositionLightYears;

            RenderDetailedColorGroup(
                _redDetailedSystems,
                ref _redDetailedMatrices,
                new Color(1f, 0.32f, 0.20f),
                mesh,
                material,
                anchorPosition);

            RenderDetailedColorGroup(
                _orangeDetailedSystems,
                ref _orangeDetailedMatrices,
                new Color(1f, 0.64f, 0.25f),
                mesh,
                material,
                anchorPosition);

            RenderDetailedColorGroup(
                _yellowDetailedSystems,
                ref _yellowDetailedMatrices,
                new Color(1f, 0.92f, 0.55f),
                mesh,
                material,
                anchorPosition);

            RenderDetailedColorGroup(
                _blueDetailedSystems,
                ref _blueDetailedMatrices,
                new Color(0.60f, 0.82f, 1f),
                mesh,
                material,
                anchorPosition);
        }

        private void RenderDetailedColorGroup(
            List<SolarSystemLocationData> solarSystems,
            ref Matrix4x4[][] matrixBatches,
            Color color,
            Mesh mesh,
            Material material,
            double3 anchorPosition)
        {
            if (solarSystems.Count == 0)
                return;

            _detailedPropertyBlock.Clear();
            _detailedPropertyBlock.SetColor("_Color", color);
            _detailedPropertyBlock.SetColor("_BaseColor", color);
            _detailedPropertyBlock.SetColor(
                "_EmissionColor",
                color * color.a);

            var index = 0;

            for (var batchIndex = 0;
                 batchIndex < matrixBatches.Length;
                 batchIndex++)
            {
                var matrices = matrixBatches[batchIndex];
                var count = Mathf.Min(
                    MaximumInstancesPerDrawCall,
                    solarSystems.Count - index);

                for (var i = 0; i < count; i++)
                {
                    var solarSystem = solarSystems[index + i];
                    var relativePosition =
                        solarSystem.GalaxyLocalPositionLightYears -
                        anchorPosition;

                    matrices[i] = Matrix4x4.TRS(
                        ToUnityPosition(relativePosition),
                        Quaternion.identity,
                        Vector3.one * GetSystemMarkerDiameter(
                            solarSystem.EstimatedSystemMassSolarMasses));
                }

                Graphics.DrawMeshInstanced(
                    mesh,
                    0,
                    material,
                    matrices,
                    count,
                    _detailedPropertyBlock,
                    ShadowCastingMode.Off,
                    false,
                    ReferenceFrameLayerUtility
                        .GetSingleLayerIndexOrDefault(stellarFrameLayer),
                    null,
                    LightProbeUsage.Off,
                    null);

                index += count;
            }
        }

        private void RenderFarField()
        {
            if (_farFieldMarkers.Count == 0)
                return;

            var mesh = ResolveMarkerMesh();
            var material = ResolveFarFieldMaterial();

            if (mesh == null || material == null)
                return;

            _farFieldPropertyBlock ??= new MaterialPropertyBlock();
            _farFieldPropertyBlock.Clear();
            _farFieldPropertyBlock.SetColor("_Color", farFieldColor);
            _farFieldPropertyBlock.SetColor("_BaseColor", farFieldColor);
            _farFieldPropertyBlock.SetColor(
                "_EmissionColor",
                farFieldColor * farFieldColor.a);

            var anchorPosition =
                galaxyAnchor.GalaxyLocalPositionLightYears;

            // A proxy remains visible while its real detailed sector is being
            // generated. It is hidden only after that sector has arrived, so
            // the detail marker takes over at the identical coordinates.
            var visibleMarkerCount = 0;

            for (var i = 0; i < _farFieldMarkers.Count; i++)
            {
                var marker = _farFieldMarkers[i];

                if (_sectorObjectIDs.ContainsKey(marker.SectorKey))
                    continue;

                var batchIndex =
                    visibleMarkerCount / MaximumInstancesPerDrawCall;

                var instanceIndex =
                    visibleMarkerCount % MaximumInstancesPerDrawCall;

                var relativePosition =
                    marker.PositionLightYears - anchorPosition;

                _farFieldMatrices[batchIndex][instanceIndex] =
                    Matrix4x4.TRS(
                        ToUnityPosition(relativePosition),
                        Quaternion.identity,
                        Vector3.one * farFieldMarkerDiameter);

                visibleMarkerCount++;
            }

            var renderedMarkerCount = 0;

            for (var batchIndex = 0;
                 batchIndex < _farFieldMatrices.Length &&
                 renderedMarkerCount < visibleMarkerCount;
                 batchIndex++)
            {
                var count = Mathf.Min(
                    MaximumInstancesPerDrawCall,
                    visibleMarkerCount - renderedMarkerCount);

                Graphics.DrawMeshInstanced(
                    mesh,
                    0,
                    material,
                    _farFieldMatrices[batchIndex],
                    count,
                    _farFieldPropertyBlock,
                    ShadowCastingMode.Off,
                    false,
                    ReferenceFrameLayerUtility
                        .GetSingleLayerIndexOrDefault(stellarFrameLayer),
                    null,
                    LightProbeUsage.Off,
                    null);

                renderedMarkerCount += count;
            }
        }

        private static void EnsureMatrixStorage(
            int instanceCount,
            ref Matrix4x4[][] matrixBatches)
        {
            var requiredBatchCount =
                (instanceCount + MaximumInstancesPerDrawCall - 1) /
                MaximumInstancesPerDrawCall;

            if (matrixBatches.Length == requiredBatchCount)
                return;

            matrixBatches = new Matrix4x4[requiredBatchCount][];

            for (var i = 0; i < requiredBatchCount; i++)
                matrixBatches[i] =
                    new Matrix4x4[MaximumInstancesPerDrawCall];
        }

        private void ClearFarField()
        {
            _farFieldMarkers.Clear();
            _farFieldMatrices = Array.Empty<Matrix4x4[]>();
        }

        private Material ResolveDetailedMarkerMaterial()
        {
            var material = markerMaterial != null
                ? markerMaterial
                : ResolveFallbackMaterial();

            if (material != null)
                material.enableInstancing = true;

            return material;
        }

        private Material ResolveFarFieldMaterial()
        {
            var material = farFieldMaterial != null
                ? farFieldMaterial
                : ResolveDetailedMarkerMaterial();

            if (material != null)
                material.enableInstancing = true;

            return material;
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

            _runtimeFallbackMaterial = new Material(shader)
            {
                enableInstancing = true
            };

            return _runtimeFallbackMaterial;
        }

        private Mesh ResolveMarkerMesh()
        {
            if (markerMesh != null)
                return markerMesh;

            if (_runtimeFallbackMarkerMesh != null)
                return _runtimeFallbackMarkerMesh;

            var temporaryObject = GameObject.CreatePrimitive(
                PrimitiveType.Sphere);

            temporaryObject.hideFlags = HideFlags.HideAndDontSave;

            _runtimeFallbackMarkerMesh =
                temporaryObject.GetComponent<MeshFilter>().sharedMesh;

            if (Application.isPlaying)
                Destroy(temporaryObject);
            else
                DestroyImmediate(temporaryObject);

            return _runtimeFallbackMarkerMesh;
        }

        private Vector3 ToUnityPosition(double3 relativePositionLightYears)
        {
            return new Vector3(
                (float)(relativePositionLightYears.x *
                        unityUnitsPerLightYear),
                (float)(relativePositionLightYears.y *
                        unityUnitsPerLightYear),
                (float)(relativePositionLightYears.z *
                        unityUnitsPerLightYear));
        }

        private float GetSystemMarkerDiameter(
            double estimatedSystemMassSolarMasses)
        {
            var massScale = Mathf.Sqrt(
                Mathf.Max(
                    0.05f,
                    (float)estimatedSystemMassSolarMasses));

            return baseSystemMarkerDiameter *
                   Mathf.Clamp(massScale, 0.45f, 2.0f);
        }

        private static int GetSectorDistanceSquared(
            int3 left,
            int3 right)
        {
            var delta = left - right;

            return delta.x * delta.x +
                   delta.y * delta.y +
                   delta.z * delta.z;
        }
    }
}
