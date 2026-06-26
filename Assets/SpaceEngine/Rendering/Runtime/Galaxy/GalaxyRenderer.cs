using System;
using System.Collections.Generic;
using SpaceEngine.Runtime.Content;
using SpaceEngine.Rendering.Content;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Galaxy;
using SpaceEngine.Runtime.Streaming;
using SpaceEngine.Runtime.Streaming.Runtime.Anchors;
using SpaceEngine.Runtime.Utils;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Rendering.Runtime.Galaxy
{
        public sealed class GalaxyRenderer
    {
        private static readonly int CoreColor = Shader.PropertyToID("_CoreColor");
        private static readonly int DiskColor = Shader.PropertyToID("_DiskColor");
        private static readonly int NebulaColor = Shader.PropertyToID("_NebulaColor");
        private static readonly int HaloColor = Shader.PropertyToID("_HaloColor");
        private static readonly int Seed = Shader.PropertyToID("_Seed");
        private static readonly int DustStrength = Shader.PropertyToID("_DustStrength");
        private static readonly int Opacity = Shader.PropertyToID("_Opacity");
        private static readonly int Brightness = Shader.PropertyToID("_Brightness");
        private static readonly int GasDensity = Shader.PropertyToID("_GasDensity");
        private static readonly int Irregularity = Shader.PropertyToID("_Irregularity");
        private static readonly int RingWidth = Shader.PropertyToID("_RingWidth");
        private static readonly int RingRadius = Shader.PropertyToID("_RingRadius");
        private static readonly int Ellipticity = Shader.PropertyToID("_Ellipticity");
        private static readonly int SpiralArmTightness = Shader.PropertyToID("_SpiralArmTightness");
        private static readonly int BarLength = Shader.PropertyToID("_BarLength");
        private static readonly int SpiralArmCount = Shader.PropertyToID("_SpiralArmCount");
        private static readonly int DiskRadiusMultiplier = Shader.PropertyToID("_DiskRadiusMultiplier");
        private static readonly int DiskThickness = Shader.PropertyToID("_DiskThickness");
        private static readonly int CoreRadius = Shader.PropertyToID("_CoreRadius");
        private static readonly int GalaxyType = Shader.PropertyToID("_GalaxyType");
        private static readonly int VolumeRadius = Shader.PropertyToID("_VolumeRadius");
        private static readonly int RaymarchSteps = Shader.PropertyToID("_RaymarchSteps");
        private static readonly int WorldToGalaxyShape = Shader.PropertyToID("_WorldToGalaxyShape");
        private static readonly int CameraGalaxyPosition = Shader.PropertyToID("_CameraGalaxyPosition");
        private const float VOLUME_RADIUS_IN_GALAXY_RADII = 1.18f;

        private SeamlessSpaceAnchor spaceAnchor;
        private SpaceEngineConfiguration configuration;
        private CelestialRenderConfiguration renderConfiguration;
        private Camera celestialCamera;
        private LayerMask celestialLayer = 0;
        private bool enabled = true;
        private int raymarchSteps = 40;

        private Mesh _runtimeFullscreenMesh;
        private Material _runtimeMaterial;
        private MaterialPropertyBlock _propertyBlock;

        internal void Configure(
            SeamlessSpaceAnchor anchor,
            SpaceEngineConfiguration contentConfiguration,
            CelestialRenderConfiguration visualConfiguration,
            Camera frameCamera,
            LayerMask frameLayer,
            bool enableRenderer,
            int gasRaymarchSteps)
        {
            spaceAnchor = anchor;
            configuration = contentConfiguration;
            renderConfiguration = visualConfiguration;
            celestialCamera = frameCamera;
            celestialLayer = frameLayer;
            enabled = enableRenderer;
            raymarchSteps = Mathf.Clamp(gasRaymarchSteps, 8, 96);
        }

        public void Tick()
        {
            if (!enabled ||
                spaceAnchor == null ||
                !spaceAnchor.IsConfigured)
            {
                return;
            }

            var mesh = ResolveFullscreenMesh();
            var material = ResolveMaterial();
            var camera = ResolveCamera();

            if (mesh == null || material == null || camera == null)
                return;

            var galaxy = spaceAnchor.Galaxy;
            var radiusLightYears = Math.Max(1.0, galaxy.RadiusLightYears);
            var cameraPosition = ToShapeLocalPosition(
                spaceAnchor.GalaxyLocalPositionLightYears,
                galaxy.RotationRadians);

            var shapeCameraPosition = new Vector3(
                (float)(cameraPosition.x / radiusLightYears),
                (float)(cameraPosition.y / radiusLightYears),
                (float)(cameraPosition.z / radiusLightYears));

            // The shader reconstructs its ray from Unity's per-camera GPU
            // matrices, so it always uses the same actual projection as the
            // celestial camera. We only provide the fixed galaxy orientation.
            var worldToGalaxyShape = CreateWorldToGalaxyShapeMatrix(
                galaxy.RotationRadians);

            var galaxyRenderer =
                ContentRendererSelection.SelectGalaxyRendererOrNull(
                    renderConfiguration.GalaxyRenderers,
                    galaxy.Entity);
            if (galaxyRenderer == null)
                return;

            var visual = galaxyRenderer.GetVisualData(galaxy);
            var gasDensityFactor = Mathf.Clamp01(0.35f + visual.GasDensity * 1.8f);

            _propertyBlock ??= new MaterialPropertyBlock();
            _propertyBlock.Clear();
            _propertyBlock.SetVector(
                CameraGalaxyPosition,
                shapeCameraPosition);
            _propertyBlock.SetMatrix(
                WorldToGalaxyShape,
                worldToGalaxyShape);
            _propertyBlock.SetFloat(RaymarchSteps, raymarchSteps);
            _propertyBlock.SetFloat(VolumeRadius, VOLUME_RADIUS_IN_GALAXY_RADII);
            _propertyBlock.SetFloat(GalaxyType, visual.ShaderMorphology);
            _propertyBlock.SetFloat(
                CoreRadius,
                Mathf.Clamp(
                    (float)(galaxy.CoreRadiusLightYears / radiusLightYears),
                    0.005f,
                    0.85f));
            _propertyBlock.SetFloat(
                DiskThickness,
                Mathf.Clamp(
                    (float)(galaxy.DiskThicknessLightYears / radiusLightYears) *
                    visual.GasDiskThicknessMultiplier,
                    0.0025f,
                    1.0f));
            _propertyBlock.SetFloat(
                DiskRadiusMultiplier,
                visual.GasDiskRadiusMultiplier);
            _propertyBlock.SetFloat(
                SpiralArmCount,
                Mathf.Max(1.0f, visual.SpiralArmCount));
            _propertyBlock.SetFloat(
                SpiralArmTightness,
                Mathf.Max(0.0f, visual.SpiralArmTightness));
            _propertyBlock.SetFloat(
                BarLength,
                Mathf.Clamp(
                    visual.BarLengthRadiusMultiplier,
                    0.005f,
                    1.5f));
            _propertyBlock.SetFloat(
                Ellipticity,
                Mathf.Clamp(visual.Ellipticity, 0.05f, 2.0f));
            _propertyBlock.SetFloat(
                RingRadius,
                Mathf.Clamp(
                    visual.RingRadiusMultiplier,
                    0.005f,
                    1.5f));
            _propertyBlock.SetFloat(
                RingWidth,
                Mathf.Clamp(
                    visual.RingWidthRadiusMultiplier,
                    0.0025f,
                    1.0f));
            _propertyBlock.SetFloat(
                Irregularity,
                Mathf.Clamp01(visual.Irregularity));
            _propertyBlock.SetFloat(GasDensity, gasDensityFactor);
            _propertyBlock.SetFloat(Brightness, visual.GasBrightness);
            _propertyBlock.SetFloat(Opacity, visual.GasOpacity);
            _propertyBlock.SetFloat(DustStrength, visual.GasDustStrength);
            _propertyBlock.SetFloat(
                Seed,
                (float)((galaxy.Seed & 0xFFFFUL) / 65535.0));
            _propertyBlock.SetColor(CoreColor, visual.CoreColor);
            _propertyBlock.SetColor(DiskColor, visual.DiskColor);
            _propertyBlock.SetColor(NebulaColor, visual.NebulaColor);
            _propertyBlock.SetColor(HaloColor, visual.HaloColor);

            // The vertex shader generates clip-space coordinates itself.
            // Keeping this mesh near the celestial camera only gives Unity a
            // sensible bounds centre for draw-call culling.
            var matrix = Matrix4x4.TRS(
                camera.transform.position +
                camera.transform.forward * (camera.nearClipPlane * 2.0f),
                Quaternion.identity,
                Vector3.one);

            Graphics.DrawMesh(
                mesh,
                matrix,
                material,
                ReferenceFrameLayerUtility.GetSingleLayerIndexOrDefault(
                    celestialLayer),
                null,
                0,
                _propertyBlock,
                ShadowCastingMode.Off,
                false,
                null,
                LightProbeUsage.Off);
        }

        public void Dispose()
        {
            if (_runtimeFullscreenMesh != null)
                UnityEngine.Object.Destroy(_runtimeFullscreenMesh);

            if (_runtimeMaterial != null)
                UnityEngine.Object.Destroy(_runtimeMaterial);
        }

        private Mesh ResolveFullscreenMesh()
        {
            if (_runtimeFullscreenMesh != null)
                return _runtimeFullscreenMesh;

            _runtimeFullscreenMesh = new Mesh
            {
                name = "Runtime Galaxy Gas Fullscreen Mesh"
            };

            _runtimeFullscreenMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0.0f),
                new Vector3( 0.5f, -0.5f, 0.0f),
                new Vector3( 0.5f,  0.5f, 0.0f),
                new Vector3(-0.5f,  0.5f, 0.0f)
            };

            _runtimeFullscreenMesh.uv = new[]
            {
                new Vector2(0.0f, 0.0f),
                new Vector2(1.0f, 0.0f),
                new Vector2(1.0f, 1.0f),
                new Vector2(0.0f, 1.0f)
            };

            _runtimeFullscreenMesh.triangles = new[]
            {
                0, 1, 2,
                0, 2, 3
            };

            _runtimeFullscreenMesh.bounds = new Bounds(
                Vector3.zero,
                Vector3.one * 1_000_000.0f);

            return _runtimeFullscreenMesh;
        }

        private Material ResolveMaterial()
        {
            if (_runtimeMaterial != null)
                return _runtimeMaterial;

            var shader = Shader.Find(
                "SpaceEngine/Streaming/Galaxy Gas Volume");

            if (shader == null)
                return null;

            _runtimeMaterial = new Material(shader)
            {
                name = "Runtime Galaxy Gas Volume",
                hideFlags = HideFlags.HideAndDontSave
            };

            return _runtimeMaterial;
        }

        private Camera ResolveCamera()
        {
            return celestialCamera != null
                ? celestialCamera
                : Camera.main;
        }

        private static double3 ToShapeLocalPosition(
            double3 galaxyLocalPosition,
            double rotationRadians)
        {
            var cosine = Math.Cos(-rotationRadians);
            var sine = Math.Sin(-rotationRadians);

            return new double3(
                galaxyLocalPosition.x * cosine -
                galaxyLocalPosition.z * sine,
                galaxyLocalPosition.y,
                galaxyLocalPosition.x * sine +
                galaxyLocalPosition.z * cosine);
        }

        private static Matrix4x4 CreateWorldToGalaxyShapeMatrix(
            double rotationRadians)
        {
            var cosine = (float)Math.Cos(-rotationRadians);
            var sine = (float)Math.Sin(-rotationRadians);
            var matrix = Matrix4x4.identity;

            // This is exactly the same XZ rotation as
            // ToShapeLocalPosition above, expressed as a shader matrix.
            matrix.m00 = cosine;
            matrix.m02 = -sine;
            matrix.m20 = sine;
            matrix.m22 = cosine;
            return matrix;
        }

    }

