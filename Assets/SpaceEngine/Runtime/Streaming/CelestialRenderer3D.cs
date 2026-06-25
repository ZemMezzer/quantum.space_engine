using System;
using System.Collections;
using System.Reflection;
using SpaceEngine.Runtime.Core;
using UnityEngine;
using UnityEngine.Serialization;

namespace SpaceEngine.Runtime.Streaming
{
    public enum CelestialRendererQuality
    {
        Low,
        Balanced,
        High
    }

    /// <summary>
    /// Concrete 3D celestial renderer. SpaceEngine drives this renderer;
    /// this component only holds rendering settings and the camera stack.
    /// </summary>
    [DisallowMultipleComponent]
    public class CelestialRenderer3D : CelestialRenderer
    {
        [Header("Required")]
        [SerializeField] private Camera playerCamera;

        [Header("Single celestial camera")]
        [Tooltip(
            "All astronomical rendering uses this one layer and one camera. " +
            "Keep the gameplay world on the player camera's normal layers.")]
        [SerializeField]
        [FormerlySerializedAs("solarFrameLayer")]
        private LayerMask celestialFrameLayer;

        [Tooltip(
            "Far clipping range of the single celestial camera. It is in " +
            "scaled-space units, not gameplay metres.")]
        [SerializeField, Min(1_000.0f)]
        [FormerlySerializedAs("solarFrameFarClipPlane")]
        private float celestialFrameFarClipPlane = 2_000_000.0f;

        [SerializeField, HideInInspector]
        [FormerlySerializedAs("solarFrameCamera")]
        private Camera celestialFrameCamera;

        [SerializeField, HideInInspector] private Transform celestialRoot;

        [Header("Quality")]
        [SerializeField] private CelestialRendererQuality quality =
            CelestialRendererQuality.Balanced;

        [Header("Star LOD transitions")]
        [SerializeField, Min(0.25f)]
        private float lod0MinimumPointDiameterPixels = 3.0f;

        [SerializeField, Min(0.000001f)]
        private double lod0ToLod1ActivationDistanceLightYears = 0.02;

        [SerializeField, Min(0.000001f)]
        private double lod0ToLod1DeactivationDistanceLightYears = 0.03;

        [SerializeField, Range(0.001f, 5.0f)]
        private float lod1MinimumStarAngularDiameterDegrees = 0.20f;

        [SerializeField, Range(0.001f, 10.0f)]
        private float lod0HideAfterLod1AngularDiameterDegrees = 0.35f;

        [Header("Star LOD 1 light")]
        [SerializeField, Range(1.0f, 64.0f)]
        private float lod1StarLightRadiusMultiplier = 10.0f;

        [SerializeField, Range(0.0f, 32.0f)]
        private float lod1StarLightIntensity = 8.0f;

        [SerializeField, Range(0.0f, 3.0f)]
        private float lod1StarLightRayStrength = 0.65f;

        [SerializeField, Range(4.0f, 32.0f)]
        private float lod1StarLightRayCount = 8.0f;

        [Header("Star LOD 2 detailed surface")]
        [SerializeField]
        [FormerlySerializedAs("enableStarLod3LocalSurface")]
        private bool enableStarLod2LocalSurface = true;

        [SerializeField, Min(1.0f)]
        [FormerlySerializedAs("lod3StarSurfaceActivationDistanceInStarRadii")]
        private double lod2StarSurfaceActivationDistanceInStarRadii = 64.0;

        [SerializeField, Min(1.0f)]
        [FormerlySerializedAs("lod3StarSurfaceDeactivationDistanceInStarRadii")]
        private double lod2StarSurfaceDeactivationDistanceInStarRadii = 80.0;

        [Header("Planet LOD 2 detailed surface")]
        [SerializeField]
        [FormerlySerializedAs("enablePlanetLod3LocalSurface")]
        private bool enablePlanetLod2LocalSurface = true;

        [SerializeField, Min(1.0f)]
        [FormerlySerializedAs("lod3PlanetSurfaceActivationDistanceInRadii")]
        private double lod2PlanetSurfaceActivationDistanceInRadii = 24.0;

