using System;
using SpaceEngine.Runtime.Content.StellarObjects.Rendering;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Data.SolarSystem.Objects;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Stars
{
    public abstract class StarStellarObjectRenderer : StellarObjectRenderer
    {
        private static readonly int SurfaceTime = Shader.PropertyToID("_SurfaceTime");

        [Header("LOD 1")]
        [SerializeField] private Color surfaceColor;
        [SerializeField] private Color coronaColor;
        [SerializeField, Range(0.001f, 10.0f)]
        private float minimumAngularDiameterDegrees = 0.20f;
        [SerializeField, Min(1.0f)]
        private float minimumDiameterInUnityUnits = 8.0f;
        [SerializeField, Min(1.0f)]
        private float coronaRadiusMultiplier = 1.055f;

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
                Multiply(ResolveColor(surfaceColor, DefaultSurfaceColor),
                    temperatureColor),
                Multiply(ResolveColor(coronaColor, DefaultCoronaColor),
                    temperatureColor),
                minimumAngularDiameterDegrees,
                minimumDiameterInUnityUnits,
                coronaRadiusMultiplier);
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
            private readonly Material surfaceInstance;
            private readonly Material coronaInstance;
            private readonly float minimumAngularDiameterDegrees;
            private readonly float minimumDiameterInUnityUnits;
            private double lastPresentationRadiusMeters;
            private double lastDistanceMeters;

            public StarVisual(
                in StellarObjectRenderContext context,
                Color surfaceColor,
                Color coronaColor,
                float minimumAngularDiameterDegrees,
                float minimumDiameterInUnityUnits,
                float coronaRadiusMultiplier)
            {
                root = StellarObjectPresentationUtility.CreateRoot(
                    context,
                    context.Data.Entity == null
                        ? $"Star {context.ObjectIndex}"
                        : context.Data.Entity.DisplayName);
                this.minimumAngularDiameterDegrees =
                    Mathf.Max(0.001f, minimumAngularDiameterDegrees);
                this.minimumDiameterInUnityUnits =
                    Mathf.Max(1.0f, minimumDiameterInUnityUnits);

                var coronaScale = Mathf.Max(
                    1.01f,
                    coronaRadiusMultiplier);

                root.gameObject.AddComponent<MeshFilter>().sharedMesh =
                    StellarObjectPresentationUtility.GetSphereMesh();

                var surfaceRenderer = root.gameObject.AddComponent<MeshRenderer>();
                surfaceRenderer.shadowCastingMode = ShadowCastingMode.Off;
                surfaceRenderer.receiveShadows = false;
                surfaceInstance =
                    StellarObjectPresentationUtility.CreateStarSurfaceMaterial(
                        surfaceColor);
                surfaceRenderer.sharedMaterial = surfaceInstance;

                var coronaObject = new GameObject("Corona")
                {
                    layer = context.Layer
                };
                var coronaTransform = coronaObject.transform;
                coronaTransform.SetParent(root, false);
                coronaTransform.localScale = Vector3.one * coronaScale;
                coronaObject.AddComponent<MeshFilter>().sharedMesh =
                    StellarObjectPresentationUtility.GetSphereMesh();

                var coronaRenderer = coronaObject.AddComponent<MeshRenderer>();
                coronaRenderer.shadowCastingMode = ShadowCastingMode.Off;
                coronaRenderer.receiveShadows = false;
                coronaInstance =
                    StellarObjectPresentationUtility.CreateStarCoronaMaterial(
                        coronaColor);
                coronaRenderer.sharedMaterial = coronaInstance;
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

                var time = (float)context.SimulationTimeSeconds;
                StellarObjectPresentationUtility.SetFloat(
                    surfaceInstance,
                    SurfaceTime,
                    time);
                StellarObjectPresentationUtility.SetFloat(
                    coronaInstance,
                    SurfaceTime,
                    time);
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
                StellarObjectPresentationUtility.DestroyObject(
                    root?.gameObject);
            }
        }
    }
}
