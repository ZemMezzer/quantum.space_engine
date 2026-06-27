using System;
using System.Collections.Generic;
using SpaceEngine.Runtime.Content.StellarObjects.Rendering;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Data.SolarSystem.Objects;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Stars
{
    public abstract class StarStellarObjectRenderer : StellarObjectRenderer
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int SurfaceColor = Shader.PropertyToID("_SurfaceColor");
        private static readonly int HotColor = Shader.PropertyToID("_HotColor");
        private static readonly int SpotColor = Shader.PropertyToID("_SpotColor");
        private static readonly int GranulationScale =
            Shader.PropertyToID("_GranulationScale");
        private static readonly int DetailScale = Shader.PropertyToID("_DetailScale");
        private static readonly int SpotScale = Shader.PropertyToID("_SpotScale");
        private static readonly int SpotStrength =
            Shader.PropertyToID("_SpotStrength");
        private static readonly int FlowSpeed = Shader.PropertyToID("_FlowSpeed");
        private static readonly int Intensity = Shader.PropertyToID("_Intensity");
        private static readonly int RimPower = Shader.PropertyToID("_RimPower");
        private static readonly int TurbulenceScale =
            Shader.PropertyToID("_TurbulenceScale");
        private static readonly int ShellDisplacement =
            Shader.PropertyToID("_ShellDisplacement");
        private static readonly int Opacity = Shader.PropertyToID("_Opacity");
        private static readonly int RayStrength =
            Shader.PropertyToID("_RayStrength");
        private static readonly int RayCount = Shader.PropertyToID("_RayCount");
        private static readonly int DiscRadius = Shader.PropertyToID("_DiscRadius");
        private static readonly int GasSoftness = Shader.PropertyToID("_GasSoftness");
        private static readonly int FlickerStrength =
            Shader.PropertyToID("_FlickerStrength");
        private static readonly int Seed = Shader.PropertyToID("_Seed");
        private static readonly int SurfaceTime =
            Shader.PropertyToID("_SurfaceTime");

        [Header("LOD 1")]
        [SerializeField] private Color surfaceColor;
        [SerializeField] private Color coronaColor;
        [SerializeField, Range(0.001f, 10.0f)]
        private float minimumAngularDiameterDegrees = 0.20f;
        [SerializeField, Min(1.0f)]
        private float minimumDiameterInUnityUnits = 8.0f;
        [SerializeField, Min(1.0f)]
        private float coronaRadiusMultiplier = 1.055f;
        [SerializeField, Min(1.0f)]
        private float lod1LightRadiusMultiplier = 10.0f;
        [SerializeField, Range(0.0f, 32.0f)]
        private float lod1LightIntensity = 8.0f;
        [SerializeField, Range(0.0f, 3.0f)]
        private float lod1LightRayStrength = 0.65f;
        [SerializeField, Range(4.0f, 32.0f)]
        private float lod1LightRayCount = 8.0f;

        [Header("Close stellar detail")]
        [SerializeField, Min(1.0f)]
        private float detailActivationDistanceInRadii = 64.0f;
        [SerializeField, Min(1.0f)]
        private float detailDeactivationDistanceInRadii = 80.0f;

        protected abstract Color DefaultSurfaceColor { get; }
        protected abstract Color DefaultCoronaColor { get; }

        protected void ApplyDefaultColors()
        {
            surfaceColor = DefaultSurfaceColor;
            coronaColor = DefaultCoronaColor;
        }

        public override IStellarObjectVisual CreateVisual(
            in StellarObjectRenderContext context)
        {
            if (context.Data is not StarData star)
            {
                throw new InvalidOperationException(
                    $"{name} is paired to {context.Data?.Entity?.DisplayName ?? "an unknown entity"} " +
                    "but requires StarData.");
            }

            var temperatureColor = GetTemperatureColor(
                star.SurfaceTemperatureKelvin);

            return new StarVisual(
                context,
                star,
                Multiply(ResolveColor(surfaceColor, DefaultSurfaceColor),
                    temperatureColor),
                Multiply(ResolveColor(coronaColor, DefaultCoronaColor),
                    temperatureColor),
                minimumAngularDiameterDegrees,
                minimumDiameterInUnityUnits,
                coronaRadiusMultiplier,
                lod1LightRadiusMultiplier,
                lod1LightIntensity,
                lod1LightRayStrength,
                lod1LightRayCount,
                detailActivationDistanceInRadii,
                detailDeactivationDistanceInRadii);
        }

        public override bool TryGetDistantPointStyle(
            StellarObjectData data,
            out Color color,
            out float intensity)
        {
            if (data is not StarData star)
            {
                color = Color.white;
                intensity = 1.5f;
                return false;
            }

            color = Multiply(
                ResolveColor(surfaceColor, DefaultSurfaceColor),
                GetTemperatureColor(star.SurfaceTemperatureKelvin));
            intensity = 2.0f;
            return true;
        }

        private static Color ResolveColor(Color authored, Color fallback)
        {
            return authored.a <= 0.0f ? fallback : authored;
        }

        private static Color GetTemperatureColor(double temperatureKelvin)
        {
            var normalized = Mathf.InverseLerp(
                1_800.0f,
                35_000.0f,
                (float)temperatureKelvin);
            return Color.Lerp(
                new Color(1.0f, 0.28f, 0.12f, 1.0f),
                new Color(0.48f, 0.70f, 1.0f, 1.0f),
                normalized);
        }

        private static Color Multiply(Color first, Color second)
        {
            return new Color(
                first.r * second.r,
                first.g * second.g,
                first.b * second.b,
                first.a * second.a);
        }

        private sealed class StarVisual : IStellarObjectVisual
        {
            private readonly Transform root;
            private readonly Transform corona;
            private readonly Transform lod1Light;
            private readonly Transform prominenceRoot;
            private readonly MeshRenderer lod1LightRenderer;
            private readonly Material surfaceInstance;
            private readonly Material coronaInstance;
            private readonly Material lod1LightInstance;
            private readonly Mesh lod1LightMesh;
            private readonly List<Mesh> prominenceMeshes = new();
            private readonly StarData star;
            private readonly int objectIndex;
            private readonly int layer;
            private readonly float seed;
            private readonly Color surfacePresentationColor;
            private readonly Color coronaPresentationColor;
            private readonly float minimumAngularDiameterDegrees;
            private readonly float minimumDiameterInUnityUnits;
            private readonly float lod1LightRadiusMultiplier;
            private readonly float lod1LightIntensity;
            private readonly float lod1LightRayStrength;
            private readonly float lod1LightRayCount;
            private readonly float detailActivationDistanceInRadii;
            private readonly float detailDeactivationDistanceInRadii;
            private Material prominenceInstance;
            private bool isDetailActive;
            private bool prominencesCreated;
            private double lastPresentationRadiusMeters;
            private double lastDistanceMeters;

            public StarVisual(
                in StellarObjectRenderContext context,
                StarData star,
                Color surfaceColor,
                Color coronaColor,
                float minimumAngularDiameterDegrees,
                float minimumDiameterInUnityUnits,
                float coronaRadiusMultiplier,
                float lod1LightRadiusMultiplier,
                float lod1LightIntensity,
                float lod1LightRayStrength,
                float lod1LightRayCount,
                float detailActivationDistanceInRadii,
                float detailDeactivationDistanceInRadii)
            {
                this.star = star;
                objectIndex = context.ObjectIndex;
                layer = context.Layer;
                seed = GetSeed(star, objectIndex);
                surfacePresentationColor = surfaceColor;
                coronaPresentationColor = coronaColor;
                this.minimumAngularDiameterDegrees =
                    Mathf.Max(0.001f, minimumAngularDiameterDegrees);
                this.minimumDiameterInUnityUnits =
                    Mathf.Max(1.0f, minimumDiameterInUnityUnits);
                this.lod1LightRadiusMultiplier = Mathf.Max(
                    1.0f,
                    lod1LightRadiusMultiplier);
                this.lod1LightIntensity = Mathf.Max(0.0f, lod1LightIntensity);
                this.lod1LightRayStrength = Mathf.Max(
                    0.0f,
                    lod1LightRayStrength);
                this.lod1LightRayCount = Mathf.Clamp(
                    lod1LightRayCount,
                    4.0f,
                    32.0f);
                this.detailActivationDistanceInRadii = Mathf.Max(
                    1.0f,
                    detailActivationDistanceInRadii);
                this.detailDeactivationDistanceInRadii = Mathf.Max(
                    this.detailActivationDistanceInRadii,
                    detailDeactivationDistanceInRadii);

                root = StellarObjectPresentationUtility.CreateRoot(
                    context,
                    context.Data.Entity == null
                        ? $"Star {context.ObjectIndex}"
                        : context.Data.Entity.DisplayName);

                root.gameObject.AddComponent<MeshFilter>().sharedMesh =
                    StellarObjectPresentationUtility.GetSphereMesh();

                var surfaceRenderer = root.gameObject.AddComponent<MeshRenderer>();
                surfaceRenderer.shadowCastingMode = ShadowCastingMode.Off;
                surfaceRenderer.receiveShadows = false;
                surfaceInstance =
                    StellarObjectPresentationUtility.CreateStarSurfaceMaterial(
                        surfaceColor);
                ConfigureSurfaceMaterial(surfaceInstance, star, surfaceColor, seed);
                surfaceRenderer.sharedMaterial = surfaceInstance;

                var coronaObject = new GameObject("Dynamic Corona")
                {
                    layer = context.Layer
                };
                corona = coronaObject.transform;
                corona.SetParent(root, false);
                corona.localScale = Vector3.one * Mathf.Max(
                    1.035f,
                    coronaRadiusMultiplier);
                coronaObject.AddComponent<MeshFilter>().sharedMesh =
                    StellarObjectPresentationUtility.GetSphereMesh();

                var coronaRenderer = coronaObject.AddComponent<MeshRenderer>();
                coronaRenderer.shadowCastingMode = ShadowCastingMode.Off;
                coronaRenderer.receiveShadows = false;
                coronaInstance =
                    StellarObjectPresentationUtility.CreateStarCoronaMaterial(
                        coronaColor);
                ConfigureCoronaMaterial(coronaInstance, star, coronaColor, seed);
                coronaRenderer.sharedMaterial = coronaInstance;

                var lightObject = new GameObject("LOD 1 Light")
                {
                    layer = context.Layer
                };
                lod1Light = lightObject.transform;
                lod1Light.SetParent(root, false);
                lod1LightMesh = CreateLod1LightQuadMesh();
                lightObject.AddComponent<MeshFilter>().sharedMesh =
                    lod1LightMesh;

                lod1LightRenderer = lightObject.AddComponent<MeshRenderer>();
                lod1LightRenderer.shadowCastingMode = ShadowCastingMode.Off;
                lod1LightRenderer.receiveShadows = false;
                lod1LightInstance = CreateLod1LightMaterial(surfaceColor, seed);
                lod1LightRenderer.sharedMaterial = lod1LightInstance;

                var gasRootObject = new GameObject("Prominence Gas")
                {
                    layer = context.Layer
                };
                prominenceRoot = gasRootObject.transform;
                prominenceRoot.SetParent(root, false);
                prominenceRoot.gameObject.SetActive(false);
            }

            public void SetVisible(bool isVisible)
            {
                if (root != null)
                    root.gameObject.SetActive(isVisible);
            }

            public void Update(in StellarObjectVisualUpdateContext context)
            {
                lastDistanceMeters = context.DistanceToCameraMeters;
                var minimumByAngularSize =
                    StellarObjectPresentationUtility
                        .GetMinimumAngularRadiusMeters(
                            context.DistanceToCameraMeters,
                            minimumAngularDiameterDegrees);
                var minimumByUnits = context.MetersPerUnityUnit *
                                     minimumDiameterInUnityUnits * 0.5;
                lastPresentationRadiusMeters = Math.Max(
                    context.Data.RadiusMeters,
                    Math.Max(
                        minimumByAngularSize,
                        minimumByUnits));

                StellarObjectPresentationUtility.ApplyTransform(
                    root,
                    context.RelativePositionMeters,
                    lastPresentationRadiusMeters,
                    context.MetersPerUnityUnit);

                var distanceInRadii = context.Data.RadiusMeters > 0.0
                    ? context.DistanceToCameraMeters / context.Data.RadiusMeters
                    : double.PositiveInfinity;
                var detailLimit = isDetailActive
                    ? detailDeactivationDistanceInRadii
                    : detailActivationDistanceInRadii;
                isDetailActive = distanceInRadii <= detailLimit;

                UpdateLod1Light(context);
                UpdateProminences(context);
                UpdateMaterials(context);
            }

            public bool IsDistantPointReplacementReady(
                float requiredAngularDiameterDegrees)
            {
                return StellarObjectPresentationUtility
                           .GetAngularDiameterDegrees(
                               lastPresentationRadiusMeters,
                               lastDistanceMeters) >=
                       requiredAngularDiameterDegrees;
            }

            public void Dispose()
            {
                StellarObjectPresentationUtility.DestroyObject(surfaceInstance);
                StellarObjectPresentationUtility.DestroyObject(coronaInstance);
                StellarObjectPresentationUtility.DestroyObject(lod1LightInstance);
                StellarObjectPresentationUtility.DestroyObject(lod1LightMesh);
                StellarObjectPresentationUtility.DestroyObject(prominenceInstance);

                for (var index = 0; index < prominenceMeshes.Count; index++)
                {
                    StellarObjectPresentationUtility.DestroyObject(
                        prominenceMeshes[index]);
                }

                StellarObjectPresentationUtility.DestroyObject(root?.gameObject);
            }

            private void UpdateLod1Light(
                in StellarObjectVisualUpdateContext context)
            {
                if (lod1Light == null || lod1LightRenderer == null)
                    return;

                var showLod1Light = !isDetailActive;
                if (lod1Light.gameObject.activeSelf != showLod1Light)
                    lod1Light.gameObject.SetActive(showLod1Light);

                if (!showLod1Light)
                    return;

                lod1Light.localPosition = Vector3.zero;
                lod1Light.localScale = Vector3.one * lod1LightRadiusMultiplier;

                var camera = context.Camera != null
                    ? context.Camera
                    : Camera.main;
                if (camera != null)
                    lod1Light.rotation = camera.transform.rotation;

                SetColor(
                    lod1LightInstance,
                    BaseColor,
                    surfacePresentationColor);
                SetFloat(lod1LightInstance, Intensity, lod1LightIntensity);
                SetFloat(lod1LightInstance, Opacity, 1.0f);
                SetFloat(lod1LightInstance, RayStrength, lod1LightRayStrength);
                SetFloat(lod1LightInstance, RayCount, lod1LightRayCount);
                SetFloat(
                    lod1LightInstance,
                    DiscRadius,
                    1.0f / lod1LightRadiusMultiplier);
            }

            private void UpdateProminences(
                in StellarObjectVisualUpdateContext context)
            {
                if (!SupportsProminences(star))
                {
                    if (prominenceRoot.gameObject.activeSelf)
                        prominenceRoot.gameObject.SetActive(false);

                    return;
                }

                if (isDetailActive && !prominencesCreated)
                    CreateProminences();

                var showProminences = isDetailActive && prominencesCreated;
                if (prominenceRoot.gameObject.activeSelf != showProminences)
                    prominenceRoot.gameObject.SetActive(showProminences);

                if (!showProminences)
                    return;

                SetFloat(
                    prominenceInstance,
                    SurfaceTime,
                    (float)context.SimulationTimeSeconds);
            }

            private void UpdateMaterials(
                in StellarObjectVisualUpdateContext context)
            {
                var time = (float)context.SimulationTimeSeconds;
                SetFloat(surfaceInstance, SurfaceTime, time);
                SetFloat(coronaInstance, SurfaceTime, time);
            }

            private void CreateProminences()
            {
                prominencesCreated = true;
                prominenceInstance = CreateProminenceMaterial(
                    coronaPresentationColor,
                    star,
                    seed);

                var count = 5 + Mathf.FloorToInt(GetSeedValue(41) * 5.0f);
                for (var index = 0; index < count; index++)
                {
                    var prominenceObject = new GameObject(
                        $"Prominence Gas {index}")
                    {
                        layer = layer
                    };
                    prominenceObject.transform.SetParent(prominenceRoot, false);

                    var mesh = CreateProminenceMesh(index);
                    prominenceMeshes.Add(mesh);
                    prominenceObject.AddComponent<MeshFilter>().sharedMesh = mesh;

                    var renderer = prominenceObject.AddComponent<MeshRenderer>();
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.sharedMaterial = prominenceInstance;
                }
            }

            private Mesh CreateProminenceMesh(int prominenceIndex)
            {
                const int pathSegments = 48;
                const int sheetCount = 4;
                const int verticesPerSheet = (pathSegments + 1) * 2;

                var direction = GetUnitDirection(prominenceIndex);
                var reference = Mathf.Abs(direction.y) > 0.9f
                    ? Vector3.right
                    : Vector3.up;

                var planeTangent = Vector3.Cross(reference, direction).normalized;
                var planeNormal = Vector3.Cross(direction, planeTangent).normalized;
                var phase = GetSeedValue(prominenceIndex * 11 + 2);

                var loopHalfWidth = Mathf.Lerp(
                    0.030f,
                    0.110f,
                    GetSeedValue(prominenceIndex * 11 + 3));
                var loopHeight = Mathf.Lerp(
                    0.070f,
                    0.260f,
                    GetSeedValue(prominenceIndex * 11 + 4));
                var baseWidth = Mathf.Lerp(
                    0.0060f,
                    0.0170f,
                    GetSeedValue(prominenceIndex * 11 + 5));
                var lateralWave = Mathf.Lerp(
                    0.006f,
                    0.026f,
                    GetSeedValue(prominenceIndex * 11 + 6));

                var path = new Vector3[pathSegments + 1];
                var pathTangents = new Vector3[pathSegments + 1];

                for (var pathIndex = 0;
                     pathIndex <= pathSegments;
                     pathIndex++)
                {
                    var t = pathIndex / (float)pathSegments;
                    var arch = Mathf.Sin(Mathf.PI * t);
                    var lateral = Mathf.Cos(Mathf.PI * t) * loopHalfWidth +
                                  Mathf.Sin(Mathf.PI * 2.0f * t +
                                            phase * Mathf.PI * 2.0f) *
                                  lateralWave * arch;
                    var verticalDistortion = Mathf.Sin(
                        Mathf.PI * 3.0f * t + phase * 5.7f) *
                        lateralWave * 0.55f * arch;

                    path[pathIndex] =
                        direction * (0.5f + arch * loopHeight) +
                        planeTangent * lateral +
                        planeNormal * verticalDistortion;
                }

                for (var pathIndex = 0;
                     pathIndex <= pathSegments;
                     pathIndex++)
                {
                    var previous = path[Mathf.Max(0, pathIndex - 1)];
                    var next = path[Mathf.Min(pathSegments, pathIndex + 1)];
                    var tangent = (next - previous).normalized;
                    pathTangents[pathIndex] = tangent.sqrMagnitude <= 0.000001f
                        ? planeTangent
                        : tangent;
                }

                var vertexCount = sheetCount * verticesPerSheet;
                var vertices = new Vector3[vertexCount];
                var normals = new Vector3[vertexCount];
                var uvs = new Vector2[vertexCount];
                var uv2s = new Vector2[vertexCount];
                var colors = new Color[vertexCount];

                for (var sheetIndex = 0;
                     sheetIndex < sheetCount;
                     sheetIndex++)
                {
                    var sheetPhase = GetSeedValue(
                        prominenceIndex * 13 + sheetIndex + 19);

                    for (var pathIndex = 0;
                         pathIndex <= pathSegments;
                         pathIndex++)
                    {
                        var t = pathIndex / (float)pathSegments;
                        var arch = Mathf.Sin(Mathf.PI * t);
                        var pathTangent = pathTangents[pathIndex];
                        var radial = path[pathIndex].normalized;
                        var baseNormal = Vector3.Cross(
                            pathTangent,
                            radial).normalized;
                        if (baseNormal.sqrMagnitude <= 0.000001f)
                            baseNormal = planeNormal;

                        var sheetAngle =
                            (sheetIndex / (float)sheetCount + sheetPhase) *
                            360.0f;
                        var sheetNormal = Quaternion.AngleAxis(
                            sheetAngle,
                            pathTangent) * baseNormal;
                        sheetNormal.Normalize();

                        var sheetSide = Vector3.Cross(
                            sheetNormal,
                            pathTangent).normalized;
                        var width = baseWidth *
                                    (0.34f + arch * 1.08f) *
                                    (0.82f + 0.28f * Mathf.Sin(
                                        t * Mathf.PI * 4.0f +
                                        sheetPhase * Mathf.PI * 2.0f));

                        for (var sideIndex = 0;
                             sideIndex < 2;
                             sideIndex++)
                        {
                            var side = sideIndex == 0 ? -1.0f : 1.0f;
                            var vertexIndex =
                                sheetIndex * verticesPerSheet +
                                pathIndex * 2 +
                                sideIndex;

                            vertices[vertexIndex] = path[pathIndex] +
                                                    sheetSide * side * width;
                            normals[vertexIndex] = sheetNormal;
                            uvs[vertexIndex] = new Vector2(t, sideIndex);
                            uv2s[vertexIndex] = new Vector2(
                                sheetPhase,
                                sheetIndex / (float)(sheetCount - 1));
                            colors[vertexIndex] = new Color(
                                sheetPhase,
                                arch,
                                width / Mathf.Max(baseWidth, 0.0001f),
                                1.0f);
                        }
                    }
                }

                var triangles = new int[sheetCount * pathSegments * 6];
                var triangleIndex = 0;

                for (var sheetIndex = 0;
                     sheetIndex < sheetCount;
                     sheetIndex++)
                {
                    var sheetStart = sheetIndex * verticesPerSheet;

                    for (var pathIndex = 0;
                         pathIndex < pathSegments;
                         pathIndex++)
                    {
                        var a = sheetStart + pathIndex * 2;
                        var b = a + 1;
                        var c = a + 2;
                        var d = a + 3;

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
                    name = "Star Prominence Gas Sheets"
                };
                mesh.vertices = vertices;
                mesh.normals = normals;
                mesh.uv = uvs;
                mesh.uv2 = uv2s;
                mesh.colors = colors;
                mesh.triangles = triangles;
                mesh.RecalculateBounds();
                return mesh;
            }

            private float GetSeedValue(int salt)
            {
                unchecked
                {
                    var value = GetSeedBits(star, objectIndex, salt);
                    return (float)((value >> 40) / (double)(1UL << 24));
                }
            }

            private Vector3 GetUnitDirection(int prominenceIndex)
            {
                var theta = Mathf.PI * 2.0f *
                            GetSeedValue(prominenceIndex * 17 + 71);
                var y = Mathf.Lerp(
                    -0.92f,
                    0.92f,
                    GetSeedValue(prominenceIndex * 17 + 73));
                var horizontal = Mathf.Sqrt(
                    Mathf.Max(0.0f, 1.0f - y * y));

                return new Vector3(
                    Mathf.Cos(theta) * horizontal,
                    y,
                    Mathf.Sin(theta) * horizontal);
            }

            private static bool SupportsProminences(StarData data)
            {
                return data.RadiusMeters >= 50_000_000.0 &&
                       data.SurfaceTemperatureKelvin < 100_000.0;
            }
        }

        private static Material CreateLod1LightMaterial(
            Color color,
            float seed)
        {
            var shader = Shader.Find("SpaceEngine/Streaming/Star LOD 1 Light");
            if (shader == null)
                return StellarObjectPresentationUtility.CreateStarCoronaMaterial(
                    color);

            var material = new Material(shader)
            {
                name = "Star LOD 1 Light Material",
                renderQueue = 3000
            };
            SetColor(material, BaseColor, color);
            SetFloat(material, Intensity, 8.0f);
            SetFloat(material, Opacity, 1.0f);
            SetFloat(material, RayStrength, 0.65f);
            SetFloat(material, RayCount, 8.0f);
            SetFloat(material, DiscRadius, 0.10f);
            SetFloat(material, Seed, seed);
            return material;
        }

        private static Material CreateProminenceMaterial(
            Color color,
            StarData star,
            float seed)
        {
            var shader = Shader.Find("SpaceEngine/Streaming/Plasma Additive");
            if (shader == null)
                return StellarObjectPresentationUtility.CreateStarCoronaMaterial(
                    color);

            var material = new Material(shader)
            {
                name = "Star Prominence Material",
                renderQueue = 3100
            };
            SetColor(material, BaseColor, color);
            SetFloat(material, Seed, seed);
            SetFloat(material, Intensity, GetCoronaIntensity(star) * 1.1f);
            SetFloat(material, FlowSpeed, GetFlowSpeed(star) * 1.8f);
            SetFloat(
                material,
                TurbulenceScale,
                Mathf.Max(8.0f, GetGranulationScale(star) * 0.32f));
            SetFloat(material, GasSoftness, 1.65f);
            SetFloat(material, FlickerStrength, 0.30f);
            return material;
        }

        private static void ConfigureSurfaceMaterial(
            Material material,
            StarData star,
            Color color,
            float seed)
        {
            SetColor(material, BaseColor, color);
            SetColor(material, SurfaceColor, BlendTowardsWhite(color, 0.35f));
            SetColor(material, HotColor, BlendTowardsWhite(color, 0.78f));
            SetColor(material, SpotColor, color * 0.12f);
            SetFloat(material, Seed, seed);
            SetFloat(material, GranulationScale, GetGranulationScale(star));
            SetFloat(
                material,
                DetailScale,
                GetGranulationScale(star) * 4.6f);
            SetFloat(material, SpotScale, GetSpotScale(star));
            SetFloat(material, SpotStrength, GetSpotStrength(star));
            SetFloat(material, FlowSpeed, GetFlowSpeed(star));
            SetFloat(material, Intensity, GetCloseSurfaceIntensity(star));
        }

        private static void ConfigureCoronaMaterial(
            Material material,
            StarData star,
            Color color,
            float seed)
        {
            SetColor(material, BaseColor, color);
            SetFloat(material, Seed, seed);
            SetFloat(material, Intensity, GetCoronaIntensity(star));
            SetFloat(material, RimPower, 2.15f);
            SetFloat(material, FlowSpeed, GetFlowSpeed(star) * 0.5f);
            SetFloat(
                material,
                TurbulenceScale,
                Mathf.Max(10.0f, GetGranulationScale(star) * 0.72f));
            SetFloat(material, ShellDisplacement, 0.035f);
        }

        private static Mesh CreateLod1LightQuadMesh()
        {
            var mesh = new Mesh
            {
                name = "Star LOD 1 Light Quad",
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0.0f),
                    new Vector3(0.5f, -0.5f, 0.0f),
                    new Vector3(0.5f, 0.5f, 0.0f),
                    new Vector3(-0.5f, 0.5f, 0.0f)
                },
                uv = new[]
                {
                    new Vector2(0.0f, 0.0f),
                    new Vector2(1.0f, 0.0f),
                    new Vector2(1.0f, 1.0f),
                    new Vector2(0.0f, 1.0f)
                },
                triangles = new[]
                {
                    0, 1, 2,
                    0, 2, 3
                }
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Color BlendTowardsWhite(Color color, float amount)
        {
            return new Color(
                Mathf.Lerp(color.r, 1.0f, amount),
                Mathf.Lerp(color.g, 1.0f, amount),
                Mathf.Lerp(color.b, 1.0f, amount),
                color.a);
        }

        private static float GetCloseSurfaceIntensity(StarData star)
        {
            if (star.RadiusMeters >= 10_000_000_000.0)
                return 1.35f;

            if (star.RadiusMeters < 50_000_000.0 ||
                star.SurfaceTemperatureKelvin >= 100_000.0)
            {
                return 1.60f;
            }

            return 1.20f;
        }

        private static float GetSpotStrength(StarData star)
        {
            if (star.RadiusMeters >= 10_000_000_000.0)
                return 0.52f;

            if (star.SurfaceTemperatureKelvin < 4_000.0)
                return 0.74f;

            if (star.SurfaceTemperatureKelvin < 5_500.0)
                return 0.42f;

            if (star.SurfaceTemperatureKelvin < 7_500.0)
                return 0.34f;

            if (star.RadiusMeters < 50_000_000.0 ||
                star.SurfaceTemperatureKelvin >= 100_000.0)
            {
                return 0.12f;
            }

            return 0.30f;
        }

        private static float GetGranulationScale(StarData star)
        {
            if (star.RadiusMeters >= 10_000_000_000.0)
                return 16.0f;

            return star.RadiusMeters < 50_000_000.0 ||
                   star.SurfaceTemperatureKelvin >= 100_000.0
                ? 92.0f
                : 52.0f;
        }

        private static float GetSpotScale(StarData star)
        {
            return star.RadiusMeters >= 10_000_000_000.0
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
            if (star.RadiusMeters >= 10_000_000_000.0)
                return 4.0f;

            return star.RadiusMeters < 50_000_000.0 ||
                   star.SurfaceTemperatureKelvin >= 100_000.0
                ? 3.4f
                : 2.2f;
        }

        private static float GetSeed(StarData star, int objectIndex)
        {
            unchecked
            {
                var value = GetSeedBits(star, objectIndex, 0);
                return (float)((value >> 40) / (double)(1UL << 24));
            }
        }

        private static ulong GetSeedBits(
            StarData star,
            int objectIndex,
            int salt)
        {
            unchecked
            {
                var value = (ulong)BitConverter.DoubleToInt64Bits(star.MassKg);
                value ^= (ulong)BitConverter.DoubleToInt64Bits(
                    star.RadiusMeters) * 0x9E3779B97F4A7C15UL;
                value ^= (ulong)BitConverter.DoubleToInt64Bits(
                    star.RotationPeriodSeconds) * 0xD1B54A32D192ED03UL;
                value ^= (ulong)(objectIndex + 1) * 0x94D049BB133111EBUL;
                value ^= (ulong)(salt + 1) * 0xBF58476D1CE4E5B9UL;
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return value;
            }
        }

        private static void SetColor(
            Material material,
            int propertyId,
            Color value)
        {
            if (material != null && material.HasProperty(propertyId))
                material.SetColor(propertyId, value);
        }

        private static void SetFloat(
            Material material,
            int propertyId,
            float value)
        {
            if (material != null && material.HasProperty(propertyId))
                material.SetFloat(propertyId, value);
        }
    }
}
