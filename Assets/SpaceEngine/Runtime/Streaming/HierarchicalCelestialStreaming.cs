using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace SpaceEngine.Runtime.Streaming
{
    public enum CelestialStreamingQuality
    {
        Low,
        Balanced,
        High
    }

    /// <summary>
    /// Compact setup component for the complete seamless celestial hierarchy.
    ///
    /// Add only this component to one "Space Streaming" object. It creates the
    /// dependent streaming components and the four reference-frame cameras.
    /// The lower LOD depends on the upper LOD, while all frames keep using the
    /// same SeamlessSpaceAnchor.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GalaxySpaceAnchor))]
    [RequireComponent(typeof(SeamlessSpaceAnchor))]
    [RequireComponent(typeof(UniverseGalaxyFieldRenderer))]
    [RequireComponent(typeof(GalaxyStarfieldRenderer))]
    [RequireComponent(typeof(StellarFieldRenderer))]
    [RequireComponent(typeof(SeamlessSpaceStreamingController))]
    [RequireComponent(typeof(SolarSystemScaledSpaceRenderer))]
    public sealed class HierarchicalCelestialStreaming : MonoBehaviour
    {
        [Header("Required")]
        [SerializeField] private Camera playerCamera;

        [Header("Reference-frame layers")]
        [Tooltip(
            "Choose exactly one layer in each mask. " +
            "The component automatically assigns camera culling masks.")]
        [SerializeField] private LayerMask universeFrameLayer;
        [SerializeField] private LayerMask galaxyFrameLayer;
        [SerializeField] private LayerMask stellarFrameLayer;
        [SerializeField] private LayerMask solarFrameLayer;

        [Header("Quality")]
        [SerializeField] private CelestialStreamingQuality quality =
            CelestialStreamingQuality.Balanced;

        [Header("Star LOD transitions")]
        [Tooltip(
            "LOD 0: minimum on-screen diameter for distant stellar points. " +
            "This is measured in pixels.")]
        [SerializeField, Min(0.25f)]
        private float lod0MinimumPointDiameterPixels = 3.0f;

        [Tooltip(
            "LOD 0 → LOD 1: enter the scaled solar-system representation " +
            "when the ship is this close to a real stellar system.")]
        [SerializeField, Min(0.000001f)]
        private double lod0ToLod1ActivationDistanceLightYears = 0.02;

        [Tooltip(
            "LOD 1 stays active until this distance. Keep it greater than " +
            "or equal to the activation distance to prevent toggling at the " +
            "boundary.")]
        [SerializeField, Min(0.000001f)]
        private double lod0ToLod1DeactivationDistanceLightYears = 0.03;

        [Tooltip(
            "LOD 1: the scaled star sphere is never rendered smaller than " +
            "this apparent diameter. Increase this when a new star appears " +
            "too small during the handoff.")]
        [SerializeField, Range(0.001f, 5.0f)]
        private float lod1MinimumStarAngularDiameterDegrees = 0.20f;

        [Tooltip(
            "LOD 0 remains visible until LOD 1 reaches this apparent diameter. " +
            "Use a value larger than the LOD 1 minimum to create an overlap " +
            "instead of a visible gap.")]
        [SerializeField, Range(0.001f, 10.0f)]
        private float lod0HideAfterLod1AngularDiameterDegrees = 0.35f;

        [Tooltip(
            "LOD 2: activate the detailed plasma surface when the ship is " +
            "closer than this many star radii.")]
        [SerializeField, Min(1.0f)]
        private double lod2SurfaceActivationDistanceInStarRadii = 64.0;

        [Tooltip(
            "LOD 2 remains active until this many star radii. Keep it greater " +
            "than the activation distance to avoid rapid toggling.")]
        [SerializeField, Min(1.0f)]
        private double lod2SurfaceDeactivationDistanceInStarRadii = 80.0;

        [SerializeField, HideInInspector] private Camera universeFrameCamera;
        [SerializeField, HideInInspector] private Camera galaxyFrameCamera;
        [SerializeField, HideInInspector] private Camera stellarFrameCamera;
        [SerializeField, HideInInspector] private Camera solarFrameCamera;

        private GalaxySpaceAnchor _galaxySpaceAnchor;
        private SeamlessSpaceAnchor _spaceAnchor;
        private UniverseGalaxyFieldRenderer _universeRenderer;
        private GalaxyStarfieldRenderer _galaxyRenderer;
        private StellarFieldRenderer _stellarRenderer;
        private GalaxySpaceStreamer _legacyGalaxySpaceStreamer;
        private SeamlessSpaceStreamingController _streamingController;
        private SolarSystemScaledSpaceRenderer _solarRenderer;

        private bool _hasReportedInvalidLayers;

        public SeamlessSpaceAnchor SpaceAnchor => _spaceAnchor;

        public SeamlessSpaceStreamingController StreamingController =>
            _streamingController;

        public Camera PlayerCamera => playerCamera;

        private void Reset()
        {
            ValidateStarLodSettings();
            ResolvePlayerCamera();
            AssignDefaultLayers();
            EnsureDependencies();
            EnsureReferenceFrameCameras();
            ApplyConfiguration();
        }

        private void Awake()
        {
            ValidateStarLodSettings();
            ResolvePlayerCamera();
            EnsureDependencies();
            EnsureReferenceFrameCameras();
            ApplyConfiguration();
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
                return;

            ValidateStarLodSettings();
            ResolvePlayerCamera();
            AssignDefaultLayers();
            EnsureDependencies();
            EnsureReferenceFrameCameras();
            ApplyConfiguration();
        }

        [ContextMenu("Rebuild seamless streaming setup")]
        public void RebuildSetup()
        {
            ValidateStarLodSettings();
            ResolvePlayerCamera();
            AssignDefaultLayers();
            EnsureDependencies();
            EnsureReferenceFrameCameras();
            ApplyConfiguration();
        }

        private void ValidateStarLodSettings()
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

            lod2SurfaceActivationDistanceInStarRadii = Math.Max(
                1.0,
                lod2SurfaceActivationDistanceInStarRadii);

            lod2SurfaceDeactivationDistanceInStarRadii = Math.Max(
                lod2SurfaceActivationDistanceInStarRadii,
                lod2SurfaceDeactivationDistanceInStarRadii);
        }

        private void ResolvePlayerCamera()
        {
            if (playerCamera == null)
                playerCamera = Camera.main;
        }

        private void AssignDefaultLayers()
        {
            universeFrameLayer = AssignDefaultLayerIfEmpty(
                universeFrameLayer,
                "UniverseFrame");

            galaxyFrameLayer = AssignDefaultLayerIfEmpty(
                galaxyFrameLayer,
                "GalaxyFrame");

            stellarFrameLayer = AssignDefaultLayerIfEmpty(
                stellarFrameLayer,
                "StellarFrame");

            solarFrameLayer = AssignDefaultLayerIfEmpty(
                solarFrameLayer,
                "SolarFrame");
        }

        private void EnsureDependencies()
        {
            _galaxySpaceAnchor = GetOrAddComponent<GalaxySpaceAnchor>();
            _spaceAnchor = GetOrAddComponent<SeamlessSpaceAnchor>();
            _universeRenderer =
                GetOrAddComponent<UniverseGalaxyFieldRenderer>();
            _galaxyRenderer = GetOrAddComponent<GalaxyStarfieldRenderer>();
            _stellarRenderer = GetOrAddComponent<StellarFieldRenderer>();

            // GalaxySpaceStreamer is from the previous prototype. Keep an
            // existing component disabled so it cannot draw a second, sparse
            // field over the new real stellar catalogue.
            _legacyGalaxySpaceStreamer =
                GetComponent<GalaxySpaceStreamer>();

            if (_legacyGalaxySpaceStreamer != null)
                _legacyGalaxySpaceStreamer.enabled = false;
            _streamingController =
                GetOrAddComponent<SeamlessSpaceStreamingController>();
            _solarRenderer =
                GetOrAddComponent<SolarSystemScaledSpaceRenderer>();
        }

        private void EnsureReferenceFrameCameras()
        {
            if (playerCamera == null)
                return;

            universeFrameCamera = GetOrCreateFrameCamera(
                universeFrameCamera,
                "Universe Frame Camera");

            galaxyFrameCamera = GetOrCreateFrameCamera(
                galaxyFrameCamera,
                "Galaxy Frame Camera");

            stellarFrameCamera = GetOrCreateFrameCamera(
                stellarFrameCamera,
                "Stellar Frame Camera");

            solarFrameCamera = GetOrCreateFrameCamera(
                solarFrameCamera,
                "Solar Frame Camera");
        }

        private Camera GetOrCreateFrameCamera(
            Camera frameCamera,
            string cameraName)
        {
            if (frameCamera != null)
                return frameCamera;

            var child = transform.Find(cameraName);
            if (child != null)
            {
                frameCamera = child.GetComponent<Camera>();

                if (frameCamera != null)
                    return frameCamera;
            }

            var cameraObject = new GameObject(cameraName);
            cameraObject.transform.SetParent(transform, false);
            cameraObject.tag = "Untagged";

            return cameraObject.AddComponent<Camera>();
        }

        private void ApplyConfiguration()
        {
            if (!TryGetConfiguredLayers(
                    out var universeLayer,
                    out var galaxyLayer,
                    out var stellarLayer,
                    out var solarLayer))
            {
                return;
            }

            if (playerCamera == null ||
                universeFrameCamera == null ||
                galaxyFrameCamera == null ||
                stellarFrameCamera == null ||
                solarFrameCamera == null)
            {
                return;
            }

            ConfigureFrameCamera(
                universeFrameCamera,
                universeFrameLayer,
                CameraClearFlags.SolidColor,
                5_000f);

            ConfigureFrameCamera(
                galaxyFrameCamera,
                galaxyFrameLayer,
                CameraClearFlags.Depth,
                5_000f);

            ConfigureFrameCamera(
                stellarFrameCamera,
                stellarFrameLayer,
                CameraClearFlags.Depth,
                1_000f);

            ConfigureFrameCamera(
                solarFrameCamera,
                solarFrameLayer,
                CameraClearFlags.Depth,
                20_000f);

            var specialFrameMask =
                universeFrameLayer.value |
                galaxyFrameLayer.value |
                stellarFrameLayer.value |
                solarFrameLayer.value;

            playerCamera.cullingMask = ~specialFrameMask;

            ConfigureUniversalCameraStack();

            var settings = GetQualitySettings();

            _spaceAnchor.Configure(_galaxySpaceAnchor);

            _universeRenderer.Configure(
                _spaceAnchor,
                universeFrameCamera,
                universeFrameLayer,
                settings.MaximumGalaxyProxies);

            _galaxyRenderer.Configure(
                _spaceAnchor,
                galaxyFrameCamera,
                galaxyFrameLayer,
                settings.AggregateStarSampleCount,
                settings.StellarFieldRadiusLightYears);

            _stellarRenderer.Configure(
                _galaxySpaceAnchor,
                stellarFrameCamera,
                stellarFrameLayer,
                settings.StellarFieldSectorRadius,
                settings.StellarFieldVerticalSectorRadius,
                settings.MaximumStellarPoints,
                lod0MinimumPointDiameterPixels);

            _solarRenderer.Configure(
                _spaceAnchor,
                solarFrameLayer,
                lod1MinimumStarAngularDiameterDegrees,
                lod2SurfaceActivationDistanceInStarRadii,
                lod2SurfaceDeactivationDistanceInStarRadii);

            _streamingController.Configure(
                _spaceAnchor,
                _stellarRenderer,
                _solarRenderer,
                lod0ToLod1ActivationDistanceLightYears,
                lod0ToLod1DeactivationDistanceLightYears,
                lod0HideAfterLod1AngularDiameterDegrees);
        }

        private void ConfigureFrameCamera(
            Camera frameCamera,
            LayerMask frameLayer,
            CameraClearFlags clearFlags,
            float farClipPlane)
        {
            frameCamera.backgroundColor = Color.black;
            frameCamera.clearFlags = clearFlags;
            frameCamera.cullingMask = frameLayer.value;

            var synchronizer =
                GetOrAddComponent<ReferenceFrameCameraSynchronizer>(
                    frameCamera.gameObject);

            synchronizer.Configure(
                playerCamera,
                frameCamera,
                frameLayer,
                clearFlags,
                farClipPlane);
        }

        private void ConfigureUniversalCameraStack()
        {
            var additionalCameraDataType =
                FindUniversalAdditionalCameraDataType();

            if (additionalCameraDataType == null)
                return;

            var universeData = GetOrAddComponent(
                universeFrameCamera.gameObject,
                additionalCameraDataType);

            var galaxyData = GetOrAddComponent(
                galaxyFrameCamera.gameObject,
                additionalCameraDataType);

            var stellarData = GetOrAddComponent(
                stellarFrameCamera.gameObject,
                additionalCameraDataType);

            var solarData = GetOrAddComponent(
                solarFrameCamera.gameObject,
                additionalCameraDataType);

            var playerData = GetOrAddComponent(
                playerCamera.gameObject,
                additionalCameraDataType);

            if (universeData == null ||
                galaxyData == null ||
                stellarData == null ||
                solarData == null ||
                playerData == null)
            {
                return;
            }

            SetEnumProperty(universeData, "renderType", "Base");
            SetEnumProperty(galaxyData, "renderType", "Overlay");
            SetEnumProperty(stellarData, "renderType", "Overlay");
            SetEnumProperty(solarData, "renderType", "Overlay");
            SetEnumProperty(playerData, "renderType", "Overlay");

            SetBooleanProperty(galaxyData, "clearDepth", true);
            SetBooleanProperty(stellarData, "clearDepth", true);
            SetBooleanProperty(solarData, "clearDepth", true);
            SetBooleanProperty(playerData, "clearDepth", true);

            var stackProperty = additionalCameraDataType.GetProperty(
                "cameraStack",
                BindingFlags.Instance |
                BindingFlags.Public);

            var stack = stackProperty == null
                ? null
                : stackProperty.GetValue(universeData) as IList;

            if (stack == null)
                return;

            stack.Clear();
            stack.Add(galaxyFrameCamera);
            stack.Add(stellarFrameCamera);
            stack.Add(solarFrameCamera);
            stack.Add(playerCamera);
        }

        private bool TryGetConfiguredLayers(
            out int universeLayer,
            out int galaxyLayer,
            out int stellarLayer,
            out int solarLayer)
        {
            universeLayer = 0;
            galaxyLayer = 0;
            stellarLayer = 0;
            solarLayer = 0;

            if (!ReferenceFrameLayerUtility.AreDifferentSingleLayers(
                    universeFrameLayer,
                    galaxyFrameLayer,
                    stellarFrameLayer,
                    solarFrameLayer))
            {
                ReportInvalidLayers();
                return false;
            }

            ReferenceFrameLayerUtility.TryGetSingleLayerIndex(
                universeFrameLayer,
                out universeLayer);

            ReferenceFrameLayerUtility.TryGetSingleLayerIndex(
                galaxyFrameLayer,
                out galaxyLayer);

            ReferenceFrameLayerUtility.TryGetSingleLayerIndex(
                stellarFrameLayer,
                out stellarLayer);

            ReferenceFrameLayerUtility.TryGetSingleLayerIndex(
                solarFrameLayer,
                out solarLayer);

            _hasReportedInvalidLayers = false;
            return true;
        }

        private void ReportInvalidLayers()
        {
            if (_hasReportedInvalidLayers)
                return;

            _hasReportedInvalidLayers = true;

            Debug.LogError(
                "HierarchicalCelestialStreaming requires four different " +
                "LayerMask values. Select exactly one layer in each frame " +
                "mask: Universe, Galaxy, Stellar and Solar.",
                this);
        }

        private QualitySettings GetQualitySettings()
        {
            switch (quality)
            {
                case CelestialStreamingQuality.Low:
                    return new QualitySettings(
                        aggregateStarSampleCount: 8_000,
                        maximumGalaxyProxies: 128,
                        stellarFieldSectorRadius: 4,
                        stellarFieldVerticalSectorRadius: 4,
                        maximumStellarPoints: 4_000);

                case CelestialStreamingQuality.High:
                    return new QualitySettings(
                        aggregateStarSampleCount: 30_000,
                        maximumGalaxyProxies: 1_024,
                        stellarFieldSectorRadius: 10,
                        stellarFieldVerticalSectorRadius: 10,
                        maximumStellarPoints: 20_000);

                default:
                    return new QualitySettings(
                        aggregateStarSampleCount: 18_000,
                        maximumGalaxyProxies: 512,
                        stellarFieldSectorRadius: 7,
                        stellarFieldVerticalSectorRadius: 7,
                        maximumStellarPoints: 10_000);
            }
        }

        private static LayerMask AssignDefaultLayerIfEmpty(
            LayerMask currentMask,
            string layerName)
        {
            if (currentMask.value != 0)
                return currentMask;

            var layer = LayerMask.NameToLayer(layerName);
            if (layer < 0)
                return currentMask;

            var result = new LayerMask
            {
                value = 1 << layer
            };

            return result;
        }

        private T GetOrAddComponent<T>()
            where T : Component
        {
            return GetOrAddComponent<T>(gameObject);
        }

        private static T GetOrAddComponent<T>(GameObject target)
            where T : Component
        {
            var component = target.GetComponent<T>();

            return component != null
                ? component
                : target.AddComponent<T>();
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

        private static Type FindUniversalAdditionalCameraDataType()
        {
            const string typeName =
                "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData";

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            for (var i = 0; i < assemblies.Length; i++)
            {
                var type = assemblies[i].GetType(typeName);

                if (type != null)
                    return type;
            }

            return null;
        }

        private static void SetEnumProperty(
            Component component,
            string propertyName,
            string enumValue)
        {
            var property = component.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance |
                BindingFlags.Public);

            if (property == null || !property.CanWrite)
                return;

            var value = Enum.Parse(
                property.PropertyType,
                enumValue);

            property.SetValue(component, value);
        }

        private static void SetBooleanProperty(
            Component component,
            string propertyName,
            bool value)
        {
            var property = component.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance |
                BindingFlags.Public);

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

            public float StellarFieldRadiusLightYears =>
                StellarFieldSectorRadius * 10f;

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
