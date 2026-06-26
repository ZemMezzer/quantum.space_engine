using System;
using SpaceEngine.Runtime.Content.StellarObjects.Rendering;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Data.SolarSystem.Objects;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Stars
{
    [CreateAssetMenu(
        fileName = "Black Hole Renderer",
        menuName = "Space Engine/Stellar Objects/Stars/Rendering/Black Hole")]
    public sealed class BlackHoleStellarObjectRenderer : StellarObjectRenderer
    {
        private static readonly int SurfaceTime = Shader.PropertyToID("_SurfaceTime");

        [Header("Horizon")]
        [SerializeField] private Color horizonColor = Color.black;

        [Header("Accretion disk")]
        [SerializeField] private Color accretionDiskColor =
            new Color(1.0f, 0.55f, 0.15f, 1.0f);
        [SerializeField, Min(1.1f)]
        private float diskOuterRadiusInHorizonRadii = 20.0f;
        [SerializeField, Range(0.001f, 10.0f)]
        private float minimumDiskAngularDiameterDegrees = 0.20f;
public override IStellarObjectVisual CreateVisual(
            in StellarObjectRenderContext context)
        {
            var blackHole = (BlackHoleData)context.Data;
            return new BlackHoleVisual(
                context,
                blackHole.HasAccretionDisk,
                horizonColor,
                accretionDiskColor,
                diskOuterRadiusInHorizonRadii,
                minimumDiskAngularDiameterDegrees);
        }

        public override bool TryGetDistantPointStyle(
            StellarObjectData data,
            out Color color,
            out float intensity)
        {
            if (data is not BlackHoleData)
            {
                color = Color.white;
                intensity = 1.5f;
                return false;
            }

            var blackHole = (BlackHoleData)data;
            color = accretionDiskColor;
            intensity = blackHole.HasAccretionDisk ? 2.5f : 0.4f;
            return true;
        }

        private sealed class BlackHoleVisual : IStellarObjectVisual
        {
            private readonly Transform root;
            private readonly Transform disk;
            private readonly Material horizonInstance;
            private readonly Material diskInstance;
            private readonly float diskOuterRadiusInHorizonRadii;
            private readonly float minimumDiskAngularDiameterDegrees;
            private double lastDiskRadiusMeters;
            private double lastDistanceMeters;

            public BlackHoleVisual(
                in StellarObjectRenderContext context,
                bool hasAccretionDisk,
                Color horizonColor,
                Color diskColor,
                float diskOuterRadiusInHorizonRadii,
                float minimumDiskAngularDiameterDegrees)
            {
                root = StellarObjectPresentationUtility.CreateRoot(context, $"Black Hole {context.ObjectIndex}");
                this.diskOuterRadiusInHorizonRadii =
                    Mathf.Max(1.1f, diskOuterRadiusInHorizonRadii);
                this.minimumDiskAngularDiameterDegrees =
                    Mathf.Max(0.001f, minimumDiskAngularDiameterDegrees);

                root.gameObject.AddComponent<MeshFilter>().sharedMesh =
                    StellarObjectPresentationUtility.GetSphereMesh();

                var horizonRenderer = root.gameObject.AddComponent<MeshRenderer>();
                horizonRenderer.shadowCastingMode = ShadowCastingMode.Off;
                horizonRenderer.receiveShadows = false;
                horizonInstance =
                    StellarObjectPresentationUtility.CreateBlackHoleHorizonMaterial(
                        horizonColor);
                horizonRenderer.sharedMaterial = horizonInstance;

                if (!hasAccretionDisk)
                {
                    return;
                }

                var diskObject = new GameObject("Accretion Disk")
                {
                    layer = context.Layer
                };
                disk = diskObject.transform;
                disk.SetParent(root, false);
                disk.localRotation = Quaternion.Euler(72.0f, 0.0f, 0.0f);

                diskObject.AddComponent<MeshFilter>().sharedMesh =
                    CreateDiskMesh();

                var diskRenderer = diskObject.AddComponent<MeshRenderer>();
                diskRenderer.shadowCastingMode = ShadowCastingMode.Off;
                diskRenderer.receiveShadows = false;
                diskInstance =
                    StellarObjectPresentationUtility.CreateBlackHoleAccretionDiskMaterial(
                        diskColor);
                diskRenderer.sharedMaterial = diskInstance;
            }

            public void SetVisible(bool isVisible)
            {
                if (root != null)
                    root.gameObject.SetActive(isVisible);
            }

            public void Update(in StellarObjectVisualUpdateContext context)
            {
                lastDistanceMeters = context.DistanceToCameraMeters;

                // The root itself remains a physical event horizon.
                StellarObjectPresentationUtility.ApplyTransform(
                    root,
                    context.RelativePositionMeters,
                    context.Data.RadiusMeters,
                    context.MetersPerUnityUnit);

                if (disk == null)
                    return;

                var physicalDiskRadius = context.Data.RadiusMeters *
                                         diskOuterRadiusInHorizonRadii;
                var angularRadius =
                    StellarObjectPresentationUtility
                        .GetMinimumAngularRadiusMeters(
                            context.DistanceToCameraMeters,
                            minimumDiskAngularDiameterDegrees);
                lastDiskRadiusMeters = Math.Max(
                    physicalDiskRadius,
                    angularRadius);

                var denominator = Math.Max(
                    context.Data.RadiusMeters,
                    0.0000001);
                var localScale = (float)(lastDiskRadiusMeters / denominator);
                disk.localScale = Vector3.one * localScale;

                StellarObjectPresentationUtility.SetFloat(
                    diskInstance,
                    SurfaceTime,
                    (float)context.SimulationTimeSeconds);
            }

            public bool IsDistantPointReplacementReady(
                float requiredAngularDiameterDegrees)
            {
                return disk != null &&
                       StellarObjectPresentationUtility
                           .GetAngularDiameterDegrees(
                               lastDiskRadiusMeters,
                               lastDistanceMeters) >=
                       requiredAngularDiameterDegrees;
            }

            public void Dispose()
            {
                StellarObjectPresentationUtility.DestroyObject(horizonInstance);
                StellarObjectPresentationUtility.DestroyObject(diskInstance);
                StellarObjectPresentationUtility.DestroyObject(root?.gameObject);
            }

            private static Mesh CreateDiskMesh()
            {
                const int segments = 64;
                const float inner = 0.16f;
                const float outer = 1.0f;

                var vertices = new Vector3[(segments + 1) * 2];
                var uv = new Vector2[vertices.Length];
                var triangles = new int[segments * 6];

                for (var i = 0; i <= segments; i++)
                {
                    var t = i / (float)segments;
                    var angle = t * Mathf.PI * 2.0f;
                    var cosine = Mathf.Cos(angle);
                    var sine = Mathf.Sin(angle);

                    var innerIndex = i * 2;
                    var outerIndex = innerIndex + 1;
                    vertices[innerIndex] =
                        new Vector3(cosine * inner, 0.0f, sine * inner);
                    vertices[outerIndex] =
                        new Vector3(cosine * outer, 0.0f, sine * outer);
                    uv[innerIndex] = new Vector2(t, 0.0f);
                    uv[outerIndex] = new Vector2(t, 1.0f);

                    if (i == segments)
                        continue;

                    var triangle = i * 6;
                    triangles[triangle] = innerIndex;
                    triangles[triangle + 1] = outerIndex;
                    triangles[triangle + 2] = innerIndex + 2;
                    triangles[triangle + 3] = outerIndex;
                    triangles[triangle + 4] = innerIndex + 3;
                    triangles[triangle + 5] = innerIndex + 2;
                }

                var mesh = new Mesh
                {
                    name = "Black Hole Accretion Disk",
                    vertices = vertices,
                    uv = uv,
                    triangles = triangles
                };
                mesh.RecalculateNormals();
                return mesh;
            }
        }
    }
}