        [SerializeField, Min(1.0f)]
        [FormerlySerializedAs("lod3PlanetSurfaceDeactivationDistanceInRadii")]
        private double lod2PlanetSurfaceDeactivationDistanceInRadii = 32.0;

        [Header("Planet proxy visibility")]
        [SerializeField, Range(0.0f, 1.0f)]
        private float minimumPlanetAngularDiameterDegrees = 0.004f;

        [Header("Distant galaxy point field")]
        [Tooltip(
            "Horizontal universe-sector radius used only for the cheapest " +
            "galaxy LOD. Every discovered remote galaxy is rendered as one " +
            "star-sized pixel until its real fog and starfield are preloaded.")]
        [SerializeField, Range(1, 8)]
        private int distantGalaxyPointHorizontalSectorRadius = 3;

        [Tooltip(
            "Vertical universe-sector radius used by the distant galaxy " +
            "point field. Increase this together with the horizontal radius " +
            "when the space outside a galaxy looks too empty.")]
        [SerializeField, Range(0, 4)]
        private int distantGalaxyPointVerticalSectorRadius = 2;

        [Tooltip(
            "Maximum number of cheap distant galaxy pixels kept in view. " +
            "This affects only LOD 0 points, not the number of foggy galaxies " +
            "that can be preloaded at once.")]
        [SerializeField, Range(64, 4_096)]
        private int maximumDistantGalaxyPoints = 2_048;

        [Header("External galaxy preload")]
        [Tooltip(
            "Pixel diameter of a distant galaxy marker. Remote galaxies stay " +
            "star-sized until their generated starfield is preloaded.")]
        [SerializeField, Min(0.25f)]
        [FormerlySerializedAs("galaxyDistantMarkerDiameterPixels")]
        private float galaxyLod0MinimumPointDiameterPixels = 3.0f;

        // Retained only to preserve existing serialized scenes. Distant
        // galaxies now stay one constant star-sized point until a real
        // preloaded external visual takes over.
        [SerializeField, HideInInspector]
        [FormerlySerializedAs("galaxyNearMarkerDiameterPixels")]
        private float galaxyLod0NearPointDiameterPixels = 0.35f;

        [SerializeField, HideInInspector]
        [FormerlySerializedAs("galaxyMarkerShrinkCompleteAtDiameterPixels")]
        private float galaxyLod0ShrinkCompleteDiameterPixels = 4.5f;

        [Tooltip(
            "Projected diameter where an external galaxy stops being only a " +
            "point and starts loading its real diffuse fog plus starfield. " +
            "Increase this to begin the transition farther away.")]
        [SerializeField, Min(0.25f)]
        private float galaxyLod1FadeInStartDiameterPixels = 6.0f;

        [Tooltip(
            "Projected diameter where the preloaded external galaxy fog and " +
            "starfield reach full brightness.")]
        [SerializeField, Min(0.25f)]
        private float galaxyLod1FullyVisibleDiameterPixels = 16.0f;

        [Tooltip(
            "Legacy marker-handoff setting. A marker now fades only after its " +
            "specific galaxy is genuinely preloaded, never by size alone.")]
        [SerializeField, Min(0.25f)]
        private float galaxyLod0HideAfterLod1DiameterPixels = 16.0f;

        [Tooltip(
            "How many nearby external galaxies may keep diffuse fog and their " +
            "generated starfields loaded simultaneously. Farther galaxies " +
            "remain star-sized points.")]
        [SerializeField, Range(1, 16)]
        private int maximumLoadedExternalGalaxies = 8;

        [Tooltip(
            "Maximum deterministic star samples used by each preloaded " +
            "external galaxy. The renderer draws fewer while it is tiny.")]
        [SerializeField, Range(256, 8_192)]
        private int externalGalaxyStarfieldSampleCount = 2_048;

        [Tooltip(
            "On-screen size of a star point inside a preloaded external " +
            "galaxy, in pixels.")]
        [SerializeField, Range(0.25f, 3.0f)]
        private float externalGalaxyStarPointDiameterPixels = 1.0f;

