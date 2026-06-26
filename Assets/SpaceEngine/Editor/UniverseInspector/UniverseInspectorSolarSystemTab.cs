using System;
using SpaceEngine.Runtime.Content;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.SolarSystem;
using UnityEditor;
using UnityEngine;

namespace SpaceEngine.Editor.UniverseInspector
{
    /// <summary>
    /// Generic system view. Index zero is displayed as the central object only
    /// because it is the solar-system data convention, never because it is
    /// assumed to be a star.
    /// </summary>
    public sealed class UniverseInspectorSolarSystemTab : IUniverseInspectorTab
    {
        private const double AstronomicalUnitMeters = 149_597_870_700.0;
        private const float MinimumZoom = 2.0f;
        private const float MaximumZoom = 900.0f;

        private SolarSystemData solarSystem;
        private string generationError;
        private Action<long> selectStellarObject;

        private float zoom = 34.0f;
        private Vector2 panOffset;
        private bool shouldFrameSystem;
        private bool isPanning;
        private Vector2 previousMousePosition;

        public void SetStellarObjectSelectionCallback(Action<long> callback)
        {
            selectStellarObject = callback;
        }

        public void Generate(
            SpaceEngineConfiguration configuration,
            CoordinatesData coordinates)
        {
            if (!UniverseInspectorGeneration.TryGenerateSolarSystem(
                    configuration,
                    coordinates,
                    out solarSystem,
                    out generationError))
            {
                solarSystem = null;
                return;
            }

            panOffset = Vector2.zero;
            shouldFrameSystem = true;
        }

        public void DrawInspector(
            SpaceEngineConfiguration configuration,
            CoordinatesData coordinates)
        {
            GUILayout.Label("Solar System", EditorStyles.boldLabel);

            if (solarSystem == null)
            {
                EditorGUILayout.HelpBox(
                    generationError ?? "Solar-system data has not been generated yet.",
                    MessageType.Error);
                return;
            }

            var objects = solarSystem.StellarObjects ?? Array.Empty<StellarObjectData>();
            EditorGUILayout.LabelField("Solar System ID", coordinates.SolarSystemID.ToString());
            EditorGUILayout.LabelField("Seed", solarSystem.Seed.ToString());
            EditorGUILayout.LabelField("Stellar Objects", objects.Length.ToString());

            if (objects.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No configured SolarSystemGenerator produced any objects for this address.",
                    MessageType.Warning);
                return;
            }

            GUILayout.Space(8.0f);
            GUILayout.Label("Generated Objects", EditorStyles.boldLabel);

            for (var index = 0; index < objects.Length; index++)
            {
                var body = objects[index];
                if (body == null)
                    continue;

                GUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(
                    index == 0 ? "Central Object · Index 0" : $"Orbiting Object · Index {index}",
                    UniverseInspectorGeneration.GetDataName(body));
                EditorGUILayout.ObjectField(
                    "Entity",
                    body.Entity,
                    typeof(SpaceEngine.Runtime.Content.Entities.StellarEntity),
                    false);
                EditorGUILayout.LabelField("Mass", $"{body.MassKg:E3} kg");
                EditorGUILayout.LabelField("Radius", $"{body.RadiusMeters:E3} m");
                EditorGUILayout.LabelField("Luminosity", $"{body.LuminosityWatts:E3} W");

                if (index > 0 && body.Orbit.SemiMajorAxisMeters > 0.0)
                {
                    EditorGUILayout.LabelField(
                        "Orbit Radius",
                        $"{body.Orbit.SemiMajorAxisMeters / AstronomicalUnitMeters:F3} AU");
                    EditorGUILayout.LabelField(
                        "Eccentricity",
                        body.Orbit.Eccentricity.ToString("F3"));
                }
                else if (index == 0)
                {
                    EditorGUILayout.LabelField("Orbit", "System barycenter");
                }

                if (GUILayout.Button("Inspect Object"))
                    selectStellarObject?.Invoke(index);

                GUILayout.EndVertical();
            }

            GUILayout.Space(8.0f);
            if (GUILayout.Button("Frame System"))
            {
                panOffset = Vector2.zero;
                shouldFrameSystem = true;
            }

            EditorGUILayout.HelpBox(
                "Each generated object carries the StellarEntity selected next to its generator. " +
                "The inspector uses that entity for labels and debug colours without classifying stars or planets.",
                MessageType.Info);
        }

        public void DrawCanvas(
            Rect canvasRect,
            SpaceEngineConfiguration configuration,
            CoordinatesData coordinates)
        {
            EditorGUI.DrawRect(canvasRect, new Color(0.01f, 0.014f, 0.03f));

            if (solarSystem == null)
            {
                DrawCenteredLabel(canvasRect, generationError ?? "Solar-system data has not been generated yet.");
                return;
            }

            var objects = solarSystem.StellarObjects ?? Array.Empty<StellarObjectData>();
            if (objects.Length == 0)
            {
                DrawCenteredLabel(canvasRect, "No stellar objects were generated for this system.");
                return;
            }

            if (shouldFrameSystem)
            {
                FrameSystem(canvasRect, objects);
                shouldFrameSystem = false;
            }

            var center = canvasRect.center + panOffset;
            DrawOrbits(center, objects);
            DrawObjects(center, objects);
            DrawHeader(canvasRect, objects.Length);
            HandleInput(canvasRect, objects);
        }

