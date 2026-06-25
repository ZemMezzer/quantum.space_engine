using System;
using System.Collections.Generic;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Galaxy;
using SpaceEngine.Runtime.Generation.Universe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Runtime.Streaming
{
    /// <summary>
    /// Renders real procedurally generated galaxies from nearby universe
    /// sectors as distant galaxy proxies. Each proxy corresponds to one
    /// GalaxyLocationData and therefore has the same universe position as the
    /// full galaxy that can later become active.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SeamlessSpaceAnchor))]
    public sealed class UniverseGalaxyFieldRenderer : MonoBehaviour
    {
        private const int MaximumInstancesPerDrawCall = 1023;

        [Header("References")]
        [SerializeField, HideInInspector] private SeamlessSpaceAnchor spaceAnchor;
        [SerializeField, HideInInspector] private Camera universeFrameCamera;
        [SerializeField, HideInInspector] private Mesh proxyMesh;
        [SerializeField, HideInInspector] private Material proxyMaterial;

        [Header("Universe-frame scale")]
        [SerializeField, HideInInspector] private LayerMask universeFrameLayer = 0;
        [SerializeField, HideInInspector, Min(0.000000001f)]
        private float unityUnitsPerLightYear = 0.000001f;

        [Header("Universe sector streaming")]
        [SerializeField, HideInInspector, Range(0, 4)]
        private int horizontalSectorRadius = 1;
        [SerializeField, HideInInspector, Range(0, 2)]
        private int verticalSectorRadius = 1;
        [SerializeField, HideInInspector] private bool useCircularFootprint = true;
        [SerializeField, HideInInspector, Range(16, 4_096)]
        private int maximumGalaxyProxies = 512;

        [Header("Galaxy proxy appearance")]
        [SerializeField, HideInInspector, Range(0.25f, 12f)]
        private float minimumGalaxyPixels = 1.5f;
        [SerializeField, HideInInspector, Min(0.000001f)]
        private float maximumGalaxyDiameter = 4f;
        [SerializeField, HideInInspector, Range(0.05f, 4f)]
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
                universeFrameCamera != frameCamera ||
                universeFrameLayer.value != frameLayer.value ||
                maximumGalaxyProxies != maximumProxies;

            spaceAnchor = anchor;
            universeFrameCamera = frameCamera;
            universeFrameLayer = frameLayer;
            maximumGalaxyProxies = Mathf.Clamp(
                maximumProxies,
                16,
                4_096);

            if (changed)
                ForceRefresh();
        }

        private void Awake()
        {
            spaceAnchor ??= GetComponent<SeamlessSpaceAnchor>();
        }

        private void OnValidate()
        {
            horizontalSectorRadius = Mathf.Max(0, horizontalSectorRadius);
            verticalSectorRadius = Mathf.Max(0, verticalSectorRadius);
            maximumGalaxyProxies = Mathf.Clamp(
                maximumGalaxyProxies,
                16,
                4_096);
            unityUnitsPerLightYear = Mathf.Max(
                0.000000001f,
                unityUnitsPerLightYear);
            maximumGalaxyDiameter = Mathf.Max(
                0.000001f,
                maximumGalaxyDiameter);
        }

        private void Update()
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

        private void OnDestroy()
        {
            if (_runtimeProxyMesh != null)
                Destroy(_runtimeProxyMesh);

            if (_runtimeProxyMaterial != null)
                Destroy(_runtimeProxyMaterial);
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
                        .GetSingleLayerIndexOrDefault(universeFrameLayer),
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
            if (universeFrameCamera != null)
                return universeFrameCamera;

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
                enableInstancing = true
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
}