        [Tooltip(
            "Distance from another galaxy centre, in its generated radii, " +
            "where the renderer changes the active galaxy context. The " +
            "full local gas and stellar streaming then take over.")]
        [SerializeField, Min(1.0f)]
        private float galaxyActivationDistanceInRadii = 1.20f;

        [Header("Galaxy gas volume")]
        [Tooltip(
            "Renders a camera-raymarched galaxy volume behind the real " +
            "star catalogue. It is visible from inside the active galaxy, " +
            "including when looking along the galactic disk.")]
        [SerializeField]
        private bool enableGalaxyGas = true;

        [Tooltip(
            "Ray-march quality for the galaxy volume. Higher values improve " +
            "dust and cloud detail but cost more GPU time.")]
        [SerializeField, Range(8, 96)]
        [FormerlySerializedAs("galaxyGasSliceCount")]
        private int galaxyGasRaymarchSteps = 24;

        [Tooltip(
            "Emission intensity of unresolved stellar light and diffuse gas.")]
        [SerializeField, Range(0.0f, 8.0f)]
        private float galaxyGasBrightness = 1.0f;

        [Tooltip(
            "Optical density of the volumetric disk. Increase this for a " +
            "stronger Milky Way-like band while flying within the galaxy.")]
        [SerializeField, Range(0.0f, 4.0f)]
        private float galaxyGasOpacity = 1.25f;

        [Tooltip(
            "Strength of procedural dust lanes. Dust darkens only dense gas " +
            "regions; it never draws opaque black planes in front of space.")]
        [SerializeField, Range(0.0f, 2.0f)]
        private float galaxyGasDustStrength = 0.9f;

        [Tooltip(
            "Expands or contracts the visible gas disk relative to the " +
            "generated galaxy radius.")]
        [SerializeField, Range(0.5f, 2.0f)]
        private float galaxyGasDiskRadiusMultiplier = 1.0f;

        [Tooltip(
            "Expands or contracts the volume thickness relative to the " +
            "generated galaxy disk thickness.")]
        [SerializeField, Range(0.5f, 3.0f)]
        private float galaxyGasDiskThicknessMultiplier = 1.0f;

        [Header("Scaled-space presentation scale")]
        [SerializeField, Min(1.0f)]
        private double scaledSpaceMetersPerUnityUnit = 10_000_000.0;

        [SerializeField, Min(1.0f)]
        private float minimumPlanetDiameterInUnityUnits = 1.0f;

        [SerializeField, Min(1.0f)]
        private float minimumStarDiameterInUnityUnits = 8.0f;

        private CelestialRenderRuntime _runtime;
        private bool _hasReportedInvalidLayer;

        public SeamlessSpaceStreamingController StreamingController =>
            _runtime == null ? null : _runtime.StreamingController;

        public SolarSystemScaledSpaceRenderer SolarRenderer =>
            _runtime == null ? null : _runtime.SolarRenderer;

        public Camera PlayerCamera => playerCamera;
        public Camera CelestialCamera => celestialFrameCamera;

        public override bool IsReady =>
            _runtime != null && celestialFrameCamera != null;

        private void Reset()
        {
            ValidateSettings();
            ResolvePlayerCamera();
            AssignDefaultLayer();
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
                return;

            ValidateSettings();
            ResolvePlayerCamera();
            AssignDefaultLayer();

            if (playerCamera == null ||
                !ReferenceFrameLayerUtility.TryGetSingleLayerIndex(
                    celestialFrameLayer,
                    out _))
            {
                return;
            }

            EnsureCelestialCamera();
            EnsureCelestialRoot();
            SynchronizeCelestialCamera();
            ConfigureCameraStack();
        }

        [ContextMenu("Rebuild 3D celestial renderer")]
        public void RebuildRenderer()
        {
            if (Engine == null || Anchor == null)
                return;

            RecreateRuntime();
        }

        protected override void OnInitialize()
        {
            // The renderer starts before any anchor is observed. Prepare the
            // camera stack now; the runtime itself is created by
            // OnAnchorChanged once a CelestialAnchor3D is selected for view.
            SetupPresentation();
        }

