using System;
using System.Collections.Generic;
using SpaceEngine.Runtime.Content;
using SpaceEngine.Rendering.Content;
using SpaceEngine.Rendering.Content.StellarObjects.Renderers.Galaxies;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Galaxy;
using SpaceEngine.Runtime.Generation.Universe;
using SpaceEngine.Runtime.Streaming;
using SpaceEngine.Runtime.Streaming.Runtime.Anchors;
using SpaceEngine.Runtime.Utils;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Rendering.Runtime.Universe
{
    public sealed class UniverseGalaxyFieldRenderer
    {
        private static readonly int GalaxyColor = Shader.PropertyToID("_GalaxyColor");
        private static readonly int GalaxyShape = Shader.PropertyToID("_GalaxyShape");
        private static readonly int GalaxyStructure = Shader.PropertyToID("_GalaxyStructure");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Color1 = Shader.PropertyToID("_Color");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int Intensity = Shader.PropertyToID("_Intensity");
        private static readonly int Softness = Shader.PropertyToID("_Softness");
        private static readonly int Cull = Shader.PropertyToID("_Cull");
        private const int MAXIMUM_INSTANCES_PER_DRAW_CALL = 1023;

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
            public readonly GalaxyVisualData VisualData;
            public readonly List<ExternalStarSample> Samples = new();
            public Matrix4x4[][] Matrices = Array.Empty<Matrix4x4[]>();

            public LoadedExternalGalaxy(
                GalaxyProxy proxy,
                GalaxyData data,
                GalaxyVisualData visualData)
            {
                Proxy = proxy;
                Data = data;
                VisualData = visualData;
            }
        }

        private SeamlessSpaceAnchor spaceAnchor;
        private SpaceEngineConfiguration configuration;
        private CelestialRenderConfiguration renderConfiguration;
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
            SpaceEngineConfiguration contentConfiguration,
            CelestialRenderConfiguration visualConfiguration,
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
                configuration != contentConfiguration ||
                renderConfiguration != visualConfiguration ||
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
            configuration = contentConfiguration;
            renderConfiguration = visualConfiguration;
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

                        var sector = SpaceEngine.Runtime.Generation.Universe.UniverseGeneration.GenerateSector(
                            configuration.GalaxyGenerators,
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

                        var sector = SpaceEngine.Runtime.Generation.Universe.UniverseGeneration.GenerateSector(
                            configuration.GalaxyGenerators,
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
                    markerCount / MAXIMUM_INSTANCES_PER_DRAW_CALL;
                var markerIndex =
                    markerCount % MAXIMUM_INSTANCES_PER_DRAW_CALL;
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
                if (loaded == null)
                    continue;

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

            var galaxy = SpaceEngine.Runtime.Generation.Universe.UniverseGeneration.GenerateGalaxy(
                configuration.GalaxyGenerators,
                spaceAnchor.Coordinates.UniverseID,
                proxy.Location.GalaxyID,
                proxy.Location.UniversePositionLightYears);
            var renderer =
                ContentRendererSelection.SelectGalaxyRendererOrNull(
                    renderConfiguration.GalaxyRenderers,
                    galaxy.Entity);
            if (renderer == null)
                return null;

            loaded = new LoadedExternalGalaxy(
                proxy,
                galaxy,
                renderer.GetVisualData(galaxy));
            CreateExternalGalaxyStarSamples(
                galaxy,
                renderer,
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
            var color = loaded.VisualData.ExternalFogColor;
            color *= brightnessMultiplier;
            color.a = candidate.Fade;

            _externalGalaxyFogPropertyBlock ??=
                new MaterialPropertyBlock();
            _externalGalaxyFogPropertyBlock.Clear();
            _externalGalaxyFogPropertyBlock.SetColor(
                GalaxyColor,
                color);
            _externalGalaxyFogPropertyBlock.SetVector(
                GalaxyShape,
                new Vector4(
                    loaded.VisualData.ShaderMorphology,
                    Mathf.Max(1.0f, loaded.VisualData.SpiralArmCount),
                    Mathf.Max(0.0f, loaded.VisualData.SpiralArmTightness),
                    1.0f));
            _externalGalaxyFogPropertyBlock.SetVector(
                GalaxyStructure,
                new Vector4(
                    Mathf.Clamp(
                        (float)(galaxy.CoreRadiusLightYears /
                                radiusLightYears),
                        0.025f,
                        0.85f),
                    Mathf.Clamp(
                        loaded.VisualData.BarLengthRadiusMultiplier,
                        0.001f,
                        1.5f),
                    Mathf.Clamp(
                        loaded.VisualData.RingRadiusMultiplier,
                        0.005f,
                        1.5f),
                    Mathf.Clamp(
                        loaded.VisualData.RingWidthRadiusMultiplier,
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

                var batchIndex = visibleCount / MAXIMUM_INSTANCES_PER_DRAW_CALL;
                var instanceIndex =
                    visibleCount % MAXIMUM_INSTANCES_PER_DRAW_CALL;
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

            var color = loaded.VisualData.ExternalStarfieldColor *
                        (brightnessMultiplier * candidate.Fade);
            _externalStarfieldPropertyBlock.SetColor(Color1, color);
            _externalStarfieldPropertyBlock.SetColor(BaseColor, color);
            _externalStarfieldPropertyBlock.SetColor(
                EmissionColor,
                color);
            _externalStarfieldPropertyBlock.SetFloat(
                Intensity,
                0.65f);
            _externalStarfieldPropertyBlock.SetFloat(Softness, 2.0f);

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
            GalaxyRenderer renderer,
            int sampleCount,
            List<ExternalStarSample> samples)
        {
            samples.Clear();

            var random = new QuantumRandom(
                GalaxyIDUtility.Combine(
                    galaxy.Seed,
                    0x4558545F47414CUL));

            for (var sampleIndex = 0;
                 sampleIndex < sampleCount;
                 sampleIndex++)
            {
                if (!renderer.TryCreateExternalStarSample(
                        galaxy,
                        ref random,
                        out var sample))
                {
                    continue;
                }

                samples.Add(new ExternalStarSample(
                    sample.GalaxyLocalPositionLightYears,
                    sample.Brightness));
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

            _markerPropertyBlock.SetColor(Color1, color);
            _markerPropertyBlock.SetColor(BaseColor, color);
            _markerPropertyBlock.SetColor(EmissionColor, color);
            _markerPropertyBlock.SetFloat(Intensity, 0.9f);
            _markerPropertyBlock.SetFloat(Softness, 2.0f);

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
                    MAXIMUM_INSTANCES_PER_DRAW_CALL,
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
                    LightProbeUsage.Off);

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

            if (_runtimeMarkerMaterial.HasProperty(Cull))
                _runtimeMarkerMaterial.SetFloat(Cull, 0.0f);

            if (_runtimeMarkerMaterial.HasProperty(Intensity))
                _runtimeMarkerMaterial.SetFloat(Intensity, 0.9f);

            if (_runtimeMarkerMaterial.HasProperty(Softness))
                _runtimeMarkerMaterial.SetFloat(Softness, 2.0f);

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
                (instanceCount + MAXIMUM_INSTANCES_PER_DRAW_CALL - 1) /
                MAXIMUM_INSTANCES_PER_DRAW_CALL;

            if (matrices.Length == requiredBatchCount)
                return;

            matrices = new Matrix4x4[requiredBatchCount][];

            for (var i = 0; i < requiredBatchCount; i++)
                matrices[i] = new Matrix4x4[MAXIMUM_INSTANCES_PER_DRAW_CALL];
        }

        private static void EnsureVectorStorage(
            int instanceCount,
            ref Vector4[][] vectors)
        {
            var requiredBatchCount =
                (instanceCount + MAXIMUM_INSTANCES_PER_DRAW_CALL - 1) /
                MAXIMUM_INSTANCES_PER_DRAW_CALL;

            if (vectors.Length == requiredBatchCount)
                return;

            vectors = new Vector4[requiredBatchCount][];

            for (var i = 0; i < requiredBatchCount; i++)
                vectors[i] = new Vector4[MAXIMUM_INSTANCES_PER_DRAW_CALL];
        }
    }
}
