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
        private static readonly int SurfaceTime =
            Shader.PropertyToID("_SurfaceTime");
        private static readonly int Seed = Shader.PropertyToID("_Seed");
        private static readonly int LensingStrength =
            Shader.PropertyToID("_LensingStrength");
        private static readonly int LensRadius =
            Shader.PropertyToID("_LensRadius");
        private static readonly int HorizonRadius =
            Shader.PropertyToID("_HorizonRadius");
        private static readonly int LensCenterViewport =
            Shader.PropertyToID("_LensCenterViewport");
        private static readonly int LensEdgeSoftness =
            Shader.PropertyToID("_LensEdgeSoftness");
        private static readonly int LensRingColor =
            Shader.PropertyToID("_LensRingColor");
        private static readonly int LensRingIntensity =
            Shader.PropertyToID("_LensRingIntensity");
        private static readonly int SwirlStrength =
            Shader.PropertyToID("_SwirlStrength");
        private static readonly int SwirlFalloff =
            Shader.PropertyToID("_SwirlFalloff");
        private static readonly int SwirlDirection =
            Shader.PropertyToID("_SwirlDirection");
        private static readonly int ApparentShadowScale =
            Shader.PropertyToID("_ApparentShadowScale");

        [Header("Horizon")]
        [SerializeField] private Color horizonColor = Color.black;

        [Tooltip(
            "Smallest visible angular diameter of the horizon while LOD 1 is " +
            "active. This keeps the black hole readable before its real " +
            "physical radius is large enough on screen.")]
        [SerializeField, Range(0.01f, 5.0f)]
        private float minimumHorizonAngularDiameterDegrees = 0.35f;

        [Header("Gravitational lensing")]
        [Tooltip(
            "Outer radius of the lensing field relative to the visible " +
            "event-horizon silhouette.")]
        [SerializeField, Range(1.1f, 8.0f)]
        private float lensingRadiusInHorizonRadii = 2.5f;

        [SerializeField, Range(0.0f, 2.0f)]
        private float lensingStrength = 0.95f;

        [SerializeField, Range(0.01f, 0.50f)]
        private float lensEdgeSoftness = 0.20f;

        [Tooltip(
            "A subtle optical highlight at the photon sphere. It remains " +
            "visible even against a nearly uniform background.")]
        [SerializeField, Range(0.0f, 1.0f)]
        private float lensRingIntensity = 0.16f;

        [SerializeField] private Color lensRingColor =
            new Color(0.78f, 0.88f, 1.0f, 1.0f);

        [Tooltip(
            "Tangential frame-dragging strength. Higher values create " +
            "a stronger visible swirl around the event horizon.")]
        [SerializeField, Range(0.0f, 12.0f)]
        private float swirlStrength = 5.5f;

        [Tooltip(
            "How quickly the swirl fades outward from the horizon.")]
        [SerializeField, Range(0.25f, 6.0f)]
        private float swirlFalloff = 1.75f;

        [Tooltip(
            "Visible rotation direction of the frame-dragging effect.")]
        [SerializeField, Range(-1.0f, 1.0f)]
        private float swirlDirection = 1.0f;

        [Header("Accretion disk")]
        [SerializeField] private Color accretionDiskColor =
            new Color(1.0f, 0.55f, 0.15f, 1.0f);

        [SerializeField, Min(1.1f)]
        private float diskOuterRadiusInHorizonRadii = 20.0f;

        [SerializeField, Range(0.001f, 10.0f)]
        private float minimumDiskAngularDiameterDegrees = 0.20f;

        [Tooltip(
            "LOD 1 safety floor for the visible accretion disk. It is used " +
            "even when an existing renderer asset still has an older, very " +
            "small Minimum Disk Angular Diameter value.")]
        [SerializeField, Range(0.01f, 10.0f)]
        private float minimumLod1DiskAngularDiameterDegrees = 0.75f;

        public override IStellarObjectVisual CreateVisual(
            in StellarObjectRenderContext context)
        {
            var blackHole = (BlackHoleData)context.Data;
            return new BlackHoleVisual(
                context,
                blackHole,
                horizonColor,
                accretionDiskColor,
                lensRingColor,
                minimumHorizonAngularDiameterDegrees,
                minimumLod1DiskAngularDiameterDegrees,
                lensingRadiusInHorizonRadii,
                lensingStrength,
                lensEdgeSoftness,
                lensRingIntensity,
                swirlStrength,
                swirlFalloff,
                swirlDirection,
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
            private const float MinimumWorldRadius = 0.000001f;
            private const float MinimumViewportRadius = 0.000001f;
            private const float CameraInsideHorizonPadding = 1.001f;
            private const float LensingRadiusVisualMultiplier = 1.45f;
            private const float DiskMeshInnerRadius = 1.2f;
            private const float DiskMeshOuterRadius = 12.0f;
            private const int LensingDiscSegments = 96;

            private readonly Transform root;
            private readonly Transform lensingDisc;
            private readonly Transform disk;
            private readonly Mesh lensingDiscMesh;
            private readonly Material horizonInstance;
            private readonly Material lensingInstance;
            private readonly Material diskInstance;
            private readonly float lensingRadiusInHorizonRadii;
            private readonly float diskOuterRadiusInHorizonRadii;
            private readonly float minimumHorizonAngularDiameterDegrees;
            private readonly float minimumDiskAngularDiameterDegrees;

            private bool isVisible;
            private double lastPresentationHorizonRadiusMeters;
            private double lastDiskRadiusMeters;
            private double lastDistanceMeters;

            public BlackHoleVisual(
                in StellarObjectRenderContext context,
                BlackHoleData blackHole,
                Color horizonColor,
                Color diskColor,
                Color lensRingColor,
                float minimumHorizonAngularDiameterDegrees,
                float minimumLod1DiskAngularDiameterDegrees,
                float lensingRadiusInHorizonRadii,
                float lensingStrength,
                float lensEdgeSoftness,
                float lensRingIntensity,
                float swirlStrength,
                float swirlFalloff,
                float swirlDirection,
                float diskOuterRadiusInHorizonRadii,
                float minimumDiskAngularDiameterDegrees)
            {
                root = StellarObjectPresentationUtility.CreateRoot(
                    context,
                    $"Black Hole {context.ObjectIndex}");
                this.lensingRadiusInHorizonRadii = Mathf.Clamp(
                    lensingRadiusInHorizonRadii,
                    1.1f,
                    8.0f);
                this.diskOuterRadiusInHorizonRadii = Mathf.Max(
                    1.1f,
                    diskOuterRadiusInHorizonRadii);
                this.minimumHorizonAngularDiameterDegrees = Mathf.Max(
                    0.01f,
                    minimumHorizonAngularDiameterDegrees);
                this.minimumDiskAngularDiameterDegrees = Mathf.Max(
                    0.01f,
                    Mathf.Max(
                        minimumDiskAngularDiameterDegrees,
                        minimumLod1DiskAngularDiameterDegrees));

                root.gameObject.AddComponent<MeshFilter>().sharedMesh =
                    StellarObjectPresentationUtility.GetSphereMesh();

                var horizonRenderer = root.gameObject.AddComponent<MeshRenderer>();
                horizonRenderer.shadowCastingMode = ShadowCastingMode.Off;
                horizonRenderer.receiveShadows = false;
                horizonInstance =
                    StellarObjectPresentationUtility.CreateBlackHoleHorizonMaterial(
                        horizonColor);
                ConfigureHorizonMaterial(
                    horizonInstance,
                    blackHole,
                    context.ObjectIndex);
                horizonRenderer.sharedMaterial = horizonInstance;

                // The lens is a camera-facing disc placed in camera space every
                // frame. It is deliberately not parented to the scaled horizon:
                // the generated mesh can never pull it inside the horizon.
                var lensingObject = new GameObject(
                    "Black Hole Gravitational Lensing")
                {
                    layer = context.Layer
                };
                lensingDisc = lensingObject.transform;
                lensingDiscMesh = CreateLensingDiscMesh();
                lensingObject.AddComponent<MeshFilter>().sharedMesh =
                    lensingDiscMesh;

                var lensingRenderer = lensingObject.AddComponent<MeshRenderer>();
                lensingRenderer.shadowCastingMode = ShadowCastingMode.Off;
                lensingRenderer.receiveShadows = false;
                lensingInstance = CreateLensingMaterial(
                    blackHole,
                    context.ObjectIndex,
                    Mathf.Clamp(lensingStrength, 0.0f, 2.0f),
                    Mathf.Clamp(lensEdgeSoftness, 0.01f, 0.50f),
                    lensRingColor,
                    Mathf.Clamp01(lensRingIntensity),
                    Mathf.Clamp(swirlStrength, 0.0f, 12.0f),
                    Mathf.Clamp(swirlFalloff, 0.25f, 6.0f),
                    Mathf.Clamp(swirlDirection, -1.0f, 1.0f));
                lensingRenderer.sharedMaterial = lensingInstance;
                lensingObject.SetActive(false);

                if (!blackHole.HasAccretionDisk)
                    return;

                var diskObject = new GameObject("Accretion Disk")
                {
                    layer = context.Layer
                };
                disk = diskObject.transform;
                disk.SetParent(root, false);
                disk.localRotation = GetDiskRotation(
                    blackHole,
                    context.ObjectIndex);

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
                this.isVisible = isVisible;

                if (root != null)
                    root.gameObject.SetActive(isVisible);

                if (lensingDisc != null)
                    lensingDisc.gameObject.SetActive(false);
            }

            public void Update(in StellarObjectVisualUpdateContext context)
            {
                lastDistanceMeters = context.DistanceToCameraMeters;

                var physicalHorizonRadius = Math.Max(
                    context.Data.RadiusMeters,
                    0.0);
                var minimumHorizonRadius =
                    StellarObjectPresentationUtility
                        .GetMinimumAngularRadiusMeters(
                            context.DistanceToCameraMeters,
                            minimumHorizonAngularDiameterDegrees);
                lastPresentationHorizonRadiusMeters = Math.Max(
                    physicalHorizonRadius,
                    minimumHorizonRadius);

                StellarObjectPresentationUtility.ApplyTransform(
                    root,
                    context.RelativePositionMeters,
                    lastPresentationHorizonRadiusMeters,
                    context.MetersPerUnityUnit);

                UpdateLensing(context);

                if (disk == null)
                    return;

                var physicalDiskRadius = context.Data.RadiusMeters *
                                         diskOuterRadiusInHorizonRadii;
                var minimumDiskRadius = StellarObjectPresentationUtility
                    .GetMinimumAngularRadiusMeters(
                        context.DistanceToCameraMeters,
                        minimumDiskAngularDiameterDegrees);
                lastDiskRadiusMeters = Math.Max(
                    physicalDiskRadius,
                    minimumDiskRadius);

                // The disk mesh uses the same 1.2..12 radius range as its
                // shader. Compensate for both the root's diameter scale and
                // the outer mesh radius so the disk's outer edge is exactly
                // lastDiskRadiusMeters from the black-hole centre.
                var denominator = Math.Max(
                    lastPresentationHorizonRadiusMeters * 2.0,
                    0.0000001);
                var localScale = (float)(
                    lastDiskRadiusMeters /
                    denominator /
                    DiskMeshOuterRadius);
                disk.localScale = Vector3.one * localScale;

                StellarObjectPresentationUtility.SetFloat(
                    diskInstance,
                    SurfaceTime,
                    (float)context.SimulationTimeSeconds);
            }

            public bool IsDistantPointReplacementReady(
                float requiredAngularDiameterDegrees)
            {
                var visibleRadius = disk == null
                    ? lastPresentationHorizonRadiusMeters
                    : Math.Max(
                        lastPresentationHorizonRadiusMeters,
                        lastDiskRadiusMeters);

                return StellarObjectPresentationUtility
                           .GetAngularDiameterDegrees(
                               visibleRadius,
                               lastDistanceMeters) >=
                       requiredAngularDiameterDegrees;
            }

            public void Dispose()
            {
                StellarObjectPresentationUtility.DestroyObject(horizonInstance);
                StellarObjectPresentationUtility.DestroyObject(lensingInstance);
                StellarObjectPresentationUtility.DestroyObject(diskInstance);
                StellarObjectPresentationUtility.DestroyObject(lensingDiscMesh);
                StellarObjectPresentationUtility.DestroyObject(
                    lensingDisc?.gameObject);
                StellarObjectPresentationUtility.DestroyObject(root?.gameObject);
            }

            private void UpdateLensing(
                in StellarObjectVisualUpdateContext context)
            {
                if (lensingInstance == null || lensingDisc == null)
                    return;

                var camera = context.Camera != null
                    ? context.Camera
                    : Camera.main;
                if (!isVisible || camera == null || root == null)
                {
                    lensingDisc.gameObject.SetActive(false);
                    return;
                }

                var centre = camera.WorldToViewportPoint(root.position);
                var horizonWorldRadius = Mathf.Max(
                    root.lossyScale.x * 0.5f,
                    MinimumWorldRadius);
                var distanceToCentre = Vector3.Distance(
                    camera.transform.position,
                    root.position);

                // The event horizon must remain solid when the camera enters
                // it. There is no external scene to refract from that point.
                if (centre.z <= 0.0f ||
                    distanceToCentre <=
                    horizonWorldRadius * CameraInsideHorizonPadding)
                {
                    lensingDisc.gameObject.SetActive(false);
                    return;
                }

                var horizonRadius = GetSphereViewportRadius(
                    camera,
                    root.position,
                    horizonWorldRadius,
                    centre);
                if (horizonRadius <= MinimumViewportRadius)
                {
                    lensingDisc.gameObject.SetActive(false);
                    return;
                }

                var effectiveLensingRadius = Mathf.Max(
                    lensingRadiusInHorizonRadii *
                    LensingRadiusVisualMultiplier,
                    1.1f);
                var lensRadius = horizonRadius *
                                 effectiveLensingRadius;
                var planeHalfHeight = GetCameraPlaneHalfHeight(
                    camera,
                    centre.z);
                var lensWorldRadius = lensRadius * planeHalfHeight * 2.0f;
                if (lensWorldRadius <= MinimumWorldRadius)
                {
                    lensingDisc.gameObject.SetActive(false);
                    return;
                }

                var planePosition = camera.ViewportToWorldPoint(
                    new Vector3(centre.x, centre.y, centre.z));
                lensingDisc.SetPositionAndRotation(
                    planePosition,
                    camera.transform.rotation);
                lensingDisc.localScale = Vector3.one * lensWorldRadius;
                lensingDisc.gameObject.SetActive(true);

                SetFloat(
                    lensingInstance,
                    SurfaceTime,
                    (float)context.SimulationTimeSeconds);
                SetVector(
                    lensingInstance,
                    LensCenterViewport,
                    new Vector4(centre.x, centre.y, 0.0f, 0.0f));
                SetFloat(lensingInstance, LensRadius, lensRadius);
                SetFloat(lensingInstance, HorizonRadius, horizonRadius);
            }

            private static float GetCameraPlaneHalfHeight(
                Camera camera,
                float depth)
            {
                if (camera.orthographic)
                    return Mathf.Max(camera.orthographicSize, MinimumWorldRadius);

                return Mathf.Max(
                    Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f) *
                    depth,
                    MinimumWorldRadius);
            }

            private static float GetSphereViewportRadius(
                Camera camera,
                Vector3 centreWorldPosition,
                float worldRadius,
                Vector3 centreViewport)
            {
                var cameraToCentre = centreWorldPosition -
                                     camera.transform.position;
                var distance = cameraToCentre.magnitude;
                if (distance <= worldRadius)
                    return 0.0f;

                var viewDirection = cameraToCentre / distance;
                var screenAxis = Vector3.ProjectOnPlane(
                    camera.transform.right,
                    viewDirection);
                if (screenAxis.sqrMagnitude <= MinimumWorldRadius)
                {
                    screenAxis = Vector3.ProjectOnPlane(
                        camera.transform.up,
                        viewDirection);
                }

                if (screenAxis.sqrMagnitude <= MinimumWorldRadius)
                    return 0.0f;

                screenAxis.Normalize();

                if (camera.orthographic)
                {
                    var orthographicEdge = camera.WorldToViewportPoint(
                        centreWorldPosition + screenAxis * worldRadius);
                    return GetAspectCorrectedViewportDistance(
                        camera,
                        centreViewport,
                        orthographicEdge);
                }

                var radiusOverDistance = Mathf.Clamp01(worldRadius / distance);
                var tangentLateralDistance = worldRadius * Mathf.Sqrt(
                    Mathf.Max(
                        0.0f,
                        1.0f - radiusOverDistance * radiusOverDistance));
                var tangentDepthOffset = worldRadius * radiusOverDistance;
                var tangentWorldPosition = centreWorldPosition +
                                           screenAxis * tangentLateralDistance -
                                           viewDirection * tangentDepthOffset;
                var tangentViewport = camera.WorldToViewportPoint(
                    tangentWorldPosition);
                if (tangentViewport.z <= 0.0f)
                    return 0.0f;

                return GetAspectCorrectedViewportDistance(
                    camera,
                    centreViewport,
                    tangentViewport);
            }

            private static float GetAspectCorrectedViewportDistance(
                Camera camera,
                Vector3 first,
                Vector3 second)
            {
                var aspect = camera.pixelHeight > 0
                    ? camera.pixelWidth / (float)camera.pixelHeight
                    : 1.0f;
                var delta = new Vector2(
                    (second.x - first.x) * Mathf.Max(aspect, 0.0001f),
                    second.y - first.y);
                return delta.magnitude;
            }

            private static void ConfigureHorizonMaterial(
                Material material,
                BlackHoleData data,
                int objectIndex)
            {
                if (material == null)
                    return;

                var photonColor = data.HasAccretionDisk
                    ? new Color(0.92f, 0.78f, 0.48f, 1.0f)
                    : new Color(0.20f, 0.38f, 0.68f, 1.0f);
                SetColor(
                    material,
                    Shader.PropertyToID("_PhotonRingColor"),
                    photonColor);
                SetFloat(
                    material,
                    Shader.PropertyToID("_PhotonRingIntensity"),
                    data.HasAccretionDisk ? 0.28f : 0.12f);
                SetFloat(material, ApparentShadowScale, 1.0f);
                SetFloat(material, Seed, GetSeed(data, objectIndex, 43));

                // The lens is drawn first; the solid horizon is then drawn over
                // the centre from both sides of the sphere.
                material.renderQueue = 3110;
            }

            private static Material CreateLensingMaterial(
                BlackHoleData data,
                int objectIndex,
                float strength,
                float edgeSoftness,
                Color ringColor,
                float ringIntensity,
                float swirlStrength,
                float swirlFalloff,
                float swirlDirection)
            {
                var shader = Shader.Find(
                    "SpaceEngine/Streaming/Black Hole Lens");
                if (shader == null)
                    return null;

                var material = new Material(shader)
                {
                    name = "Black Hole Gravitational Lensing Material",
                    renderQueue = 3100
                };
                SetFloat(material, Seed, GetSeed(data, objectIndex, 47));
                SetFloat(material, LensingStrength, strength);
                SetFloat(material, LensRadius, 0.10f);
                SetFloat(material, HorizonRadius, 0.04f);
                SetFloat(material, LensEdgeSoftness, edgeSoftness);
                SetColor(material, LensRingColor, ringColor);
                SetFloat(material, LensRingIntensity, ringIntensity);
                SetFloat(material, SwirlStrength, swirlStrength);
                SetFloat(material, SwirlFalloff, swirlFalloff);
                SetFloat(material, SwirlDirection, swirlDirection);
                return material;
            }

            private static Quaternion GetDiskRotation(
                BlackHoleData data,
                int objectIndex)
            {
                var normal = GetUnitDirection(data, objectIndex, 97);
                var spin = GetSeed(data, objectIndex, 101) * 360.0f;
                return Quaternion.AngleAxis(spin, normal) *
                       Quaternion.FromToRotation(Vector3.up, normal);
            }

            private static Vector3 GetUnitDirection(
                BlackHoleData data,
                int objectIndex,
                int salt)
            {
                var theta = Mathf.PI * 2.0f *
                            GetSeed(data, objectIndex, salt);
                var y = Mathf.Lerp(
                    -0.92f,
                    0.92f,
                    GetSeed(data, objectIndex, salt + 1));
                var horizontal = Mathf.Sqrt(
                    Mathf.Max(0.0f, 1.0f - y * y));

                return new Vector3(
                    Mathf.Cos(theta) * horizontal,
                    y,
                    Mathf.Sin(theta) * horizontal);
            }

            private static float GetSeed(
                BlackHoleData data,
                int objectIndex,
                int salt)
            {
                unchecked
                {
                    var value = (ulong)BitConverter.DoubleToInt64Bits(data.MassKg);
                    value ^= (ulong)BitConverter.DoubleToInt64Bits(
                        data.RadiusMeters) * 0x9E3779B97F4A7C15UL;
                    value ^= (ulong)BitConverter.DoubleToInt64Bits(
                        data.RotationPeriodSeconds) * 0xD1B54A32D192ED03UL;
                    value ^= (ulong)(objectIndex + 1) * 0x94D049BB133111EBUL;
                    value ^= (ulong)(salt + 1) * 0xBF58476D1CE4E5B9UL;
                    value ^= value >> 30;
                    value *= 0xBF58476D1CE4E5B9UL;
                    value ^= value >> 27;
                    value *= 0x94D049BB133111EBUL;
                    value ^= value >> 31;
                    return (float)((value >> 40) / (double)(1UL << 24));
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

            private static void SetVector(
                Material material,
                int propertyId,
                Vector4 value)
            {
                if (material != null && material.HasProperty(propertyId))
                    material.SetVector(propertyId, value);
            }

            private static Mesh CreateLensingDiscMesh()
            {
                var vertices = new Vector3[LensingDiscSegments + 1];
                var uv = new Vector2[vertices.Length];
                var triangles = new int[LensingDiscSegments * 3];

                vertices[0] = Vector3.zero;
                uv[0] = new Vector2(0.5f, 0.5f);

                for (var index = 0; index < LensingDiscSegments; index++)
                {
                    var t = index / (float)LensingDiscSegments;
                    var angle = t * Mathf.PI * 2.0f;
                    var vertexIndex = index + 1;
                    var x = Mathf.Cos(angle);
                    var y = Mathf.Sin(angle);
                    vertices[vertexIndex] = new Vector3(x, y, 0.0f);
                    uv[vertexIndex] = new Vector2(
                        x * 0.5f + 0.5f,
                        y * 0.5f + 0.5f);

                    var nextIndex = index == LensingDiscSegments - 1
                        ? 1
                        : vertexIndex + 1;
                    var triangleIndex = index * 3;
                    triangles[triangleIndex] = 0;
                    triangles[triangleIndex + 1] = vertexIndex;
                    triangles[triangleIndex + 2] = nextIndex;
                }

                var mesh = new Mesh
                {
                    name = "Black Hole Gravitational Lensing Disc",
                    vertices = vertices,
                    uv = uv,
                    triangles = triangles,
                    bounds = new Bounds(
                        Vector3.zero,
                        Vector3.one * 1000000.0f)
                };
                return mesh;
            }

            private static Mesh CreateDiskMesh()
            {
                const int segments = 64;
                const float inner = DiskMeshInnerRadius;
                const float outer = DiskMeshOuterRadius;

                var vertices = new Vector3[(segments + 1) * 2];
                var uv = new Vector2[vertices.Length];
                var triangles = new int[segments * 6];

                for (var index = 0; index <= segments; index++)
                {
                    var t = index / (float)segments;
                    var angle = t * Mathf.PI * 2.0f;
                    var cosine = Mathf.Cos(angle);
                    var sine = Mathf.Sin(angle);

                    var innerIndex = index * 2;
                    var outerIndex = innerIndex + 1;
                    vertices[innerIndex] =
                        new Vector3(cosine * inner, 0.0f, sine * inner);
                    vertices[outerIndex] =
                        new Vector3(cosine * outer, 0.0f, sine * outer);
                    uv[innerIndex] = new Vector2(t, 0.0f);
                    uv[outerIndex] = new Vector2(t, 1.0f);

                    if (index == segments)
                        continue;

                    var triangle = index * 6;
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
