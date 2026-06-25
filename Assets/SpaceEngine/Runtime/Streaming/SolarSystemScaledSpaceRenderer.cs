using System;
using System.Collections.Generic;
using SpaceEngine.Runtime.Data.Planet;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Generation.SolarSystem;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Runtime.Streaming
{
    /// <summary>
    /// Renders stars and planets from the active solar system in a separate
    /// scaled reference frame. Radii and positions use the same conversion,
    /// therefore their angular sizes remain physically correct once the
    /// minimum far-distance proxy size is no longer needed.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SeamlessSpaceStreamingController))]
    public sealed class SolarSystemScaledSpaceRenderer : MonoBehaviour
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
            public double3 BarycentricPositionMeters;
        }

        /// <summary>
        /// Runtime-only visual that replaces the simple stellar sphere while
        /// the ship is close enough for surface detail to be meaningful.
        /// The root is parented to the same scaled-space star transform, so
        /// its radius and position always remain identical to the real star.
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

        [Header("References")]
        [SerializeField, HideInInspector] private SeamlessSpaceAnchor spaceAnchor;
        [SerializeField, HideInInspector] private Transform visualRoot;
        [SerializeField, HideInInspector] private Mesh sphereMesh;
        [SerializeField, HideInInspector] private Material starMaterial;
        [SerializeField, HideInInspector] private Material planetMaterial;
        [SerializeField, HideInInspector] private Material coronaMaterial;

        [Header("Reference-frame layer")]
        [SerializeField, HideInInspector] private LayerMask scaledSpaceLayer = 0;
        [SerializeField, HideInInspector, Min(1.0f)]
        private double scaledSpaceMetersPerUnityUnit = 1_000_000_000.0;

        [Header("Far-to-close star LOD")]
        [SerializeField, HideInInspector, Range(0.001f, 2.0f)]
        private float minimumStarAngularDiameterDegrees = 0.06f;
        [SerializeField, HideInInspector, Min(1.0f)]
        private float coronaRadiusMultiplier = 1.12f;
        [SerializeField, HideInInspector, Min(1.0f)]
        private double closeStarSurfaceActivationDistanceInRadii = 64.0;
        [SerializeField, HideInInspector, Min(1.0f)]
        private double closeStarSurfaceDeactivationDistanceInRadii = 80.0;

        [Header("Planet proxy visibility")]
        [SerializeField, HideInInspector, Range(0.0f, 1.0f)]
        private float minimumPlanetAngularDiameterDegrees = 0.004f;

        [Header("Simulation")]
        [SerializeField, HideInInspector] private double simulationTimeScale = 1.0;

        private readonly List<StarVisual> _stars = new();
        private readonly List<PlanetVisual> _planets = new();

        private SolarSystemData _solarSystem;
        private ulong _loadedSystemSeed;
        private double _simulationTimeSeconds;
        private double _totalStarMassKg;
        private bool _isVisible;
        private bool _isNearStarSurface;

        private Material _runtimeFallbackMaterial;
        private Mesh _runtimeSphereMesh;
        private Mesh _runtimeCloseStarSurfaceMesh;
        private CloseStarSurfaceVisual _closeStarSurfaceVisual;

        /// <summary>
        /// Fires when the active ship enters or leaves the close-star range.
        /// A future local plasma/surface renderer can subscribe here without
        /// replacing the solar-system data or changing coordinate frames.
        /// </summary>
        public event Action<bool> StarSurfaceLodChanged;

        public bool IsVisible => _isVisible;

        public bool IsNearStarSurface => _isNearStarSurface;

        public ulong LoadedSolarSystemID =>
            spaceAnchor != null && spaceAnchor.IsConfigured
                ? spaceAnchor.Coordinates.SolarSystemID
                : 0UL;

        internal void Configure(
            SeamlessSpaceAnchor anchor,
            LayerMask frameLayer,
            float minimumStarAngularDiameter,
            double closeSurfaceActivationDistanceInRadii,
            double closeSurfaceDeactivationDistanceInRadii)
        {
            spaceAnchor = anchor;
            scaledSpaceLayer = frameLayer;
            minimumStarAngularDiameterDegrees = Mathf.Max(
                0.001f,
                minimumStarAngularDiameter);
            closeStarSurfaceActivationDistanceInRadii = Math.Max(
                1.0,
                closeSurfaceActivationDistanceInRadii);
            closeStarSurfaceDeactivationDistanceInRadii = Math.Max(
                closeStarSurfaceActivationDistanceInRadii,
                closeSurfaceDeactivationDistanceInRadii);

            EnsureVisualRoot();

            if (visualRoot != null)
            {
                SetLayerRecursively(
                    visualRoot.gameObject,
                    ReferenceFrameLayerUtility
                        .GetSingleLayerIndexOrDefault(scaledSpaceLayer));
            }
        }

        private void Awake()
        {
            spaceAnchor ??= GetComponent<SeamlessSpaceAnchor>();
            EnsureVisualRoot();
        }

        private void OnEnable()
        {
            EnsureVisualRoot();
            SetScaledSpaceVisible(_isVisible);
        }

        private void Update()
        {
            if (!_isVisible || spaceAnchor == null ||
                !spaceAnchor.IsConfigured)
            {
                return;
            }

            _simulationTimeSeconds += Time.deltaTime * simulationTimeScale;

            EnsureSystemData();
            UpdateBodyTransforms();
        }

        private void OnDestroy()
        {
            ClearVisuals();

            if (_runtimeFallbackMaterial != null)
                Destroy(_runtimeFallbackMaterial);

            if (_runtimeCloseStarSurfaceMesh != null)
                Destroy(_runtimeCloseStarSurfaceMesh);
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
                UpdateBodyTransforms();
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

            return visual;
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

        private void UpdateBodyTransforms()
        {
            if (_totalStarMassKg <= 0.0)
                return;

            var gravitationalParameter =
                SolarSystemOrbitUtility.GravitationalConstant *
                _totalStarMassKg;

            var nearestStarIndex = -1;
            var nearestStarDistanceMeters = double.PositiveInfinity;

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

            var nearStarSurface = UpdateCloseStarSurfaceLod(
                nearestStarIndex,
                nearestStarDistanceMeters);

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
            var minimumRadius = GetMinimumAngularRadiusMeters(
                distanceToCentreMeters,
                minimumStarAngularDiameterDegrees);

            return Math.Max(physicalRadiusMeters, minimumRadius);
        }

        private double GetPlanetRenderRadiusMeters(
            double physicalRadiusMeters,
            double distanceToCentreMeters)
        {
            if (minimumPlanetAngularDiameterDegrees <= 0.0f)
                return physicalRadiusMeters;

            var minimumRadius = GetMinimumAngularRadiusMeters(
                distanceToCentreMeters,
                minimumPlanetAngularDiameterDegrees);

            return Math.Max(physicalRadiusMeters, minimumRadius);
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

            return (float)(
                2.0 * Math.Atan(radiusMeters / distanceToCentreMeters) *
                180.0 / Math.PI);
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


        private bool UpdateCloseStarSurfaceLod(
            int nearestStarIndex,
            double nearestDistanceMeters)
        {
            if (nearestStarIndex < 0 ||
                nearestStarIndex >= _stars.Count)
            {
                DeactivateCloseStarSurfaceLod(destroyVisual: false);
                return false;
            }

            var star = _stars[nearestStarIndex];
            var closeSurfaceIsAlreadyActive =
                _closeStarSurfaceVisual != null &&
                _closeStarSurfaceVisual.StarIndex == nearestStarIndex &&
                _closeStarSurfaceVisual.Root != null &&
                _closeStarSurfaceVisual.Root.gameObject.activeSelf;

            var distanceLimitInRadii = closeSurfaceIsAlreadyActive
                ? closeStarSurfaceDeactivationDistanceInRadii
                : closeStarSurfaceActivationDistanceInRadii;

            if (star.Data.Type == StarType.BlackHole ||
                star.Data.RadiusMeters <= 0.0 ||
                nearestDistanceMeters >
                star.Data.RadiusMeters * distanceLimitInRadii)
            {
                DeactivateCloseStarSurfaceLod(destroyVisual: false);
                return false;
            }

            EnsureCloseStarSurfaceVisual(nearestStarIndex, star);
            UpdateCloseStarSurfaceMaterials(star.Data, nearestStarIndex);

            return _closeStarSurfaceVisual != null &&
                   _closeStarSurfaceVisual.Root != null &&
                   _closeStarSurfaceVisual.Root.gameObject.activeSelf;
        }

        private void EnsureCloseStarSurfaceVisual(
            int starIndex,
            StarVisual star)
        {
            if (_closeStarSurfaceVisual != null &&
                _closeStarSurfaceVisual.StarIndex == starIndex &&
                _closeStarSurfaceVisual.Root != null)
            {
                if (_closeStarSurfaceVisual.Root.parent != star.Transform)
                {
                    _closeStarSurfaceVisual.Root.SetParent(
                        star.Transform,
                        worldPositionStays: false);
                }

                _closeStarSurfaceVisual.Root.gameObject.SetActive(true);
                SetBaseStarVisualVisible(star, false);
                return;
            }

            DeactivateCloseStarSurfaceLod(destroyVisual: true);

            var layer = ReferenceFrameLayerUtility
                .GetSingleLayerIndexOrDefault(scaledSpaceLayer);

            var rootObject = new GameObject("Close Star Surface LOD")
            {
                layer = layer
            };

            var root = rootObject.transform;
            root.SetParent(star.Transform, false);
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
                        Destroy(mesh);
                    else
                        DestroyImmediate(mesh);
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

            const int longitudeSegments = 128;
            const int latitudeSegments = 64;

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
                name = "Runtime Close Star Surface Sphere",
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
            if (visualRoot != null && visualRoot != transform)
                return;

            var root = new GameObject("Solar System Scaled Space");
            root.transform.SetParent(transform, false);
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
            DeactivateCloseStarSurfaceLod(destroyVisual: true);

            for (var i = 0; i < _stars.Count; i++)
            {
                DestroyVisualObject(_stars[i].Transform);
                DestroyMaterial(_stars[i].Material);
                DestroyMaterial(_stars[i].CoronaMaterial);
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

        private static void DestroyVisualObject(Transform body)
        {
            if (body == null)
                return;

            if (Application.isPlaying)
                Destroy(body.gameObject);
            else
                DestroyImmediate(body.gameObject);
        }

        private static void DestroyMaterial(Material material)
        {
            if (material == null)
                return;

            if (Application.isPlaying)
                Destroy(material);
            else
                DestroyImmediate(material);
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
                Destroy(temporaryObject);
            else
                DestroyImmediate(temporaryObject);

            return _runtimeSphereMesh;
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
}
