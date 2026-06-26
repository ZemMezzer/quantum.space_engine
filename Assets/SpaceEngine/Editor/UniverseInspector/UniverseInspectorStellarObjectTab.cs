using System;
using System.Globalization;
using System.Reflection;
using SpaceEngine.Runtime.Content;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.SolarSystem;
using UnityEditor;
using UnityEngine;

namespace SpaceEngine.Editor.UniverseInspector
{
    /// <summary>
    /// Generic inspection for one generated object. The window knows only the
    /// common physical contract; public properties declared by the concrete
    /// data type are discovered reflectively, so new data classes need no
    /// Editor changes.
    /// </summary>
    public sealed class UniverseInspectorStellarObjectTab : IUniverseInspectorTab
    {
        private const double AstronomicalUnitMeters = 149_597_870_700.0;

        private long objectIndex;
        private SolarSystemData solarSystem;
        private StellarObjectData stellarObject;
        private string generationError;

        public void SetObjectIndex(long index)
        {
            objectIndex = Math.Max(0L, index);
        }

        public void Generate(
            SpaceEngineConfiguration configuration,
            CoordinatesData coordinates)
        {
            stellarObject = null;

            if (!UniverseInspectorGeneration.TryGenerateSolarSystem(
                    configuration,
                    coordinates,
                    out solarSystem,
                    out generationError))
            {
                solarSystem = null;
                return;
            }

            var objects = solarSystem.StellarObjects ??
                          Array.Empty<StellarObjectData>();
            if (objectIndex >= objects.Length)
            {
                generationError =
                    $"Object index {objectIndex} is outside this system's generated range 0–{Math.Max(0, objects.Length - 1)}.";
                return;
            }

            stellarObject = objects[objectIndex];
            if (stellarObject == null)
                generationError = $"Generated object {objectIndex} is null.";
        }

        public void DrawInspector(
            SpaceEngineConfiguration configuration,
            CoordinatesData coordinates)
        {
            GUILayout.Label("Stellar Object", EditorStyles.boldLabel);

            if (stellarObject == null)
            {
                EditorGUILayout.HelpBox(
                    generationError ?? "Object data has not been generated yet.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Object Index", objectIndex.ToString());
            EditorGUILayout.LabelField(
                "System Role",
                objectIndex == 0
                    ? "Primary object by index convention"
                    : "Additional system object");
            EditorGUILayout.ObjectField(
                "Entity",
                stellarObject.Entity,
                typeof(SpaceEngine.Runtime.Content.Entities.StellarEntity),
                false);
            EditorGUILayout.LabelField(
                "Type",
                UniverseInspectorGeneration.GetDataName(stellarObject));
            EditorGUILayout.LabelField("Mass", $"{stellarObject.MassKg:E6} kg");
            EditorGUILayout.LabelField("Radius", $"{stellarObject.RadiusMeters:E6} m");
            EditorGUILayout.LabelField(
                "Luminosity",
                $"{stellarObject.LuminosityWatts:E6} W");

            DrawConcreteData(stellarObject);
            DrawOrbit(stellarObject);

            GUILayout.Space(8.0f);
            EditorGUILayout.HelpBox(
                "Common fields come from StellarObjectData. The concrete data " +
                "class exposes its own public properties automatically, so a new " +
                "generator/data type does not require an Editor change.",
                MessageType.Info);
        }

        public void DrawCanvas(
            Rect canvasRect,
            SpaceEngineConfiguration configuration,
            CoordinatesData coordinates)
        {
            EditorGUI.DrawRect(canvasRect, new Color(0.012f, 0.016f, 0.03f));

            if (stellarObject == null)
            {
                GUI.Label(
                    canvasRect,
                    generationError ?? "Object data has not been generated yet.",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var center = canvasRect.center;
            var color = GetStableDataColor(stellarObject);
            var radius = Mathf.Clamp(
                Mathf.Log10(Mathf.Max(1.0f, (float)stellarObject.RadiusMeters)) *
                4.0f,
                24.0f,
                170.0f);

            Handles.BeginGUI();
            var oldColor = Handles.color;
            color.a = 0.17f;
            Handles.color = color;
            Handles.DrawSolidDisc(center, Vector3.forward, radius * 1.5f);
            color.a = 1.0f;
            Handles.color = color;
            Handles.DrawSolidDisc(center, Vector3.forward, radius);
            Handles.color = oldColor;
            Handles.EndGUI();

            GUI.Label(
                new Rect(
                    center.x - 220.0f,
                    center.y + radius + 16.0f,
                    440.0f,
                    24.0f),
                $"Index {objectIndex} · {UniverseInspectorGeneration.GetDataName(stellarObject)}",
                EditorStyles.centeredGreyMiniLabel);
            GUI.Label(
                new Rect(
                    canvasRect.xMin + 12.0f,
                    canvasRect.yMin + 10.0f,
                    560.0f,
                    22.0f),
                "Generic object preview · colour comes from the generated StellarEntity",
                EditorStyles.whiteMiniLabel);
        }

        private static void DrawConcreteData(StellarObjectData data)
        {
            var properties = data.GetType().GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (properties.Length == 0)
                return;

            GUILayout.Space(8.0f);
            GUILayout.Label("Concrete Data", EditorStyles.boldLabel);

            foreach (var property in properties)
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0)
                    continue;

                var value = property.GetValue(data);
                EditorGUILayout.LabelField(
                    ObjectNames.NicifyVariableName(property.Name),
                    FormatValue(value));
            }
        }

        private void DrawOrbit(StellarObjectData data)
        {
            GUILayout.Space(8.0f);
            GUILayout.Label("Orbit", EditorStyles.boldLabel);

            if (data.Orbit.SemiMajorAxisMeters <= 0.0)
            {
                EditorGUILayout.HelpBox(
                    "This object is at the generated system barycenter.",
                    MessageType.None);
                return;
            }

            EditorGUILayout.LabelField(
                "Semi-major Axis",
                $"{data.Orbit.SemiMajorAxisMeters / AstronomicalUnitMeters:F6} AU");
            EditorGUILayout.LabelField(
                "Eccentricity",
                data.Orbit.Eccentricity.ToString("F6", CultureInfo.InvariantCulture));
            EditorGUILayout.LabelField(
                "Inclination",
                $"{data.Orbit.InclinationRadians * Mathf.Rad2Deg:F3}°");
            EditorGUILayout.LabelField(
                "Argument of Periapsis",
                $"{data.Orbit.ArgumentOfPeriapsisRadians * Mathf.Rad2Deg:F3}°");
            EditorGUILayout.LabelField(
                "Ascending Node",
                $"{data.Orbit.LongitudeOfAscendingNodeRadians * Mathf.Rad2Deg:F3}°");
            EditorGUILayout.LabelField(
                "Mean Anomaly at Epoch",
                $"{data.Orbit.MeanAnomalyAtEpochRadians * Mathf.Rad2Deg:F3}°");
        }

        internal static Color GetStableDataColor(StellarObjectData data)
        {
            return UniverseInspectorGeneration.GetDebugColor(
                data?.Entity,
                data == null ? 0L : data.GetType().FullName.GetHashCode());
        }

        private static string FormatValue(object value)
        {
            return value switch
            {
                null => "None",
                double number => number.ToString("G6", CultureInfo.InvariantCulture),
                float number => number.ToString("G6", CultureInfo.InvariantCulture),
                _ => value.ToString()
            };
        }
    }
}
