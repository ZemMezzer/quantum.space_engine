using System;
using System.Collections;
using System.Reflection;
using SpaceEngine.Rendering.Runtime;
using SpaceEngine.Rendering.Runtime.Anchors;
using SpaceEngine.Rendering.Runtime.SolarSystem;
using SpaceEngine.Runtime.Core;
using SpaceEngine.Runtime.Streaming;
using SpaceEngine.Runtime.Streaming.Runtime.Anchors;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SpaceEngine.Rendering
{
    /// <summary>
    /// Scene bridge for the 3D celestial camera. This component owns only the
    /// cameras and scene roots; authored streaming policy is supplied by a
    /// CelestialRenderConfiguration asset and content by SpaceEngine.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CelestialRenderer3D : CelestialRenderer
    {
        [Header("Required")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private CelestialRenderConfiguration renderConfiguration;

        [Header("Single celestial camera")]
        [SerializeField]
        private LayerMask celestialFrameLayer;

        [SerializeField, Min(1_000.0f)]
        private float celestialFrameFarClipPlane = 2_000_000.0f;

        [SerializeField, HideInInspector]
        private Camera celestialFrameCamera;
        [SerializeField, HideInInspector] private Transform celestialRoot;

        private CelestialRenderRuntime _runtime;
        private bool _hasReportedInvalidConfiguration;

        public SeamlessSpaceStreamingService StreamingService =>
            _runtime?.StreamingService;

        public SolarSystemScaledSpaceRenderer SolarRenderer =>
            _runtime?.SolarRenderer;

        public Camera PlayerCamera => playerCamera;
        public Camera CelestialCamera => celestialFrameCamera;
        public override bool IsReady => _runtime != null && celestialFrameCamera != null;

        private void Reset()
        {
            ResolvePlayerCamera();
            AssignDefaultLayer();
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
                return;

            celestialFrameFarClipPlane = Mathf.Max(1_000.0f, celestialFrameFarClipPlane);
            ResolvePlayerCamera();
            AssignDefaultLayer();

            if (playerCamera == null ||
                !ReferenceFrameLayerUtility.TryGetSingleLayerIndex(celestialFrameLayer, out _))
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
            SetupPresentation();
        }

        protected override void OnTickStreaming(float unscaledTime)
        {
            _runtime?.UpdateStreaming(unscaledTime);
        }

        protected override void OnTickVisuals(float deltaTime, double simulationTimeSeconds)
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

            if (renderConfiguration == null)
            {
                ReportInvalidConfiguration(
                    "CelestialRenderer3D requires a CelestialRenderConfiguration asset.");
                return;
            }

            if (Engine?.Configuration == null || !(Anchor is CelestialAnchor3D anchor3D))
                return;

            _hasReportedInvalidConfiguration = false;
            _runtime ??= new CelestialRenderRuntime(
                celestialRoot,
                anchor3D.Backend,
                Engine.Configuration,
                renderConfiguration);

            _runtime.Configure(
                celestialFrameCamera,
                celestialFrameLayer);
            _runtime.Initialize();
        }

        private bool SetupPresentation()
        {
            celestialFrameFarClipPlane = Mathf.Max(1_000.0f, celestialFrameFarClipPlane);
            ResolvePlayerCamera();
            AssignDefaultLayer();

            if (playerCamera == null ||
                !ReferenceFrameLayerUtility.TryGetSingleLayerIndex(celestialFrameLayer, out _))
            {
                ReportInvalidConfiguration(
                    "CelestialRenderer3D requires a player camera and exactly one Celestial Frame layer.");
                return false;
            }

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

            celestialFrameCamera.transform.SetPositionAndRotation(Vector3.zero, playerCamera.transform.rotation);
            celestialFrameCamera.orthographic = playerCamera.orthographic;
            celestialFrameCamera.fieldOfView = playerCamera.fieldOfView;
            celestialFrameCamera.orthographicSize = playerCamera.orthographicSize;
            celestialFrameCamera.nearClipPlane = 0.00001f;
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
            if (oldSolarCamera != null && oldSolarCamera.GetComponent<Camera>() != celestialFrameCamera)
                DestroyObject(oldSolarCamera.gameObject);
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
            var additionalCameraDataType = typeof(UniversalAdditionalCameraData);
            if (celestialFrameCamera == null || playerCamera == null)
                return;

            var celestialData = GetOrAddComponent(celestialFrameCamera.gameObject, additionalCameraDataType);
            var playerData = GetOrAddComponent(playerCamera.gameObject, additionalCameraDataType);
            if (celestialData == null || playerData == null)
                return;

            SetEnumProperty(celestialData, "renderType", "Base");
            SetEnumProperty(celestialData, "requiresColorOption", "On");
            SetEnumProperty(playerData, "renderType", "Overlay");
            SetBooleanProperty(playerData, "clearDepth", true);

            var stackProperty = additionalCameraDataType.GetProperty("cameraStack", BindingFlags.Instance | BindingFlags.Public);
            var stack = stackProperty?.GetValue(celestialData) as IList;
            if (stack == null)
                return;

            stack.Clear();
            stack.Add(playerCamera);
        }

        private void ReportInvalidConfiguration(string message)
        {
            if (_hasReportedInvalidConfiguration)
                return;

            _hasReportedInvalidConfiguration = true;
            Debug.LogError(message, this);
        }

        private static Component GetOrAddComponent(GameObject target, Type componentType)
        {
            var component = target.GetComponent(componentType);
            return component != null ? component : target.AddComponent(componentType);
        }

        private static void SetEnumProperty(Component component, string propertyName, string enumValue)
        {
            var property = component.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property?.CanWrite != true)
                return;

            property.SetValue(component, Enum.Parse(property.PropertyType, enumValue));
        }

        private static void SetBooleanProperty(Component component, string propertyName, bool value)
        {
            var property = component.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property?.CanWrite == true && property.PropertyType == typeof(bool))
                property.SetValue(component, value);
        }
    }
}