        protected override void OnTickStreaming(float unscaledTime)
        {
            _runtime?.UpdateStreaming(unscaledTime);
        }

        protected override void OnTickVisuals(
            float deltaTime,
            double simulationTimeSeconds)
        {
            SynchronizeCelestialCamera();
            _runtime?.UpdateVisuals(simulationTimeSeconds);
        }

        protected override void OnAnchorChanged()
        {
            RecreateRuntime();
        }

        protected override void OnEvaluateNow()
        {
            _runtime?.EvaluateNow();
        }

        protected override void OnShutdown()
        {
            _runtime?.Dispose();
            _runtime = null;
        }

        private void SetupRuntime()
        {
            if (!SetupPresentation())
                return;

            if (!(Anchor is CelestialAnchor3D anchor3D))
            {
                // A future 2D renderer can accept another anchor type. This
                // renderer deliberately only consumes the 3D frame backend.
                return;
            }

            _runtime ??= new CelestialRenderRuntime(
                celestialRoot,
                anchor3D.Backend);

            _runtime.Configure(CreateRuntimeSettings());
            _runtime.Initialize();
        }

        private bool SetupPresentation()
        {
            ValidateSettings();
            ResolvePlayerCamera();
            AssignDefaultLayer();

            if (playerCamera == null ||
                !ReferenceFrameLayerUtility.TryGetSingleLayerIndex(
                    celestialFrameLayer,
                    out _))
            {
                ReportInvalidLayer();
                return false;
            }

            _hasReportedInvalidLayer = false;
            EnsureCelestialCamera();
            EnsureCelestialRoot();
            RemoveLegacyFrameCameras();
            SynchronizeCelestialCamera();
            ConfigureCameraStack();
            return true;
        }

        private void RecreateRuntime()
        {
            _runtime?.Dispose();
            _runtime = null;
            SetupRuntime();
        }

