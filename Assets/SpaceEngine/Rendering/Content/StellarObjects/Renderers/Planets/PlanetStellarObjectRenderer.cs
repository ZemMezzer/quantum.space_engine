using System;
using SpaceEngine.Runtime.Content.StellarObjects.Rendering;
using SpaceEngine.Runtime.Data.SolarSystem.Objects;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers.Planets
{
    public abstract class PlanetStellarObjectRenderer : StellarObjectRenderer
    {
        private static readonly int SurfaceTime = Shader.PropertyToID("_SurfaceTime");
        [SerializeField] private Color color;
        [SerializeField, Range(0.0f, 1.0f)]
        private float minimumAngularDiameterDegrees = 0.004f;
        [SerializeField, Min(0.01f)]
        private float minimumDiameterInUnityUnits = 1.0f;

        protected abstract Color DefaultColor { get; }

        protected void ApplyDefaultColor()
        {
            color = DefaultColor;
        }

        public override IStellarObjectVisual CreateVisual(
            in StellarObjectRenderContext context)
        {
            if (context.Data is not PlanetData)
            {
                throw new InvalidOperationException(
                    $"{name} is paired to {context.Data?.Entity?.DisplayName ?? "an unknown entity"} " +
                    "but requires PlanetData.");
            }

            return new PlanetVisual(
                context,
                color.a <= 0.0f ? DefaultColor : color,
                minimumAngularDiameterDegrees,
                minimumDiameterInUnityUnits);
        }

        private sealed class PlanetVisual : IStellarObjectVisual
        {
            private readonly Transform root;
            private readonly Material materialInstance;
            private readonly float minimumAngularDiameterDegrees;
            private readonly float minimumDiameterInUnityUnits;

            public PlanetVisual(
                in StellarObjectRenderContext context,
                Color color,
                float minimumAngularDiameterDegrees,
                float minimumDiameterInUnityUnits)
            {
                root = StellarObjectPresentationUtility.CreateRoot(
                    context,
                    context.Data.Entity == null
                        ? $"Planet {context.ObjectIndex}"
                        : context.Data.Entity.DisplayName);
                this.minimumAngularDiameterDegrees =
                    Mathf.Max(0.0f, minimumAngularDiameterDegrees);
                this.minimumDiameterInUnityUnits =
                    Mathf.Max(0.01f, minimumDiameterInUnityUnits);

                root.gameObject.AddComponent<MeshFilter>().sharedMesh =
                    StellarObjectPresentationUtility.GetSphereMesh();

                var renderer = root.gameObject.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                materialInstance =
                    StellarObjectPresentationUtility.CreatePlanetSurfaceMaterial(
                        color);
                renderer.sharedMaterial = materialInstance;
            }

            public void SetVisible(bool isVisible)
            {
                if (root != null)
                    root.gameObject.SetActive(isVisible);
            }

            public void Update(in StellarObjectVisualUpdateContext context)
            {
                var angularRadius =
                    StellarObjectPresentationUtility
                        .GetMinimumAngularRadiusMeters(
                            context.DistanceToCameraMeters,
                            minimumAngularDiameterDegrees);
                var unitFloor = context.MetersPerUnityUnit *
                                minimumDiameterInUnityUnits * 0.5;
                var presentationRadius = Math.Max(
                    context.Data.RadiusMeters,
                    Math.Max(angularRadius, unitFloor));

                StellarObjectPresentationUtility.ApplyTransform(
                    root,
                    context.RelativePositionMeters,
                    presentationRadius,
                    context.MetersPerUnityUnit);

                StellarObjectPresentationUtility.SetFloat(
                    materialInstance,
                    SurfaceTime,
                    (float)context.SimulationTimeSeconds);
            }

            public bool IsDistantPointReplacementReady(
                float requiredAngularDiameterDegrees) => false;

            public void Dispose()
            {
                StellarObjectPresentationUtility.DestroyObject(materialInstance);
                StellarObjectPresentationUtility.DestroyObject(root?.gameObject);
            }
        }
    }
}
