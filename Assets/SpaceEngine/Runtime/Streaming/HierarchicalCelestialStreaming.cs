using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Serialization;

namespace SpaceEngine.Runtime.Streaming
{
    public enum CelestialStreamingQuality
    {
        Low,
        Balanced,
        High
    }

    /// <summary>
    /// The only MonoBehaviour in the celestial streaming package.
    /// It owns one celestial camera and drives all non-MonoBehaviour runtime
    /// systems: universe proxies, galaxy haze, stellar points, scaled solar
    /// systems and detailed LOD 2 body renderers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HierarchicalCelestialStreaming : MonoBehaviour
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
        [SerializeField] private CelestialStreamingQuality quality =
            CelestialStreamingQuality.Balanced;

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

        [Header("Scaled-space presentation scale")]
        [SerializeField, Min(1.0f)]
        private double scaledSpaceMetersPerUnityUnit = 10_000_000.0;

        [SerializeField, Min(1.0f)]
        private float minimumPlanetDiameterInUnityUnits = 1.0f;

        [SerializeField, Min(1.0f)]
        private float minimumStarDiameterInUnityUnits = 8.0f;

        private CelestialStreamingRuntime _runtime;
        private bool _hasReportedInvalidLayer;

        public SeamlessSpaceAnchor SpaceAnchor =>
            _runtime == null ? null : _runtime.SpaceAnchor;

        public SeamlessSpaceStreamingController StreamingController =>
            _runtime == null ? null : _runtime.StreamingController;

        public SolarSystemScaledSpaceRenderer SolarRenderer =>
            _runtime == null ? null : _runtime.SolarRenderer;

        public Camera PlayerCamera => playerCamera;
        public Camera CelestialCamera => celestialFrameCamera;

        private void Reset()
        {
            Setup();
        }

        private void Awake()
        {
            Setup();
            _runtime?.Initialize();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
                Setup();
        }

        private void Update()
        {
            _runtime?.UpdateStreaming(Time.unscaledTime);
        }

        private void LateUpdate()
        {
            SynchronizeCelestialCamera();
            _runtime?.UpdateVisuals(Time.deltaTime);
        }

        private void OnDestroy()
        {
            _runtime?.Dispose();
            _runtime = null;
        }

        [ContextMenu("Rebuild celestial streaming setup")]
        public void RebuildSetup()
        {
            Setup();
        }

        private void Setup()
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
                return;
            }

            _hasReportedInvalidLayer = false;
            EnsureCelestialCamera();
            EnsureCelestialRoot();
            RemoveLegacyFrameCameras();
            SynchronizeCelestialCamera();
            ConfigureCameraStack();

            _runtime ??= new CelestialStreamingRuntime(celestialRoot);
            _runtime.Configure(CreateRuntimeSettings());
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
                settings.MaximumGalaxyProxies,
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
                CelestialStreamingQuality.Low => new QualitySettings(
                    8_000, 128, 4, 4, 4_000),
                CelestialStreamingQuality.High => new QualitySettings(
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
                "HierarchicalCelestialStreaming requires exactly one " +
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
