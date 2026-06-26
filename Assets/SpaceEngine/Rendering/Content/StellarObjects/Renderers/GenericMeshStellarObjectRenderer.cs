using SpaceEngine.Runtime.Content.StellarObjects.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Rendering.Content.StellarObjects.Renderers
{
    [CreateAssetMenu(
        fileName = "Generic Mesh Stellar Object Renderer",
        menuName = "Space Engine/Stellar Objects/Other/Rendering/Generic Mesh")]
    public sealed class GenericMeshStellarObjectRenderer
        : StellarObjectRenderer
    {
        [SerializeField] private Mesh mesh;
        [SerializeField] private Color color = Color.white;
        [SerializeField, Min(0.0f)]
        private float minimumAngularDiameterDegrees;
public override IStellarObjectVisual CreateVisual(
            in StellarObjectRenderContext context)
        {
            return new GenericVisual(
                context,
                mesh,
                color,
                minimumAngularDiameterDegrees);
        }

        private sealed class GenericVisual : IStellarObjectVisual
        {
            private readonly Transform root;
            private readonly Material materialInstance;
            private readonly float minimumAngularDiameterDegrees;

            public GenericVisual(
                in StellarObjectRenderContext context,
                Mesh mesh,
                Color color,
                float minimumAngularDiameterDegrees)
            {
                root = StellarObjectPresentationUtility.CreateRoot(context, 
                    $"Object {context.ObjectIndex}");
                this.minimumAngularDiameterDegrees =
                    Mathf.Max(0.0f, minimumAngularDiameterDegrees);

                root.gameObject.AddComponent<MeshFilter>().sharedMesh =
                    mesh != null
                        ? mesh
                        : StellarObjectPresentationUtility.GetSphereMesh();

                var renderer = root.gameObject.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                materialInstance =
                    StellarObjectPresentationUtility.CreateGenericSurfaceMaterial(
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
                var radius = System.Math.Max(
                    context.Data.RadiusMeters,
                    angularRadius);

                StellarObjectPresentationUtility.ApplyTransform(
                    root,
                    context.RelativePositionMeters,
                    radius,
                    context.MetersPerUnityUnit);
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
