using System;
using System.Collections.Generic;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Galaxy;
using SpaceEngine.Runtime.Utils;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Runtime.Streaming
{
    /// <summary>
    /// Renders the unresolved light of the active galaxy as a deterministic
    /// point cloud. These samples are an aggregate representation of distant
    /// stellar populations, not selectable solar-system records.
    ///
    /// Nearby individual systems remain the responsibility of
    /// GalaxySpaceStreamer, which uses real SolarSystemLocationData and can
    /// hand off exactly to a solar-system LOD.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UniverseGalaxyFieldRenderer))]
    public sealed class GalaxyStarfieldRenderer : MonoBehaviour
    {
        private const int MaximumInstancesPerDrawCall = 1023;
        private const ulong AggregatePointSalt = 0x47414C5F53544152UL;

        private readonly struct StarSample
        {
            public readonly double3 GalaxyLocalPositionLightYears;
            public readonly byte ColorBand;
            public readonly float Brightness;

            public StarSample(
                double3 galaxyLocalPositionLightYears,
                byte colorBand,
                float brightness)
            {
                GalaxyLocalPositionLightYears =
                    galaxyLocalPositionLightYears;
                ColorBand = colorBand;
                Brightness = brightness;
            }
        }

        [Header("References")]
        [SerializeField, HideInInspector] private SeamlessSpaceAnchor spaceAnchor;
        [SerializeField, HideInInspector] private Camera galaxyFrameCamera;
        [SerializeField, HideInInspector] private Mesh pointMesh;
        [SerializeField, HideInInspector] private Material pointMaterial;

        [Header("Galaxy-frame scale")]
        [SerializeField, HideInInspector] private LayerMask galaxyFrameLayer = 0;
        [SerializeField, HideInInspector, Min(0.000001f)]
        private float unityUnitsPerLightYear = 0.001f;

        [Header("Aggregate star field")]
        [SerializeField, HideInInspector, Range(1_000, 30_000)]
        private int aggregateSampleCount = 12_000;
        [SerializeField, HideInInspector, Min(1)]
        private int attemptsPerSample = 32;
        [SerializeField, HideInInspector, Min(0.0f)]
        private float unresolvedInnerRadiusLightYears = 150f;
        [SerializeField, HideInInspector, Range(0.25f, 4f)]
        private float starPixels = 1.35f;
        [SerializeField, HideInInspector, Min(0.00001f)]
        private float minimumPointDiameter = 0.0001f;
        [SerializeField, HideInInspector, Min(0.00001f)]
        private float maximumPointDiameter = 0.25f;
        [SerializeField, HideInInspector, Range(0.05f, 2f)]
        private float brightnessMultiplier = 1.0f;

        private readonly List<StarSample> _redSamples = new();
        private readonly List<StarSample> _orangeSamples = new();
        private readonly List<StarSample> _yellowSamples = new();
        private readonly List<StarSample> _blueSamples = new();

        private Matrix4x4[][] _redMatrices = Array.Empty<Matrix4x4[]>();
        private Matrix4x4[][] _orangeMatrices = Array.Empty<Matrix4x4[]>();
        private Matrix4x4[][] _yellowMatrices = Array.Empty<Matrix4x4[]>();
        private Matrix4x4[][] _blueMatrices = Array.Empty<Matrix4x4[]>();

        private MaterialPropertyBlock _propertyBlock;
        private Mesh _runtimePointMesh;
        private Material _runtimePointMaterial;
        private Texture2D _runtimePointTexture;
        private ulong _loadedGalaxySeed;

        internal void Configure(
            SeamlessSpaceAnchor anchor,
            Camera frameCamera,
            LayerMask frameLayer,
            int sampleCount,
            float resolvedStellarFieldRadiusLightYears)
        {
            var clampedInnerRadius = Mathf.Max(
                0f,
                resolvedStellarFieldRadiusLightYears);

            var changed =
                spaceAnchor != anchor ||
                galaxyFrameCamera != frameCamera ||
                galaxyFrameLayer.value != frameLayer.value ||
                aggregateSampleCount != sampleCount ||
                !Mathf.Approximately(
                    unresolvedInnerRadiusLightYears,
                    clampedInnerRadius);

            spaceAnchor = anchor;
            galaxyFrameCamera = frameCamera;
            galaxyFrameLayer = frameLayer;
            aggregateSampleCount = Mathf.Clamp(
                sampleCount,
                1_000,
                30_000);
            unresolvedInnerRadiusLightYears = clampedInnerRadius;

            if (changed)
                ForceRefresh();
        }

        private void Awake()
        {
            spaceAnchor ??= GetComponent<SeamlessSpaceAnchor>();
        }

        private void OnValidate()
        {
            aggregateSampleCount = Mathf.Clamp(
                aggregateSampleCount,
                1_000,
                30_000);

            attemptsPerSample = Mathf.Max(1, attemptsPerSample);
            unresolvedInnerRadiusLightYears = Mathf.Max(
                0f,
                unresolvedInnerRadiusLightYears);
            unityUnitsPerLightYear = Mathf.Max(
                0.000001f,
                unityUnitsPerLightYear);
            minimumPointDiameter = Mathf.Max(
                0.00001f,
                minimumPointDiameter);
            maximumPointDiameter = Mathf.Max(
                minimumPointDiameter,
                maximumPointDiameter);
        }

        private void Update()
        {
            if (spaceAnchor == null || !spaceAnchor.IsConfigured)
                return;

            if (_loadedGalaxySeed != spaceAnchor.Galaxy.Seed)
            {
                RebuildSamples(spaceAnchor.Galaxy);
                _loadedGalaxySeed = spaceAnchor.Galaxy.Seed;
            }

            RenderSamples();
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

        /// <summary>
        /// Rebuilds the deterministic aggregate cloud for the active galaxy.
        /// Call after changing visual density settings from code.
        /// </summary>
        public void ForceRefresh()
        {
            _loadedGalaxySeed = 0UL;
        }

        private void RebuildSamples(in GalaxyData galaxy)
        {
            _redSamples.Clear();
            _orangeSamples.Clear();
            _yellowSamples.Clear();
            _blueSamples.Clear();

            var random = new QuantumRandom(
                GalaxyIDUtility.Combine(
                    galaxy.Seed,
                    AggregatePointSalt));

            for (var sampleIndex = 0;
                 sampleIndex < aggregateSampleCount;
                 sampleIndex++)
            {
                if (!TryCreateSample(
                        galaxy,
                        ref random,
                        out var sample))
                {
                    continue;
                }

                AddSampleToColorBand(sample);
            }

            EnsureMatrixStorage(_redSamples.Count, ref _redMatrices);
            EnsureMatrixStorage(_orangeSamples.Count, ref _orangeMatrices);
            EnsureMatrixStorage(_yellowSamples.Count, ref _yellowMatrices);
            EnsureMatrixStorage(_blueSamples.Count, ref _blueMatrices);
        }

        private bool TryCreateSample(
            in GalaxyData galaxy,
            ref QuantumRandom random,
            out StarSample sample)
        {
            var verticalExtent = GetVerticalExtent(galaxy);

            for (var attempt = 0; attempt < attemptsPerSample; attempt++)
            {
                var x = random.NextDouble(
                    -galaxy.RadiusLightYears,
                    galaxy.RadiusLightYears);

                var z = random.NextDouble(
                    -galaxy.RadiusLightYears,
                    galaxy.RadiusLightYears);

                var planarRadius = math.length(new double2(x, z));
                if (planarRadius > galaxy.RadiusLightYears)
                    continue;

                // A triangular distribution keeps most unresolved light near
                // the disk/ellipsoid centre while retaining vertical extent.
                var vertical =
                    random.NextDouble(-1.0, 1.0) +
                    random.NextDouble(-1.0, 1.0) +
                    random.NextDouble(-1.0, 1.0);

                var position = new double3(
                    x,
                    vertical * verticalExtent / 3.0,
                    z);

                var density = GalaxyDensityUtility.GetDensity(
                    galaxy,
                    position);

                if (density <= 0.0 || random.NextDouble() > density)
                    continue;

                var colorRoll = random.NextDouble();
                var colorBand = colorRoll < 0.46 ? (byte)0 :
                                colorRoll < 0.75 ? (byte)1 :
                                colorRoll < 0.94 ? (byte)2 : (byte)3;

                var brightness = (float)random.NextDouble(0.45, 1.15);
                sample = new StarSample(position, colorBand, brightness);
                return true;
            }

            sample = default;
            return false;
        }

        private static double GetVerticalExtent(in GalaxyData galaxy)
        {
            switch (galaxy.Type)
            {
                case GalaxyType.Elliptical:
                    return Math.Max(
                        galaxy.RadiusLightYears * galaxy.Ellipticity,
                        galaxy.DiskThicknessLightYears);

                case GalaxyType.Dwarf:
                case GalaxyType.Irregular:
                    return Math.Max(
                        galaxy.RadiusLightYears * 0.35,
                        galaxy.DiskThicknessLightYears);

                default:
                    return Math.Max(
                        galaxy.DiskThicknessLightYears * 2.0,
                        100.0);
            }
        }

        private void AddSampleToColorBand(in StarSample sample)
        {
            switch (sample.ColorBand)
            {
                case 0:
                    _redSamples.Add(sample);
                    break;
                case 1:
                    _orangeSamples.Add(sample);
                    break;
                case 2:
                    _yellowSamples.Add(sample);
                    break;
                default:
                    _blueSamples.Add(sample);
                    break;
            }
        }

        private void RenderSamples()
        {
            var mesh = ResolvePointMesh();
            var material = ResolvePointMaterial();
            var camera = ResolveCamera();

            if (mesh == null || material == null || camera == null)
                return;

            _propertyBlock ??= new MaterialPropertyBlock();

            var anchorPosition = spaceAnchor.GalaxyLocalPositionLightYears;
            var cameraRotation = camera.transform.rotation;

            RenderColorBand(
                _redSamples,
                ref _redMatrices,
                new Color(1f, 0.34f, 0.22f),
                anchorPosition,
                camera,
                cameraRotation,
                mesh,
                material);

            RenderColorBand(
                _orangeSamples,
                ref _orangeMatrices,
                new Color(1f, 0.66f, 0.30f),
                anchorPosition,
                camera,
                cameraRotation,
                mesh,
                material);

            RenderColorBand(
                _yellowSamples,
                ref _yellowMatrices,
                new Color(1f, 0.93f, 0.68f),
                anchorPosition,
                camera,
                cameraRotation,
                mesh,
                material);

            RenderColorBand(
                _blueSamples,
                ref _blueMatrices,
                new Color(0.66f, 0.84f, 1f),
                anchorPosition,
                camera,
                cameraRotation,
                mesh,
                material);
        }

        private void RenderColorBand(
            List<StarSample> samples,
            ref Matrix4x4[][] matrices,
            Color color,
            double3 anchorPosition,
            Camera camera,
            Quaternion cameraRotation,
            Mesh mesh,
            Material material)
        {
            if (samples.Count == 0)
                return;

            var visibleCount = 0;

            for (var i = 0; i < samples.Count; i++)
            {
                var sample = samples[i];
                var relativeLightYears =
                    sample.GalaxyLocalPositionLightYears - anchorPosition;

                var distanceLightYears = math.length(relativeLightYears);
                if (distanceLightYears < unresolvedInnerRadiusLightYears)
                    continue;

                var position = ToUnityPosition(relativeLightYears);
                if (!IsInCameraFrustum(camera, position))
                    continue;

                var batchIndex = visibleCount / MaximumInstancesPerDrawCall;
                var instanceIndex = visibleCount % MaximumInstancesPerDrawCall;

                var diameter = GetPointDiameter(
                    camera,
                    position.magnitude,
                    sample.Brightness);

                matrices[batchIndex][instanceIndex] = Matrix4x4.TRS(
                    position,
                    cameraRotation,
                    Vector3.one * diameter);

                visibleCount++;
            }

            if (visibleCount == 0)
                return;

            _propertyBlock.Clear();
            var scaledColor = color * brightnessMultiplier;
            _propertyBlock.SetColor("_Color", scaledColor);
            _propertyBlock.SetColor("_BaseColor", scaledColor);
            _propertyBlock.SetColor("_EmissionColor", scaledColor);

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
                    ReferenceFrameLayerUtility
                        .GetSingleLayerIndexOrDefault(galaxyFrameLayer),
                    null,
                    LightProbeUsage.Off,
                    null);

                drawn += count;
            }
        }

        private float GetPointDiameter(
            Camera camera,
            float distance,
            float brightness)
        {
            var halfFovRadians =
                camera.fieldOfView * Mathf.Deg2Rad * 0.5f;

            var unitsPerPixel =
                2f * Mathf.Max(distance, camera.nearClipPlane) *
                Mathf.Tan(halfFovRadians) /
                Mathf.Max(1, Screen.height);

            return Mathf.Clamp(
                unitsPerPixel * starPixels * brightness,
                minimumPointDiameter,
                maximumPointDiameter);
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

        private Vector3 ToUnityPosition(double3 relativeLightYears)
        {
            return new Vector3(
                (float)(relativeLightYears.x * unityUnitsPerLightYear),
                (float)(relativeLightYears.y * unityUnitsPerLightYear),
                (float)(relativeLightYears.z * unityUnitsPerLightYear));
        }

        private Camera ResolveCamera()
        {
            if (galaxyFrameCamera != null)
                return galaxyFrameCamera;

            return Camera.main;
        }

        private Mesh ResolvePointMesh()
        {
            if (pointMesh != null)
                return pointMesh;

            if (_runtimePointMesh != null)
                return _runtimePointMesh;

            _runtimePointMesh = new Mesh
            {
                name = "Runtime Billboard Point"
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
            var material = pointMaterial != null
                ? pointMaterial
                : CreateRuntimePointMaterialIfNeeded();

            if (material != null)
                material.enableInstancing = true;

            return material;
        }

        private Material CreateRuntimePointMaterialIfNeeded()
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
                name = "Runtime Galaxy Star Point Material",
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
                _runtimePointMaterial.SetFloat("_Intensity", 0.65f);

            if (_runtimePointMaterial.HasProperty("_Softness"))
                _runtimePointMaterial.SetFloat("_Softness", 3.5f);

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
                name = "Runtime Galaxy Star Point Texture",
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
                matrices[i] = new Matrix4x4[MaximumInstancesPerDrawCall];
        }
    }
}
