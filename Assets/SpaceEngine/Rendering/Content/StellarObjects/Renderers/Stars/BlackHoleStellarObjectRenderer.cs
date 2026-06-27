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
        private static readonly int Seed =
            Shader.PropertyToID("_Seed");
        private static readonly int HasAccretionDisk =
            Shader.PropertyToID("_HasAccretionDisk");
        private static readonly int HorizonColor =
            Shader.PropertyToID("_HorizonColor");
        private static readonly int LensingStrength =
            Shader.PropertyToID("_LensingStrength");
        private static readonly int LensingRadiusWorld =
            Shader.PropertyToID("_LensingRadiusWorld");
        private static readonly int LensEnabled =
            Shader.PropertyToID("_LensEnabled");
        private static readonly int LensCenterViewport =
            Shader.PropertyToID("_LensCenterViewport");
        private static readonly int LensRadius =
            Shader.PropertyToID("_LensRadius");
        private static readonly int HorizonRadius =
            Shader.PropertyToID("_HorizonRadius");
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
        private static readonly int BlackHoleCenterWorld =
            Shader.PropertyToID("_BlackHoleCenterWorld");
        private static readonly int BlackHoleRadiusWorld =
            Shader.PropertyToID("_BlackHoleRadiusWorld");
        private static readonly int AccretionRadiusWorld =
            Shader.PropertyToID("_AccretionRadiusWorld");
        private static readonly int DiskPlaneNormalWorld =
            Shader.PropertyToID("_DiskPlaneNormalWorld");
        private static readonly int DiskPlaneRightWorld =
            Shader.PropertyToID("_DiskPlaneRightWorld");
        private static readonly int DiskPlaneForwardWorld =
            Shader.PropertyToID("_DiskPlaneForwardWorld");
        private static readonly int DiskInnerRadiusWorld =
            Shader.PropertyToID("_DiskInnerRadiusWorld");
        private static readonly int DiskHalfThicknessWorld =
            Shader.PropertyToID("_DiskHalfThicknessWorld");
        private static readonly int RaymarchSteps =
            Shader.PropertyToID("_RaymarchSteps");
        private static readonly int VolumeOpacity =
            Shader.PropertyToID("_VolumeOpacity");
        private static readonly int VolumeBrightness =
            Shader.PropertyToID("_VolumeBrightness");
        private static readonly int DiskTwist =
            Shader.PropertyToID("_Twist");
        private static readonly int DiskTemperature =
            Shader.PropertyToID("_Temperature");
        private static readonly int DiskSpeed =
            Shader.PropertyToID("_Speed");
        private static readonly int DiskRedshift =
            Shader.PropertyToID("_Redshift");

        private const float MinimumDiskShaderTemperature = 0.1f;
        private const float MaximumDiskShaderTemperature = 4.0f;
        private const double MinimumDiskTemperatureKelvin = 1_200.0;
        private const double MaximumDiskTemperatureKelvin = 20_000.0;

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
        [SerializeField, Min(1.1f)]
        private float diskOuterRadiusInHorizonRadii = 20.0f;

        [Tooltip(
            "Half-thickness of the volumetric accretion gas relative to the " +
            "event-horizon radius. The disk remains visibly gaseous even " +
            "when viewed nearly edge-on.")]
        [SerializeField, Range(0.05f, 4.0f)]
        private float diskHalfThicknessInHorizonRadii = 1.08f;

        [Tooltip(
            "Maximum number of unified volume/lensing samples per visible " +
            "black-hole ray. Higher values improve detail at a proportional " +
            "GPU cost.")]
        [SerializeField, Range(8, 64)]
        private int diskRaymarchSteps = 32;

        [SerializeField, Range(0.05f, 4.0f)]
        private float diskVolumeOpacity = 4.0f;

        [SerializeField, Range(0.0f, 8.0f)]
        private float diskVolumeBrightness = 2.35f;

        [SerializeField, Range(0.001f, 10.0f)]
        private float minimumDiskAngularDiameterDegrees = 0.20f;

        [Tooltip(
            "LOD 1 safety floor for the visible accretion disk. It is used " +
            "even when an existing renderer asset still has an older, very " +
            "small Minimum Disk Angular Diameter value.")]
        [SerializeField, Range(0.01f, 10.0f)]
        private float minimumLod1DiskAngularDiameterDegrees = 0.75f;

        [SerializeField, Range(0.001f, 0.05f)]
        private float diskLensingCutoff = 0.0025f;

        [SerializeField, Range(0.0f, 24.0f)]
        private float diskTwist = 28.0f;

        [SerializeField, Range(0.0f, 1.0f)]
        private float diskAnimationSpeed = 0.12f;

        [SerializeField, Range(0.0f, 1.0f)]
        private float diskRedshift = 0.12f;

        public override IStellarObjectVisual CreateVisual(
            in StellarObjectRenderContext context)
        {
            var blackHole = (BlackHoleData)context.Data;
            return new BlackHoleVisual(
                context,
                blackHole,
                horizonColor,
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
                Mathf.Clamp(diskHalfThicknessInHorizonRadii, 0.05f, 4.0f),
                Mathf.Clamp(diskRaymarchSteps, 8, 64),
                Mathf.Clamp(diskVolumeOpacity, 0.05f, 4.0f),
                Mathf.Clamp(diskVolumeBrightness, 0.0f, 8.0f),
                minimumDiskAngularDiameterDegrees,
                Mathf.Clamp(diskLensingCutoff, 0.0001f, 0.05f),
                Mathf.Clamp(diskTwist, -64.0f, 64.0f),
                Mathf.Clamp01(diskAnimationSpeed),
                Mathf.Clamp01(diskRedshift));
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
            color = GetAccretionDiskColour(
                GetDiskShaderTemperature(blackHole.TemperatureKelvin) *
                1.25f);
            intensity = blackHole.HasAccretionDisk ? 2.5f : 0.4f;
            return true;
        }

        private static float GetDiskShaderTemperature(
            double temperatureKelvin)
        {
            return Mathf.Lerp(
                MinimumDiskShaderTemperature,
                MaximumDiskShaderTemperature,
                Mathf.InverseLerp(
                    (float)MinimumDiskTemperatureKelvin,
                    (float)MaximumDiskTemperatureKelvin,
                    (float)Math.Max(0.0, temperatureKelvin)));
        }

        private static Color GetAccretionDiskColour(float temperature)
        {
            var normalizedTemperature = Mathf.InverseLerp(
                0.1f,
                4.0f,
                Mathf.Max(temperature, 0.1f));

            var deepRed = new Color(1.0f, 0.035f, 0.004f, 1.0f);
            var orange = new Color(1.0f, 0.32f, 0.018f, 1.0f);
            var yellow = new Color(1.0f, 0.74f, 0.22f, 1.0f);
            var white = new Color(1.0f, 0.94f, 0.78f, 1.0f);
            var blueWhite = new Color(0.72f, 0.84f, 1.0f, 1.0f);

            var colour = Color.Lerp(
                deepRed,
                orange,
                GetSmoothStep(0.02f, 0.26f, normalizedTemperature));
            colour = Color.Lerp(
                colour,
                yellow,
                GetSmoothStep(0.20f, 0.55f, normalizedTemperature));
            colour = Color.Lerp(
                colour,
                white,
                GetSmoothStep(0.48f, 0.75f, normalizedTemperature));
            return Color.Lerp(
                colour,
                blueWhite,
                GetSmoothStep(0.76f, 1.0f, normalizedTemperature));
        }

        private static float GetSmoothStep(
            float edge0,
            float edge1,
            float value)
        {
            return Mathf.SmoothStep(
                0.0f,
                1.0f,
                Mathf.InverseLerp(edge0, edge1, value));
        }

        private sealed class BlackHoleVisual : IStellarObjectVisual
        {
            private const float MinimumWorldRadius = 0.000001f;
            private const float CameraInsideHorizonPadding = 1.001f;
            private const float LensingRadiusVisualMultiplier = 1.45f;
            private const float DiskInnerRadiusInHorizonRadii = 1.18f;

            private readonly Transform root;
            private readonly Transform composite;
            private readonly Mesh compositeMesh;
            private readonly Material compositeMaterial;
            private readonly bool hasAccretionDisk;
            private readonly Quaternion diskPlaneRotation;
            private readonly float lensingRadiusInHorizonRadii;
            private readonly float diskOuterRadiusInHorizonRadii;
            private readonly float diskHalfThicknessInHorizonRadii;
            private readonly int diskRaymarchSteps;
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
                float diskHalfThicknessInHorizonRadii,
                int diskRaymarchSteps,
                float diskVolumeOpacity,
                float diskVolumeBrightness,
                float minimumDiskAngularDiameterDegrees,
                float diskLensingCutoff,
                float diskTwist,
                float diskAnimationSpeed,
                float diskRedshift)
            {
                root = StellarObjectPresentationUtility.CreateRoot(
                    context,
                    $"Black Hole {context.ObjectIndex}");
                hasAccretionDisk = blackHole.HasAccretionDisk;
                diskPlaneRotation = GetDiskRotation(blackHole, context.ObjectIndex);
                this.lensingRadiusInHorizonRadii = Mathf.Clamp(
                    lensingRadiusInHorizonRadii,
                    1.1f,
                    8.0f);
                this.diskOuterRadiusInHorizonRadii = Mathf.Max(
                    1.1f,
                    diskOuterRadiusInHorizonRadii);
                this.diskHalfThicknessInHorizonRadii = Mathf.Clamp(
                    diskHalfThicknessInHorizonRadii,
                    0.05f,
                    4.0f);
                this.diskRaymarchSteps = Mathf.Clamp(
                    diskRaymarchSteps,
                    8,
                    64);
                this.minimumHorizonAngularDiameterDegrees = Mathf.Max(
                    0.01f,
                    minimumHorizonAngularDiameterDegrees);
                this.minimumDiskAngularDiameterDegrees = Mathf.Max(
                    0.01f,
                    Mathf.Max(
                        minimumDiskAngularDiameterDegrees,
                        minimumLod1DiskAngularDiameterDegrees));

                var compositeObject = new GameObject("Black Hole Unified Composite")
                {
                    layer = context.Layer
                };
                composite = compositeObject.transform;
                composite.SetParent(root, false);
                compositeMesh = CreateFullscreenTriangleMesh();
                compositeObject.AddComponent<MeshFilter>().sharedMesh = compositeMesh;

                var compositeRenderer = compositeObject.AddComponent<MeshRenderer>();
                compositeRenderer.shadowCastingMode = ShadowCastingMode.Off;
                compositeRenderer.receiveShadows = false;
                compositeMaterial = CreateUnifiedMaterial(
                    blackHole,
                    context.ObjectIndex,
                    horizonColor,
                    lensRingColor,
                    lensingStrength,
                    lensEdgeSoftness,
                    lensRingIntensity,
                    swirlStrength,
                    swirlFalloff,
                    swirlDirection,
                    diskRaymarchSteps,
                    diskVolumeOpacity,
                    diskVolumeBrightness,
                    diskLensingCutoff,
                    diskTwist,
                    diskAnimationSpeed,
                    diskRedshift);
                compositeRenderer.sharedMaterial = compositeMaterial;
                compositeObject.SetActive(false);
            }

            public void SetVisible(bool isVisible)
            {
                this.isVisible = isVisible;

                if (root != null)
                    root.gameObject.SetActive(isVisible);

                if (composite != null)
                    composite.gameObject.SetActive(false);
            }

            public void Update(in StellarObjectVisualUpdateContext context)
            {
                lastDistanceMeters = context.DistanceToCameraMeters;

                var physicalHorizonRadius = Math.Max(
                    context.Data.RadiusMeters,
                    0.0);
                var minimumHorizonRadius =
                    StellarObjectPresentationUtility.GetMinimumAngularRadiusMeters(
                        context.DistanceToCameraMeters,
                        minimumHorizonAngularDiameterDegrees);
                lastPresentationHorizonRadiusMeters = Math.Max(
                    physicalHorizonRadius,
                    minimumHorizonRadius);

                if (hasAccretionDisk)
                {
                    var physicalDiskRadius = context.Data.RadiusMeters *
                                             diskOuterRadiusInHorizonRadii;
                    var minimumDiskRadius =
                        StellarObjectPresentationUtility.GetMinimumAngularRadiusMeters(
                            context.DistanceToCameraMeters,
                            minimumDiskAngularDiameterDegrees);
                    lastDiskRadiusMeters = Math.Max(
                        physicalDiskRadius,
                        minimumDiskRadius);
                }
                else
                {
                    lastDiskRadiusMeters = lastPresentationHorizonRadiusMeters;
                }

                StellarObjectPresentationUtility.ApplyTransform(
                    root,
                    context.RelativePositionMeters,
                    lastPresentationHorizonRadiusMeters,
                    context.MetersPerUnityUnit);

                UpdateComposite(context);
            }

            public bool IsDistantPointReplacementReady(
                float requiredAngularDiameterDegrees)
            {
                var visibleRadius = hasAccretionDisk
                    ? Math.Max(
                        lastPresentationHorizonRadiusMeters,
                        lastDiskRadiusMeters)
                    : lastPresentationHorizonRadiusMeters;

                return StellarObjectPresentationUtility
                           .GetAngularDiameterDegrees(
                               visibleRadius,
                               lastDistanceMeters) >=
                       requiredAngularDiameterDegrees;
            }

            public void Dispose()
            {
                StellarObjectPresentationUtility.DestroyObject(compositeMaterial);
                StellarObjectPresentationUtility.DestroyObject(compositeMesh);
                StellarObjectPresentationUtility.DestroyObject(root?.gameObject);
            }

            private void UpdateComposite(
                in StellarObjectVisualUpdateContext context)
            {
                if (compositeMaterial == null || composite == null)
                    return;

                var camera = context.Camera != null
                    ? context.Camera
                    : Camera.main;
                if (!isVisible || camera == null || root == null)
                {
                    composite.gameObject.SetActive(false);
                    return;
                }

                var horizonWorldRadius = Mathf.Max(
                    (float)(lastPresentationHorizonRadiusMeters /
                            Math.Max(context.MetersPerUnityUnit, 1.0)),
                    MinimumWorldRadius);
                var diskWorldRadius = hasAccretionDisk
                    ? Mathf.Max(
                        (float)(lastDiskRadiusMeters /
                                Math.Max(context.MetersPerUnityUnit, 1.0)),
                        horizonWorldRadius * 1.1f)
                    : horizonWorldRadius * 1.1f;
                var diskHalfThicknessWorld = Mathf.Clamp(
                    horizonWorldRadius * diskHalfThicknessInHorizonRadii,
                    horizonWorldRadius * 0.025f,
                    diskWorldRadius * 0.45f);

                var diskNormalWorld = diskPlaneRotation * Vector3.up;
                var diskRightWorld = diskPlaneRotation * Vector3.right;
                var diskForwardWorld = diskPlaneRotation * Vector3.forward;
                var effectiveLensingRadius = Mathf.Max(
                    lensingRadiusInHorizonRadii *
                    LensingRadiusVisualMultiplier,
                    1.1f);
                var lensingRadiusWorld = horizonWorldRadius *
                                          effectiveLensingRadius;

                var centreViewport = camera.WorldToViewportPoint(root.position);
                var distanceToCentre = Vector3.Distance(
                    camera.transform.position,
                    root.position);
                var canLensScreen = centreViewport.z > 0.0f &&
                                    distanceToCentre >
                                    horizonWorldRadius *
                                    CameraInsideHorizonPadding;
                var horizonViewportRadius = canLensScreen
                    ? GetSphereViewportRadius(
                        camera,
                        root.position,
                        horizonWorldRadius,
                        centreViewport)
                    : 0.0f;
                var lensViewportRadius = horizonViewportRadius *
                                         effectiveLensingRadius;

                var proxyDepth = Mathf.Max(
                    camera.nearClipPlane * 4.0f,
                    0.01f);
                var proxyHalfHeight = GetCameraPlaneHalfHeight(
                                          camera,
                                          proxyDepth) *
                                      1.08f;
                var proxyHalfWidth = proxyHalfHeight *
                                     Mathf.Max(camera.aspect, 0.0001f) *
                                     1.08f;
                composite.SetPositionAndRotation(
                    camera.transform.position +
                    camera.transform.forward * proxyDepth,
                    camera.transform.rotation);
                SetWorldScale(
                    composite,
                    new Vector3(proxyHalfWidth, proxyHalfHeight, 1.0f));
                composite.gameObject.SetActive(true);

                SetFloat(
                    compositeMaterial,
                    SurfaceTime,
                    (float)context.SimulationTimeSeconds);
                SetFloat(
                    compositeMaterial,
                    HasAccretionDisk,
                    hasAccretionDisk ? 1.0f : 0.0f);
                SetVector(
                    compositeMaterial,
                    BlackHoleCenterWorld,
                    new Vector4(
                        root.position.x,
                        root.position.y,
                        root.position.z,
                        1.0f));
                SetFloat(
                    compositeMaterial,
                    BlackHoleRadiusWorld,
                    horizonWorldRadius);
                SetFloat(
                    compositeMaterial,
                    LensingRadiusWorld,
                    lensingRadiusWorld);
                SetFloat(
                    compositeMaterial,
                    LensEnabled,
                    canLensScreen ? 1.0f : 0.0f);
                SetVector(
                    compositeMaterial,
                    LensCenterViewport,
                    new Vector4(
                        centreViewport.x,
                        centreViewport.y,
                        0.0f,
                        0.0f));
                SetFloat(
                    compositeMaterial,
                    HorizonRadius,
                    horizonViewportRadius);
                SetFloat(
                    compositeMaterial,
                    LensRadius,
                    lensViewportRadius);
                SetFloat(
                    compositeMaterial,
                    AccretionRadiusWorld,
                    diskWorldRadius);
                SetFloat(
                    compositeMaterial,
                    DiskHalfThicknessWorld,
                    diskHalfThicknessWorld);
                SetFloat(
                    compositeMaterial,
                    DiskInnerRadiusWorld,
                    horizonWorldRadius *
                    DiskInnerRadiusInHorizonRadii);
                SetVector(
                    compositeMaterial,
                    DiskPlaneNormalWorld,
                    new Vector4(
                        diskNormalWorld.x,
                        diskNormalWorld.y,
                        diskNormalWorld.z,
                        0.0f));
                SetVector(
                    compositeMaterial,
                    DiskPlaneRightWorld,
                    new Vector4(
                        diskRightWorld.x,
                        diskRightWorld.y,
                        diskRightWorld.z,
                        0.0f));
                SetVector(
                    compositeMaterial,
                    DiskPlaneForwardWorld,
                    new Vector4(
                        diskForwardWorld.x,
                        diskForwardWorld.y,
                        diskForwardWorld.z,
                        0.0f));
            }

            private static Material CreateUnifiedMaterial(
                BlackHoleData data,
                int objectIndex,
                Color horizonColor,
                Color lensRingColor,
                float lensingStrength,
                float lensEdgeSoftness,
                float lensRingIntensity,
                float swirlStrength,
                float swirlFalloff,
                float swirlDirection,
                int raymarchSteps,
                float volumeOpacity,
                float volumeBrightness,
                float diskLensingCutoff,
                float diskTwist,
                float diskAnimationSpeed,
                float diskRedshift)
            {
                var shader = Shader.Find(
                    "SpaceEngine/Streaming/Black Hole Unified");
                if (shader == null)
                {
                    Debug.LogError(
                        "SpaceEngine unified black-hole shader was not found: " +
                        "SpaceEngine/Streaming/Black Hole Unified.");
                    return null;
                }

                var material = new Material(shader)
                {
                    name = "Black Hole Unified Composite Material",
                    renderQueue = 3125
                };
                SetColor(material, HorizonColor, horizonColor);
                SetColor(material, LensRingColor, lensRingColor);
                SetFloat(
                    material,
                    LensingStrength,
                    Mathf.Clamp(lensingStrength, 0.0f, 2.0f));
                SetFloat(
                    material,
                    LensEdgeSoftness,
                    Mathf.Clamp(lensEdgeSoftness, 0.01f, 0.50f));
                SetFloat(
                    material,
                    LensRingIntensity,
                    Mathf.Clamp01(lensRingIntensity));
                SetFloat(
                    material,
                    SwirlStrength,
                    Mathf.Clamp(swirlStrength, 0.0f, 12.0f));
                SetFloat(
                    material,
                    SwirlFalloff,
                    Mathf.Clamp(swirlFalloff, 0.25f, 6.0f));
                SetFloat(
                    material,
                    SwirlDirection,
                    Mathf.Clamp(swirlDirection, -1.0f, 1.0f));
                SetFloat(
                    material,
                    RaymarchSteps,
                    Mathf.Clamp(raymarchSteps, 8, 64));
                SetFloat(
                    material,
                    VolumeOpacity,
                    Mathf.Clamp(volumeOpacity, 0.05f, 4.0f));
                SetFloat(
                    material,
                    VolumeBrightness,
                    Mathf.Clamp(volumeBrightness, 0.0f, 8.0f));
                SetFloat(
                    material,
                    DiskTwist,
                    Mathf.Clamp(diskTwist, -64.0f, 64.0f));
                SetFloat(
                    material,
                    DiskTemperature,
                    GetDiskShaderTemperature(data.TemperatureKelvin));
                SetFloat(material, DiskSpeed, diskAnimationSpeed);
                SetFloat(material, DiskRedshift, diskRedshift);
                SetFloat(material, Seed, GetSeed(data, objectIndex, 59));
                SetFloat(material, Shader.PropertyToID("_Cutoff"), diskLensingCutoff);
                return material;
            }

            private static void SetWorldScale(
                Transform transform,
                Vector3 worldScale)
            {
                if (transform == null)
                    return;

                var parent = transform.parent;
                if (parent == null)
                {
                    transform.localScale = worldScale;
                    return;
                }

                var parentScale = parent.lossyScale;
                transform.localScale = new Vector3(
                    worldScale.x / Mathf.Max(
                        Mathf.Abs(parentScale.x),
                        MinimumWorldRadius),
                    worldScale.y / Mathf.Max(
                        Mathf.Abs(parentScale.y),
                        MinimumWorldRadius),
                    worldScale.z / Mathf.Max(
                        Mathf.Abs(parentScale.z),
                        MinimumWorldRadius));
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

            private static void SetColor(
                Material material,
                int propertyId,
                Color value)
            {
                if (material != null && material.HasProperty(propertyId))
                    material.SetColor(propertyId, value);
            }

            private static Mesh CreateFullscreenTriangleMesh()
            {
                var mesh = new Mesh
                {
                    name = "Black Hole Unified Fullscreen Triangle",
                    vertices = new[]
                    {
                        new Vector3(-1.0f, -1.0f, 0.0f),
                        new Vector3( 3.0f, -1.0f, 0.0f),
                        new Vector3(-1.0f,  3.0f, 0.0f)
                    },
                    uv = new[]
                    {
                        new Vector2(0.0f, 0.0f),
                        new Vector2(2.0f, 0.0f),
                        new Vector2(0.0f, 2.0f)
                    },
                    triangles = new[] { 0, 1, 2 },
                    bounds = new Bounds(Vector3.zero, Vector3.one * 1000000.0f)
                };
                return mesh;
            }
        }
    }
}
