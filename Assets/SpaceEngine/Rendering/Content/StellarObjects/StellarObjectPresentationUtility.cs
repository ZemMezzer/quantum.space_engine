using System;
using SpaceEngine.Runtime.Content.StellarObjects.Rendering;
using UnityEngine;

namespace SpaceEngine.Rendering.Content.StellarObjects
{
    internal static class StellarObjectPresentationUtility
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int Color1 = Shader.PropertyToID("_Color");
        private static readonly int PhotonRingColor = Shader.PropertyToID("_PhotonRingColor");
        private static readonly int PhotonRingIntensity = Shader.PropertyToID("_PhotonRingIntensity");
        private static readonly int InnerColor = Shader.PropertyToID("_InnerColor");
        private static readonly int MiddleColor = Shader.PropertyToID("_MiddleColor");
        private static readonly int OuterColor = Shader.PropertyToID("_OuterColor");
        private static readonly int DustColor = Shader.PropertyToID("_DustColor");
        private static readonly int Intensity = Shader.PropertyToID("_Intensity");

        public static Transform CreateRoot(
            in StellarObjectRenderContext context,
            string name)
        {
            var visualObject = new GameObject(
                string.IsNullOrWhiteSpace(name)
                    ? $"Stellar Object {context.ObjectIndex}"
                    : name)
            {
                layer = context.Layer
            };

            var root = visualObject.transform;
            root.SetParent(context.Parent, false);
            return root;
        }

        public static Mesh GetSphereMesh()
        {
            return Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        }

        public static Material CreateStarSurfaceMaterial(Color color)
        {
            return CreateGeneratedMaterial(
                "Star Surface Material",
                color,
                true,
                "SpaceEngine/Streaming/Star Surface",
                "Universal Render Pipeline/Unlit",
                "Standard",
                "Sprites/Default");
        }

        public static Material CreateStarCoronaMaterial(Color color)
        {
            return CreateGeneratedMaterial(
                "Star Corona Material",
                color,
                true,
                "SpaceEngine/Streaming/Star Corona",
                "Universal Render Pipeline/Unlit",
                "Unlit/Transparent",
                "Sprites/Default");
        }

        public static Material CreatePlanetSurfaceMaterial(Color color)
        {
            return CreateGeneratedMaterial(
                "Planet Surface Material",
                color,
                false,
                "Universal Render Pipeline/Lit",
                "Standard",
                "Universal Render Pipeline/Unlit",
                "Sprites/Default");
        }

        public static Material CreateGenericSurfaceMaterial(
            Color color,
            bool emissive = false)
        {
            return CreateGeneratedMaterial(
                "Generic Stellar Object Material",
                color,
                emissive,
                "Universal Render Pipeline/Lit",
                "Standard",
                "Universal Render Pipeline/Unlit",
                "Sprites/Default");
        }

        public static Material CreateBlackHoleHorizonMaterial(
            Color photonRingColor)
        {
            var material = CreateGeneratedMaterial(
                "Black Hole Horizon Material",
                Color.black,
                false,
                "SpaceEngine/Streaming/Black Hole Horizon",
                "Universal Render Pipeline/Unlit",
                "Unlit/Color",
                "Sprites/Default");

            SetColor(material, PhotonRingColor, photonRingColor);
            SetFloat(material, PhotonRingIntensity, 0.08f);
            return material;
        }

        public static Material CreateBlackHoleAccretionDiskMaterial(
            Color baseColor)
        {
            var material = CreateGeneratedMaterial(
                "Black Hole Accretion Disk Material",
                baseColor,
                true,
                "SpaceEngine/Streaming/Black Hole Accretion Disk",
                "Universal Render Pipeline/Unlit",
                "Unlit/Transparent",
                "Sprites/Default");

            SetColor(material, InnerColor, Multiply(baseColor, 2.5f));
            SetColor(material, MiddleColor, baseColor);
            SetColor(material, OuterColor, Multiply(baseColor, 0.30f));
            SetColor(material, DustColor, Multiply(baseColor, 0.55f));
            SetFloat(material, Intensity, 1.8f);
            return material;
        }

        public static void SetFloat(
            Material material,
            int propertyId,
            float value)
        {
            if (material != null && material.HasProperty(propertyId))
                material.SetFloat(propertyId, value);
        }

        public static double GetMinimumAngularRadiusMeters(
            double distanceMeters,
            float minimumAngularDiameterDegrees)
        {
            if (distanceMeters <= 0.0 ||
                minimumAngularDiameterDegrees <= 0.0f)
            {
                return 0.0;
            }

            return distanceMeters *
                   Math.Tan(minimumAngularDiameterDegrees *
                            Math.PI / 360.0);
        }

        public static float GetAngularDiameterDegrees(
            double radiusMeters,
            double distanceMeters)
        {
            if (radiusMeters <= 0.0 || distanceMeters <= 0.0)
                return 180.0f;

            if (distanceMeters <= radiusMeters)
                return 180.0f;

            var ratio = Math.Min(1.0, radiusMeters / distanceMeters);
            return (float)(2.0 * Math.Asin(ratio) * 180.0 / Math.PI);
        }

        public static void ApplyTransform(
            Transform root,
            Unity.Mathematics.double3 relativePositionMeters,
            double radiusMeters,
            double metersPerUnityUnit)
        {
            if (root == null)
                return;

            var safeScale = Math.Max(1.0, metersPerUnityUnit);
            root.localPosition = new Vector3(
                (float)(relativePositionMeters.x / safeScale),
                (float)(relativePositionMeters.y / safeScale),
                (float)(relativePositionMeters.z / safeScale));

            var diameterUnits = (float)(
                Math.Max(0.0, radiusMeters) * 2.0 / safeScale);
            root.localScale = Vector3.one * Mathf.Max(
                0.000001f,
                diameterUnits);
        }

        public static void DestroyObject(UnityEngine.Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(target);
            else
                UnityEngine.Object.DestroyImmediate(target);
        }

        private static Material CreateGeneratedMaterial(
            string materialName,
            Color color,
            bool emissive,
            params string[] shaderNames)
        {
            var shader = FindFirstAvailableShader(shaderNames);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"No supported shader is available for {materialName}.");
            }

            var material = new Material(shader)
            {
                name = materialName
            };

            SetColor(material, BaseColor, color);
            SetColor(material, Color1, color);

            if (emissive)
            {
                SetColor(material, EmissionColor, color);
                material.EnableKeyword("_EMISSION");
            }

            return material;
        }

        private static Shader FindFirstAvailableShader(
            params string[] shaderNames)
        {
            for (var index = 0; index < shaderNames.Length; index++)
            {
                var shader = Shader.Find(shaderNames[index]);
                if (shader != null)
                    return shader;
            }

            return null;
        }

        private static void SetColor(
            Material material,
            int propertyId,
            Color value)
        {
            if (material != null && material.HasProperty(propertyId))
                material.SetColor(propertyId, value);
        }

        private static Color Multiply(Color color, float multiplier)
        {
            return new Color(
                color.r * multiplier,
                color.g * multiplier,
                color.b * multiplier,
                color.a);
        }
    }
}
