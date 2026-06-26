using System;
using System.Collections.Generic;
using SpaceEngine.Runtime.Content;
using SpaceEngine.Runtime.Content.StellarObjects.Generation.Galaxies;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Galaxy;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace SpaceEngine.Editor.UniverseInspector
{
    public sealed class UniverseInspectorGalaxyTab : IUniverseInspectorTab
    {
        private const int MAXIMUM_VISIBLE_SAMPLES = 4_000;
        private const int SECTOR_RADIUS = 2;
        private const float SYSTEM_LAYER_MINIMUM_PIXELS_PER_LIGHT_YEAR = 0.75f;
        private const float MINIMUM_ZOOM = 0.000001f;
        private const float MAXIMUM_ZOOM = 100.0f;

        private readonly List<SolarSystemLocationData> visibleSolarSystems = new();
        private Action<long> selectSolarSystem;

        private GalaxyData galaxy;
        private GalaxyGenerator galaxyGenerator;
        private bool hasGeneratedGalaxy;
        private string generationError;

        private Vector2[] samplePositions = Array.Empty<Vector2>();
        private float[] sampleBrightness = Array.Empty<float>();

        private int3 centerSystemSector;
        private bool hasLoadedSystemSectors;

        private float zoom = 1.0f;
        private Vector2 panOffset;
        private bool isPanning;
        private Vector2 previousMousePosition;
        private bool shouldFrameGalaxy;

        public void SetSolarSystemSelectionCallback(Action<long> callback)
        {
            selectSolarSystem = callback;
        }

        public void Generate(
            SpaceEngineConfiguration configuration,
            CoordinatesData coordinates)
        {
            hasGeneratedGalaxy = UniverseInspectorGeneration.TryGenerateGalaxy(
                configuration,
                coordinates.UniverseID,
                coordinates.GalaxyID,
                out galaxy,
                out galaxyGenerator,
                out generationError);

            visibleSolarSystems.Clear();
            centerSystemSector = int3.zero;
            hasLoadedSystemSectors = false;
            panOffset = Vector2.zero;
            shouldFrameGalaxy = true;

            if (hasGeneratedGalaxy)
                GenerateVisualSample();
            else
            {
                samplePositions = Array.Empty<Vector2>();
                sampleBrightness = Array.Empty<float>();
            }
        }

        public void DrawInspector(
            SpaceEngineConfiguration configuration,
            CoordinatesData coordinates)
        {
            GUILayout.Label("Galaxy", EditorStyles.boldLabel);

            if (!hasGeneratedGalaxy || galaxy == null)
            {
                EditorGUILayout.HelpBox(
                    generationError ?? "Galaxy data has not been generated yet.",
                    MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("Galaxy ID", galaxy.GalaxyID.ToString());
            EditorGUILayout.LabelField("Seed", galaxy.Seed.ToString());
            EditorGUILayout.ObjectField(
                "Entity",
                galaxy.Entity,
                typeof(SpaceEngine.Runtime.Content.Entities.StellarEntity),
                false);
            EditorGUILayout.ObjectField(
                "Selected Generator",
                galaxyGenerator,
                typeof(GalaxyGenerator),
                false);
            EditorGUILayout.LabelField("Type", UniverseInspectorGeneration.GetDataName(galaxy));
            EditorGUILayout.LabelField("Radius", $"{galaxy.RadiusLightYears:F0} ly");
            EditorGUILayout.LabelField("Core Radius", $"{galaxy.CoreRadiusLightYears:F0} ly");
            EditorGUILayout.LabelField("Disk Thickness", $"{galaxy.DiskThicknessLightYears:F0} ly");
            EditorGUILayout.LabelField("Mass", $"{galaxy.MassKg:E3} kg");
            EditorGUILayout.LabelField(
                "Base System Density",
                $"{galaxy.BaseSystemDensityPerCubicLightYear:E3} systems / ly³");
            EditorGUILayout.LabelField("Metallicity", galaxy.Metallicity.ToString("F4"));
            EditorGUILayout.LabelField("Rotation", $"{galaxy.RotationRadians * Mathf.Rad2Deg:F1}°");

            GUILayout.Space(8.0f);
            if (GUILayout.Button("Frame Galaxy"))
            {
                panOffset = Vector2.zero;
                shouldFrameGalaxy = true;
            }

            GUILayout.Space(8.0f);
            if (IsSystemLayerActive())
            {
                EditorGUILayout.LabelField("System Layer", "Active");
                EditorGUILayout.LabelField(
                    "Center Sector",
                    $"({centerSystemSector.x}, {centerSystemSector.y}, {centerSystemSector.z})");
                EditorGUILayout.LabelField("Visible Systems", visibleSolarSystems.Count.ToString());
                EditorGUILayout.HelpBox(
                    "Displayed points are SolarSystemLocationData produced by this GalaxyGenerator. " +
                    "Click a point to inspect the corresponding system.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"Broad generator sample: {samplePositions.Length} deterministic locations. " +
                    $"Zoom to {SYSTEM_LAYER_MINIMUM_PIXELS_PER_LIGHT_YEAR:F2} pixels per light-year to load real systems.",
                    MessageType.None);
            }
        }

        public void DrawCanvas(
            Rect canvasRect,
            SpaceEngineConfiguration configuration,
            CoordinatesData coordinates)
        {
            EditorGUI.DrawRect(canvasRect, new Color(0.015f, 0.018f, 0.035f));

            if (!hasGeneratedGalaxy || galaxy == null || galaxyGenerator == null)
            {
                DrawCenteredLabel(canvasRect, generationError ?? "Galaxy data has not been generated yet.");
                return;
            }

            if (shouldFrameGalaxy)
            {
                FrameGalaxy(canvasRect);
                shouldFrameGalaxy = false;
            }

            UpdateSystemSectorsForCurrentView();
            DrawGalaxyBounds(canvasRect);
            DrawBroadSample(canvasRect);

            if (IsSystemLayerActive())
                DrawSolarSystems(canvasRect);

            DrawHeader(canvasRect);
            HandleInput(canvasRect);
        }

        private void GenerateVisualSample()
        {
            var positions = new Vector2[MAXIMUM_VISIBLE_SAMPLES];
            var brightness = new float[MAXIMUM_VISIBLE_SAMPLES];
            var brightnessRandom = new System.Random(
                unchecked((int)(galaxy.Seed ^ (galaxy.Seed >> 32))));
            var radius = Math.Max(1.0, galaxy.RadiusLightYears);
            
            for (var sampleIndex = 0;
                 sampleIndex < MAXIMUM_VISIBLE_SAMPLES;
                 sampleIndex++)
            {
                var location = galaxyGenerator.GenerateSolarSystemLocation(
                    galaxy,
                    sampleIndex + 1L);

                positions[sampleIndex] = new Vector2(
                    (float)location.GalaxyLocalPositionLightYears.x,
                    (float)location.GalaxyLocalPositionLightYears.z);

                var normalizedRadius = Math.Min(
                    1.0,
                    math.length(new double2(
                        location.GalaxyLocalPositionLightYears.x,
                        location.GalaxyLocalPositionLightYears.z)) /
                    radius);

                var radialBrightness = Mathf.Lerp(
                    0.92f,
                    0.30f,
                    (float)normalizedRadius);

                brightness[sampleIndex] = radialBrightness * Mathf.Lerp(
                    0.70f,
                    1.00f,
                    (float)brightnessRandom.NextDouble());
            }

            samplePositions = positions;
            sampleBrightness = brightness;
        }

        private void UpdateSystemSectorsForCurrentView()
        {
            if (!IsSystemLayerActive())
            {
                visibleSolarSystems.Clear();
                hasLoadedSystemSectors = false;
                return;
            }

            var viewedPosition = new double3(
                -panOffset.x / zoom,
                0.0,
                panOffset.y / zoom);
            var newCenterSector = GalaxySectorUtility.GetCoordinates(viewedPosition);

            if (hasLoadedSystemSectors && newCenterSector.Equals(centerSystemSector))
                return;

            centerSystemSector = newCenterSector;
            hasLoadedSystemSectors = true;
            visibleSolarSystems.Clear();
            generationError = null;

            for (var y = -SECTOR_RADIUS; y <= SECTOR_RADIUS; y++)
            {
                for (var z = -SECTOR_RADIUS; z <= SECTOR_RADIUS; z++)
                {
                    for (var x = -SECTOR_RADIUS; x <= SECTOR_RADIUS; x++)
                    {
                        var coordinates = centerSystemSector + new int3(x, y, z);

                        if (!UniverseInspectorGeneration.TryGenerateGalaxySector(
                                galaxyGenerator,
                                galaxy,
                                coordinates,
                                out var sector,
                                out generationError))
                        {
                            return;
                        }

                        for (var index = 0; index < sector.SolarSystems.Length; index++)
                            visibleSolarSystems.Add(sector.SolarSystems[index]);
                    }
                }
            }
        }

        private bool IsSystemLayerActive()
        {
            return zoom >= SYSTEM_LAYER_MINIMUM_PIXELS_PER_LIGHT_YEAR;
        }

        private void FrameGalaxy(Rect canvasRect)
        {
            var diameter = Math.Max(2.0, galaxy.RadiusLightYears * 2.0);
            zoom = Mathf.Clamp(
                canvasRect.width * 0.78f / (float)diameter,
                MINIMUM_ZOOM,
                MAXIMUM_ZOOM);
            panOffset = Vector2.zero;
        }

        private void DrawGalaxyBounds(Rect canvasRect)
        {
            var center = canvasRect.center + panOffset;
            var radius = Mathf.Max(8.0f, (float)(galaxy.RadiusLightYears * zoom));
            var color = UniverseInspectorGeneration.GetDebugColor(
                galaxy.Entity,
                galaxy.GalaxyID);
            color.a = 0.22f;

            Handles.BeginGUI();
            var oldColor = Handles.color;
            Handles.color = color;
            Handles.DrawWireDisc(center, Vector3.forward, radius);
            Handles.color = oldColor;
            Handles.EndGUI();
        }

        private void DrawBroadSample(Rect canvasRect)
        {
            var color = UniverseInspectorGeneration.GetDebugColor(
                galaxy.Entity,
                galaxy.GalaxyID);

            for (var index = 0; index < samplePositions.Length; index++)
            {
                var screenPoint = canvasRect.center + panOffset + samplePositions[index] * zoom;
                if (!canvasRect.Contains(screenPoint))
                    continue;

                color.a = sampleBrightness[index] * 0.70f;
                EditorGUI.DrawRect(
                    new Rect(screenPoint.x, screenPoint.y, 1.0f, 1.0f),
                    color);
            }
        }

        private void DrawSolarSystems(Rect canvasRect)
        {
            for (var index = 0; index < visibleSolarSystems.Count; index++)
            {
                var system = visibleSolarSystems[index];
                var screenPoint = WorldToCanvas(canvasRect, system.GalaxyLocalPositionLightYears);
                if (!canvasRect.Contains(screenPoint))
                    continue;

                var color = UniverseInspectorGeneration.GetStableColor(system.SolarSystemID);
                color.a = 1.0f;
                EditorGUI.DrawRect(
                    new Rect(screenPoint.x - 2.0f, screenPoint.y - 2.0f, 4.0f, 4.0f),
                    color);
            }
        }

        private void DrawHeader(Rect canvasRect)
        {
            GUI.Label(
                new Rect(canvasRect.xMin + 12.0f, canvasRect.yMin + 10.0f, 480.0f, 22.0f),
                $"Galaxy {galaxy.GalaxyID} · {galaxyGenerator.name} · scroll to zoom · drag with middle mouse",
                EditorStyles.whiteMiniLabel);
        }

        private void HandleInput(Rect canvasRect)
        {
            var currentEvent = Event.current;
            if (!canvasRect.Contains(currentEvent.mousePosition))
                return;

            if (currentEvent.type == EventType.ScrollWheel)
            {
                var oldZoom = zoom;
                zoom = Mathf.Clamp(
                    zoom * (currentEvent.delta.y > 0.0f ? 0.82f : 1.22f),
                    MINIMUM_ZOOM,
                    MAXIMUM_ZOOM);

                var mouseDelta = currentEvent.mousePosition - canvasRect.center - panOffset;
                panOffset += mouseDelta * (1.0f - zoom / oldZoom);
                EditorWindow.focusedWindow?.Repaint();
                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && IsSystemLayerActive())
            {
                for (var index = visibleSolarSystems.Count - 1; index >= 0; index--)
                {
                    var screenPoint = WorldToCanvas(
                        canvasRect,
                        visibleSolarSystems[index].GalaxyLocalPositionLightYears);

                    if ((screenPoint - currentEvent.mousePosition).sqrMagnitude > 100.0f)
                        continue;

                    selectSolarSystem?.Invoke(visibleSolarSystems[index].SolarSystemID);
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

        private Vector2 WorldToCanvas(Rect canvasRect, double3 localPosition)
        {
            return canvasRect.center + panOffset + new Vector2(
                (float)(localPosition.x * zoom),
                (float)(-localPosition.z * zoom));
        }

        private static void DrawCenteredLabel(Rect canvasRect, string text)
        {
            GUI.Label(canvasRect, text, EditorStyles.centeredGreyMiniLabel);
        }
    }
}