        private void ValidateSettings()
        {
            lod0MinimumPointDiameterPixels = Mathf.Max(
                0.25f,
                lod0MinimumPointDiameterPixels);

            lod0ToLod1ActivationDistanceLightYears = Math.Max(
                0.000001,
                lod0ToLod1ActivationDistanceLightYears);

            lod0ToLod1DeactivationDistanceLightYears = Math.Max(
                lod0ToLod1ActivationDistanceLightYears,
                lod0ToLod1DeactivationDistanceLightYears);

            lod1MinimumStarAngularDiameterDegrees = Mathf.Max(
                0.001f,
                lod1MinimumStarAngularDiameterDegrees);

            lod0HideAfterLod1AngularDiameterDegrees = Mathf.Max(
                0.001f,
                lod0HideAfterLod1AngularDiameterDegrees);

            lod1StarLightRadiusMultiplier = Mathf.Clamp(
                lod1StarLightRadiusMultiplier,
                1.0f,
                64.0f);
            lod1StarLightIntensity = Mathf.Clamp(
                lod1StarLightIntensity,
                0.0f,
                32.0f);
            lod1StarLightRayStrength = Mathf.Clamp(
                lod1StarLightRayStrength,
                0.0f,
                3.0f);
            lod1StarLightRayCount = Mathf.Clamp(
                lod1StarLightRayCount,
                4.0f,
                32.0f);

            lod2StarSurfaceActivationDistanceInStarRadii = Math.Max(
                1.0,
                lod2StarSurfaceActivationDistanceInStarRadii);
            lod2StarSurfaceDeactivationDistanceInStarRadii = Math.Max(
                lod2StarSurfaceActivationDistanceInStarRadii,
                lod2StarSurfaceDeactivationDistanceInStarRadii);

            lod2PlanetSurfaceActivationDistanceInRadii = Math.Max(
                1.0,
                lod2PlanetSurfaceActivationDistanceInRadii);
            lod2PlanetSurfaceDeactivationDistanceInRadii = Math.Max(
                lod2PlanetSurfaceActivationDistanceInRadii,
                lod2PlanetSurfaceDeactivationDistanceInRadii);

            minimumPlanetAngularDiameterDegrees = Mathf.Max(
                0.0f,
                minimumPlanetAngularDiameterDegrees);
            distantGalaxyPointHorizontalSectorRadius = Mathf.Clamp(
                distantGalaxyPointHorizontalSectorRadius,
                1,
                8);
            distantGalaxyPointVerticalSectorRadius = Mathf.Clamp(
                distantGalaxyPointVerticalSectorRadius,
                0,
                4);
            maximumDistantGalaxyPoints = Mathf.Clamp(
                maximumDistantGalaxyPoints,
                64,
                4_096);
            galaxyLod0MinimumPointDiameterPixels = Mathf.Max(
                0.25f,
                galaxyLod0MinimumPointDiameterPixels);
            galaxyLod0NearPointDiameterPixels = Mathf.Clamp(
                galaxyLod0NearPointDiameterPixels,
                0.05f,
                galaxyLod0MinimumPointDiameterPixels);
            galaxyLod0ShrinkCompleteDiameterPixels = Mathf.Max(
                0.25f,
                galaxyLod0ShrinkCompleteDiameterPixels);
            galaxyLod1FadeInStartDiameterPixels = Mathf.Max(
                0.25f,
                galaxyLod1FadeInStartDiameterPixels);
            galaxyLod1FullyVisibleDiameterPixels = Mathf.Max(
                galaxyLod1FadeInStartDiameterPixels,
                galaxyLod1FullyVisibleDiameterPixels);
            galaxyLod0HideAfterLod1DiameterPixels = Mathf.Max(
                galaxyLod1FullyVisibleDiameterPixels,
                galaxyLod0HideAfterLod1DiameterPixels);
            galaxyLod0ShrinkCompleteDiameterPixels = Mathf.Min(
                galaxyLod0ShrinkCompleteDiameterPixels,
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
            galaxyGasBrightness = Mathf.Clamp(
                galaxyGasBrightness,
                0.0f,
                8.0f);
            galaxyGasOpacity = Mathf.Clamp(
                galaxyGasOpacity,
                0.0f,
                4.0f);
            galaxyGasDustStrength = Mathf.Clamp(
                galaxyGasDustStrength,
                0.0f,
                2.0f);
            galaxyGasDiskRadiusMultiplier = Mathf.Clamp(
                galaxyGasDiskRadiusMultiplier,
                0.5f,
                2.0f);
            galaxyGasDiskThicknessMultiplier = Mathf.Clamp(
                galaxyGasDiskThicknessMultiplier,
                0.5f,
                3.0f);
            scaledSpaceMetersPerUnityUnit = Math.Max(
                1.0,
                scaledSpaceMetersPerUnityUnit);
            minimumPlanetDiameterInUnityUnits = Mathf.Max(
                1.0f,
                minimumPlanetDiameterInUnityUnits);
            minimumStarDiameterInUnityUnits = Mathf.Max(
                minimumPlanetDiameterInUnityUnits,
                minimumStarDiameterInUnityUnits);
            celestialFrameFarClipPlane = Mathf.Max(
                1_000.0f,
                celestialFrameFarClipPlane);
        }

        private void ResolvePlayerCamera()
        {
            if (playerCamera == null)
                playerCamera = Camera.main;
        }

        private void AssignDefaultLayer()
        {
            if (celestialFrameLayer.value != 0)
                return;

            var layer = LayerMask.NameToLayer("CelestialFrame");
            if (layer < 0)
                layer = LayerMask.NameToLayer("SolarFrame");

            if (layer >= 0)
                celestialFrameLayer = 1 << layer;
        }

        private void EnsureCelestialCamera()
        {
            if (celestialFrameCamera == null)
            {
                var child = transform.Find("Celestial Frame Camera") ??
                            transform.Find("Solar Frame Camera");

                if (child != null)
                    celestialFrameCamera = child.GetComponent<Camera>();
            }

            if (celestialFrameCamera == null)
            {
                var cameraObject = new GameObject("Celestial Frame Camera");
                cameraObject.transform.SetParent(transform, false);
                cameraObject.tag = "Untagged";
                celestialFrameCamera = cameraObject.AddComponent<Camera>();
            }

            celestialFrameCamera.name = "Celestial Frame Camera";
        }

        private void EnsureCelestialRoot()
        {
            if (celestialRoot != null)
                return;

            var child = transform.Find("Celestial Render Root");
            if (child != null)
            {
                celestialRoot = child;
                return;
            }

            var root = new GameObject("Celestial Render Root");
            root.transform.SetParent(transform, false);
            celestialRoot = root.transform;
        }

        private void SynchronizeCelestialCamera()
        {
            if (playerCamera == null || celestialFrameCamera == null)
                return;

            celestialFrameCamera.transform.SetPositionAndRotation(
                Vector3.zero,
                playerCamera.transform.rotation);
            celestialFrameCamera.orthographic = playerCamera.orthographic;
            celestialFrameCamera.fieldOfView = playerCamera.fieldOfView;
            celestialFrameCamera.orthographicSize =
                playerCamera.orthographicSize;
            celestialFrameCamera.nearClipPlane = 0.001f;
            celestialFrameCamera.farClipPlane = celestialFrameFarClipPlane;
            celestialFrameCamera.aspect = playerCamera.aspect;
            celestialFrameCamera.clearFlags = CameraClearFlags.SolidColor;
            celestialFrameCamera.backgroundColor = Color.black;
            celestialFrameCamera.cullingMask = celestialFrameLayer.value;
            celestialFrameCamera.depth = playerCamera.depth - 1.0f;
            celestialFrameCamera.allowHDR = playerCamera.allowHDR;
            celestialFrameCamera.allowMSAA = playerCamera.allowMSAA;

            playerCamera.cullingMask &= ~celestialFrameLayer.value;
        }

        private void RemoveLegacyFrameCameras()
        {
            RemoveLegacyFrameCamera("Universe Frame Camera");
            RemoveLegacyFrameCamera("Galaxy Frame Camera");
            RemoveLegacyFrameCamera("Stellar Frame Camera");

            var oldSolarCamera = transform.Find("Solar Frame Camera");
            if (oldSolarCamera != null &&
                oldSolarCamera.GetComponent<Camera>() != celestialFrameCamera)
            {
                DestroyObject(oldSolarCamera.gameObject);
            }
        }

        private void RemoveLegacyFrameCamera(string childName)
        {
            var child = transform.Find(childName);
            if (child != null && child.GetComponent<Camera>() != celestialFrameCamera)
                DestroyObject(child.gameObject);
        }

        private static void DestroyObject(UnityEngine.Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(target);
            else
                UnityEngine.Object.DestroyImmediate(target);
        }

        private void ConfigureCameraStack()
        {
            var additionalCameraDataType = FindUniversalAdditionalCameraDataType();
            if (celestialFrameCamera == null || playerCamera == null)
                return;

            if (additionalCameraDataType == null)
            {
                // Built-in pipeline fallback: celestial camera clears first,
                // then the game camera adds its world without erasing space.
                playerCamera.depth = celestialFrameCamera.depth + 1.0f;
                playerCamera.clearFlags = CameraClearFlags.Depth;
                return;
            }

            var celestialData = GetOrAddComponent(
                celestialFrameCamera.gameObject,
                additionalCameraDataType);
            var playerData = GetOrAddComponent(
                playerCamera.gameObject,
                additionalCameraDataType);

            if (celestialData == null || playerData == null)
                return;

            SetEnumProperty(celestialData, "renderType", "Base");
            SetEnumProperty(playerData, "renderType", "Overlay");
            SetBooleanProperty(playerData, "clearDepth", true);

            var stackProperty = additionalCameraDataType.GetProperty(
                "cameraStack",
                BindingFlags.Instance | BindingFlags.Public);
            var stack = stackProperty?.GetValue(celestialData) as IList;
            if (stack == null)
                return;

            stack.Clear();
            stack.Add(playerCamera);
        }

        private CelestialStreamingSettings CreateRuntimeSettings()
        {
            var settings = GetQualitySettings();
            return new CelestialStreamingSettings(
                celestialFrameCamera,
                celestialFrameLayer,
                settings.AggregateStarSampleCount,
                Mathf.Max(
                    settings.MaximumGalaxyProxies,
                    maximumDistantGalaxyPoints),
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
                galaxyGasBrightness,
                galaxyGasOpacity,
                galaxyGasDustStrength,
                galaxyGasDiskRadiusMultiplier,
                galaxyGasDiskThicknessMultiplier,
                settings.StellarFieldSectorRadius,
                settings.StellarFieldVerticalSectorRadius,
                settings.MaximumStellarPoints,
                lod0MinimumPointDiameterPixels,
                lod1MinimumStarAngularDiameterDegrees,
                minimumPlanetAngularDiameterDegrees,
                scaledSpaceMetersPerUnityUnit,
                minimumStarDiameterInUnityUnits,
                minimumPlanetDiameterInUnityUnits,
                lod1StarLightRadiusMultiplier,
                lod1StarLightIntensity,
                lod1StarLightRayStrength,
                lod1StarLightRayCount,
                enableStarLod2LocalSurface,
                lod2StarSurfaceActivationDistanceInStarRadii,
                lod2StarSurfaceDeactivationDistanceInStarRadii,
                enablePlanetLod2LocalSurface,
                lod2PlanetSurfaceActivationDistanceInRadii,
                lod2PlanetSurfaceDeactivationDistanceInRadii,
                lod0ToLod1ActivationDistanceLightYears,
                lod0ToLod1DeactivationDistanceLightYears,
                lod0HideAfterLod1AngularDiameterDegrees);
        }

        private QualitySettings GetQualitySettings()
        {
            return quality switch
            {
                CelestialRendererQuality.Low => new QualitySettings(
                    8_000, 128, 4, 4, 4_000),
                CelestialRendererQuality.High => new QualitySettings(
                    30_000, 1_024, 10, 10, 20_000),
                _ => new QualitySettings(18_000, 512, 7, 7, 10_000)
            };
        }

        private void ReportInvalidLayer()
        {
            if (_hasReportedInvalidLayer)
                return;

            _hasReportedInvalidLayer = true;
            Debug.LogError(
                "CelestialRenderer3D requires exactly one " +
                "Celestial Frame layer.",
                this);
        }

        private static Type FindUniversalAdditionalCameraDataType()
        {
            const string typeName =
                "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData";

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static Component GetOrAddComponent(
            GameObject target,
            Type componentType)
        {
            var component = target.GetComponent(componentType);
            return component != null
                ? component
                : target.AddComponent(componentType);
        }

        private static void SetEnumProperty(
            Component component,
            string propertyName,
            string enumValue)
        {
            var property = component.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public);

            if (property?.CanWrite != true)
                return;

            property.SetValue(
                component,
                Enum.Parse(property.PropertyType, enumValue));
        }

        private static void SetBooleanProperty(
            Component component,
            string propertyName,
            bool value)
        {
            var property = component.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public);

            if (property?.CanWrite == true &&
                property.PropertyType == typeof(bool))
            {
                property.SetValue(component, value);
            }
        }

        private readonly struct QualitySettings
        {
            public readonly int AggregateStarSampleCount;
            public readonly int MaximumGalaxyProxies;
            public readonly int StellarFieldSectorRadius;
            public readonly int StellarFieldVerticalSectorRadius;
            public readonly int MaximumStellarPoints;

            public QualitySettings(
                int aggregateStarSampleCount,
                int maximumGalaxyProxies,
                int stellarFieldSectorRadius,
                int stellarFieldVerticalSectorRadius,
                int maximumStellarPoints)
            {
                AggregateStarSampleCount = aggregateStarSampleCount;
                MaximumGalaxyProxies = maximumGalaxyProxies;
                StellarFieldSectorRadius = stellarFieldSectorRadius;
                StellarFieldVerticalSectorRadius =
                    stellarFieldVerticalSectorRadius;
                MaximumStellarPoints = maximumStellarPoints;
            }
        }
    }
}