public sealed class GalaxyStarfieldRenderer
    {
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Color1 = Shader.PropertyToID("_Color");
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int Surface = Shader.PropertyToID("_Surface");
        private static readonly int Blend = Shader.PropertyToID("_Blend");
        private static readonly int ZWrite = Shader.PropertyToID("_ZWrite");
        private static readonly int Cull = Shader.PropertyToID("_Cull");
        private static readonly int Intensity = Shader.PropertyToID("_Intensity");
        private static readonly int Softness = Shader.PropertyToID("_Softness");
        private const int MAXIMUM_INSTANCES_PER_DRAW_CALL = 1023;
        private const ulong AGGREGATE_POINT_SALT = 0x47414C5F53544152UL;
        private const float STAR_PIXELS = 1.35f;
        private const float MINIMUM_POINT_DIAMETER = 0.0001f;
        private const float MAXIMUM_POINT_DIAMETER = 0.25f;
        private const float BRIGHTNESS_MULTIPLIER = 1.0f;
        private const float UNITY_UNITS_PER_LIGHT_YEAR = 0.001f;

        private readonly struct StarSample
        {
            public readonly double3 GalaxyLocalPositionLightYears;
            public readonly float Brightness;

            public StarSample(
                double3 galaxyLocalPositionLightYears,
                float brightness)
            {
                GalaxyLocalPositionLightYears =
                    galaxyLocalPositionLightYears;
                Brightness = brightness;
            }
        }
        
        private SeamlessSpaceAnchor spaceAnchor;
        private SpaceEngineConfiguration configuration;
        private CelestialRenderConfiguration renderConfiguration;
        private Camera celestialCamera;
        private Mesh pointMesh;
        private Material pointMaterial;
        private LayerMask celestialLayer = 0;
        private int aggregateSampleCount = 12_000;
        private float unresolvedInnerRadiusLightYears = 150f;

        private readonly List<StarSample> _samples = new();
        private Matrix4x4[][] _matrices = Array.Empty<Matrix4x4[]>();

        private MaterialPropertyBlock _propertyBlock;
        private Mesh _runtimePointMesh;
        private Material _runtimePointMaterial;
        private Texture2D _runtimePointTexture;
        private ulong _loadedGalaxySeed;

        private Camera _camera;
        private Camera Camera
        {
            get
            {
                if (_camera == null)
                {
                    _camera = Camera.main;
                }
                return _camera;
            }
        }

        internal void Configure(
            SeamlessSpaceAnchor anchor,
            SpaceEngineConfiguration contentConfiguration,
            CelestialRenderConfiguration visualConfiguration,
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
                configuration != contentConfiguration ||
                celestialCamera != frameCamera ||
                celestialLayer.value != frameLayer.value ||
                aggregateSampleCount != sampleCount ||
                !Mathf.Approximately(
                    unresolvedInnerRadiusLightYears,
                    clampedInnerRadius);

            spaceAnchor = anchor;
            configuration = contentConfiguration;
            renderConfiguration = visualConfiguration;
            celestialCamera = frameCamera;
            celestialLayer = frameLayer;
            aggregateSampleCount = Mathf.Clamp(
                sampleCount,
                1_000,
                30_000);
            unresolvedInnerRadiusLightYears = clampedInnerRadius;

            if (changed)
                ForceRefresh();
        }



        public void Tick()
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

        public void Dispose()
        {
            if (_runtimePointMesh != null)
                UnityEngine.Object.Destroy(_runtimePointMesh);

            if (_runtimePointMaterial != null)
                UnityEngine.Object.Destroy(_runtimePointMaterial);

            if (_runtimePointTexture != null)
                UnityEngine.Object.Destroy(_runtimePointTexture);
        }

        /// <summary>
        /// Rebuilds the deterministic aggregate cloud for the active galaxy.
        /// Call after changing visual density settings from code.
        /// </summary>
        private void ForceRefresh()
        {
            _loadedGalaxySeed = 0UL;
        }

        private void RebuildSamples(in GalaxyData galaxy)
        {
            _samples.Clear();

            var random = new QuantumRandom(
                GalaxyIDUtility.Combine(
                    galaxy.Seed,
                    AGGREGATE_POINT_SALT));
            var renderer =
                ContentRendererSelection.SelectGalaxyRendererOrNull(
                    renderConfiguration.GalaxyRenderers,
                    galaxy.Entity);
            if (renderer == null)
                return;

            for (var sampleIndex = 0;
                 sampleIndex < aggregateSampleCount;
                 sampleIndex++)
            {
                if (!renderer.TryCreateExternalStarSample(
                        galaxy,
                        ref random,
                        out var sample))
                {
                    continue;
                }

                _samples.Add(new StarSample(
                    sample.GalaxyLocalPositionLightYears,
                    sample.Brightness));
            }

            EnsureMatrixStorage(_samples.Count, ref _matrices);
        }


        private void RenderSamples()
        {
            if (_samples.Count == 0)
                return;

            var mesh = ResolvePointMesh();
            var material = ResolvePointMaterial();
            var camera = ResolveCamera();

            if (mesh == null || material == null || camera == null)
                return;

            var anchorPosition = spaceAnchor.GalaxyLocalPositionLightYears;
            var cameraRotation = camera.transform.rotation;
            var visibleCount = 0;

            for (var i = 0; i < _samples.Count; i++)
            {
                var sample = _samples[i];
                var relativeLightYears =
                    sample.GalaxyLocalPositionLightYears - anchorPosition;

                if (math.length(relativeLightYears) <
                    unresolvedInnerRadiusLightYears)
                {
                    continue;
                }

                var position = ToUnityPosition(relativeLightYears);
                if (!IsInCameraFrustum(camera, position))
                    continue;

                var batchIndex = visibleCount / MAXIMUM_INSTANCES_PER_DRAW_CALL;
                var instanceIndex = visibleCount % MAXIMUM_INSTANCES_PER_DRAW_CALL;
                var diameter = GetPointDiameter(
                    camera,
                    position.magnitude,
                    sample.Brightness);

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
            var galaxyRenderer =
                ContentRendererSelection.SelectGalaxyRendererOrNull(
                    renderConfiguration.GalaxyRenderers,
                    spaceAnchor.Galaxy.Entity);
            if (galaxyRenderer == null)
                return;

            var visual = galaxyRenderer.GetVisualData(spaceAnchor.Galaxy);
            var color = visual.ExternalStarfieldColor * BRIGHTNESS_MULTIPLIER;
            _propertyBlock.SetColor(Color1, color);
            _propertyBlock.SetColor(BaseColor, color);
            _propertyBlock.SetColor(EmissionColor, color);

            var drawn = 0;
            for (var batchIndex = 0;
                 batchIndex < _matrices.Length && drawn < visibleCount;
                 batchIndex++)
            {
                var count = Mathf.Min(
                    MAXIMUM_INSTANCES_PER_DRAW_CALL,
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
                    ReferenceFrameLayerUtility.GetSingleLayerIndexOrDefault(
                        celestialLayer),
                    null,
                    LightProbeUsage.Off);

                drawn += count;
            }
        }

        private static float GetPointDiameter(
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
                unitsPerPixel * STAR_PIXELS * brightness,
                MINIMUM_POINT_DIAMETER,
                MAXIMUM_POINT_DIAMETER);
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
                (float)(relativeLightYears.x * UNITY_UNITS_PER_LIGHT_YEAR),
                (float)(relativeLightYears.y * UNITY_UNITS_PER_LIGHT_YEAR),
                (float)(relativeLightYears.z * UNITY_UNITS_PER_LIGHT_YEAR));
        }

        private Camera ResolveCamera()
        {
            if (celestialCamera != null)
                return celestialCamera;

            return Camera;
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
                renderQueue = shader.name == "SpaceEngine/Streaming/Star Point"
                    ? (int)RenderQueue.Background
                    : (int)RenderQueue.Transparent
            };

            if (shader.name != "SpaceEngine/Streaming/Star Point")
            {
                _runtimePointTexture = CreatePointTexture();

                if (_runtimePointMaterial.HasProperty(BaseMap))
                    _runtimePointMaterial.SetTexture(BaseMap, _runtimePointTexture);

                if (_runtimePointMaterial.HasProperty(MainTex))
                    _runtimePointMaterial.SetTexture(MainTex, _runtimePointTexture);
            }

            if (_runtimePointMaterial.HasProperty(BaseColor))
                _runtimePointMaterial.SetColor(BaseColor, Color.white);

            if (_runtimePointMaterial.HasProperty(Color1))
                _runtimePointMaterial.SetColor(Color1, Color.white);

            if (_runtimePointMaterial.HasProperty(Surface))
                _runtimePointMaterial.SetFloat(Surface, 1f);

            if (_runtimePointMaterial.HasProperty(Blend))
                _runtimePointMaterial.SetFloat(Blend, 1f);

            if (_runtimePointMaterial.HasProperty(ZWrite))
                _runtimePointMaterial.SetFloat(ZWrite, 0f);

            if (_runtimePointMaterial.HasProperty(Cull))
                _runtimePointMaterial.SetFloat(Cull, 0f);

            if (_runtimePointMaterial.HasProperty(Intensity))
                _runtimePointMaterial.SetFloat(Intensity, 0.65f);

            if (_runtimePointMaterial.HasProperty(Softness))
                _runtimePointMaterial.SetFloat(Softness, 3.5f);

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
                (instanceCount + MAXIMUM_INSTANCES_PER_DRAW_CALL - 1) /
                MAXIMUM_INSTANCES_PER_DRAW_CALL;

            if (matrices.Length == requiredBatchCount)
                return;

            matrices = new Matrix4x4[requiredBatchCount][];

            for (var i = 0; i < requiredBatchCount; i++)
                matrices[i] = new Matrix4x4[MAXIMUM_INSTANCES_PER_DRAW_CALL];
        }
    }
}