        private void FrameSystem(Rect canvasRect, StellarObjectData[] objects)
        {
            var outerOrbitAu = 1.0;
            for (var index = 1; index < objects.Length; index++)
            {
                if (objects[index] == null)
                    continue;

                outerOrbitAu = Math.Max(
                    outerOrbitAu,
                    objects[index].Orbit.SemiMajorAxisMeters / AstronomicalUnitMeters);
            }

            zoom = Mathf.Clamp(
                Mathf.Min(canvasRect.width, canvasRect.height) * 0.38f /
                Mathf.Max(1.0f, (float)outerOrbitAu),
                MinimumZoom,
                MaximumZoom);
            panOffset = Vector2.zero;
        }

        private void DrawOrbits(Vector2 center, StellarObjectData[] objects)
        {
            Handles.BeginGUI();
            var oldColor = Handles.color;
            Handles.color = new Color(0.26f, 0.32f, 0.47f, 0.60f);

            for (var index = 1; index < objects.Length; index++)
            {
                var body = objects[index];
                if (body == null || body.Orbit.SemiMajorAxisMeters <= 0.0)
                    continue;

                var semiMajor = (float)(body.Orbit.SemiMajorAxisMeters / AstronomicalUnitMeters) * zoom;
                var semiMinor = semiMajor * Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - (float)(body.Orbit.Eccentricity * body.Orbit.Eccentricity)));
                DrawEllipse(center, semiMajor, semiMinor);
            }

            Handles.color = oldColor;
            Handles.EndGUI();
        }

        private static void DrawEllipse(Vector2 center, float radiusX, float radiusY)
        {
            const int SegmentCount = 64;
            var points = new Vector3[SegmentCount + 1];

            for (var index = 0; index <= SegmentCount; index++)
            {
                var angle = Mathf.PI * 2.0f * index / SegmentCount;
                points[index] = new Vector3(
                    center.x + Mathf.Cos(angle) * radiusX,
                    center.y + Mathf.Sin(angle) * radiusY,
                    0.0f);
            }

            Handles.DrawPolyLine(points);
        }

        private void DrawObjects(Vector2 center, StellarObjectData[] objects)
        {
            for (var index = 0; index < objects.Length; index++)
            {
                var body = objects[index];
                if (body == null)
                    continue;

                var position = GetObjectPosition(center, body, index);
                var diameter = index == 0 ? 18.0f : 9.0f;
                var color = GetObjectColor(body, index);

                EditorGUI.DrawRect(
                    new Rect(
                        position.x - diameter * 0.5f,
                        position.y - diameter * 0.5f,
                        diameter,
                        diameter),
                    color);

                var label = index == 0
                    ? $"0 · {UniverseInspectorGeneration.GetDataName(body)}"
                    : $"{index} · {UniverseInspectorGeneration.GetDataName(body)}";

                GUI.Label(
                    new Rect(position.x + diameter * 0.5f + 3.0f, position.y - 10.0f, 220.0f, 20.0f),
                    label,
                    EditorStyles.whiteMiniLabel);
            }
        }

        private void DrawHeader(Rect canvasRect, int count)
        {
            GUI.Label(
                new Rect(canvasRect.xMin + 12.0f, canvasRect.yMin + 10.0f, 470.0f, 22.0f),
                $"System map · {count} generated objects · scroll to zoom · click an object to inspect it",
                EditorStyles.whiteMiniLabel);
        }

        private void HandleInput(Rect canvasRect, StellarObjectData[] objects)
        {
            var currentEvent = Event.current;
            if (!canvasRect.Contains(currentEvent.mousePosition))
                return;

            if (currentEvent.type == EventType.ScrollWheel)
            {
                var oldZoom = zoom;
                zoom = Mathf.Clamp(
                    zoom * (currentEvent.delta.y > 0.0f ? 0.84f : 1.19f),
                    MinimumZoom,
                    MaximumZoom);

                var mouseDelta = currentEvent.mousePosition - canvasRect.center - panOffset;
                panOffset += mouseDelta * (1.0f - zoom / oldZoom);
                EditorWindow.focusedWindow?.Repaint();
                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
            {
                var center = canvasRect.center + panOffset;
                for (var index = objects.Length - 1; index >= 0; index--)
                {
                    if (objects[index] == null)
                        continue;

                    var position = GetObjectPosition(center, objects[index], index);
                    if ((position - currentEvent.mousePosition).sqrMagnitude > 196.0f)
                        continue;

                    selectStellarObject?.Invoke(index);
                    currentEvent.Use();
                    return;
                }
            }

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 2)
            {
                isPanning = true;
                previousMousePosition = currentEvent.mousePosition;
                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseDrag && isPanning)
            {
                panOffset += currentEvent.mousePosition - previousMousePosition;
                previousMousePosition = currentEvent.mousePosition;
                EditorWindow.focusedWindow?.Repaint();
                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseUp && currentEvent.button == 2)
            {
                isPanning = false;
                currentEvent.Use();
            }
        }

        private Vector2 GetObjectPosition(
            Vector2 center,
            StellarObjectData body,
            int index)
        {
            if (index == 0 || body.Orbit.SemiMajorAxisMeters <= 0.0)
                return center;

            var radius = (float)(body.Orbit.SemiMajorAxisMeters / AstronomicalUnitMeters) * zoom;
            var angle = (float)body.Orbit.MeanAnomalyAtEpochRadians;
            return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        private static Color GetObjectColor(StellarObjectData body, int index)
        {
            var color = UniverseInspectorStellarObjectTab.GetStableDataColor(body);

            // Index only changes emphasis on the map. It does not classify the
            // object or prescribe its visual presentation.
            color.a = index == 0 ? 1.0f : 0.88f;
            return color;
        }

        private static void DrawCenteredLabel(Rect canvasRect, string text)
        {
            GUI.Label(canvasRect, text, EditorStyles.centeredGreyMiniLabel);
        }
    }
}
