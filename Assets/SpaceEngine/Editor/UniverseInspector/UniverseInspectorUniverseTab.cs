using System;
using System.Collections.Generic;
using SpaceEngine.Runtime.Content;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Universe;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace SpaceEngine.Editor.UniverseInspector
{
    /// <summary>
    /// Editor-only map of generated universe sectors. The engine owns the
    /// fixed universe map while GalaxyGenerator assets own galaxy content.
    /// </summary>
    public sealed class UniverseInspectorUniverseTab : IUniverseInspectorTab
    {
        private const int SECTOR_RADIUS = 2;
        private const float MINIMUM_ZOOM = 0.00000001f;
        private const float MAXIMUM_ZOOM = 0.25f;

        private readonly Action<long> selectGalaxy;
        private readonly List<GalaxyLocationData> galaxies = new();

        private int3 centerSectorCoordinates;
        private int viewYSector;
        private bool hasGenerated;
        private string generationError;
        private bool shouldFrameUniverse = true;

        private float zoom = 0.000025f;
        private Vector2 panOffset;
        private bool isPanning;
        private Vector2 previousMousePosition;

        public UniverseInspectorUniverseTab(Action<long> selectGalaxy)
        {
            this.selectGalaxy = selectGalaxy;
        }

        public void Generate(
            SpaceEngineConfiguration configuration,
            CoordinatesData coordinates)
        {
            centerSectorCoordinates = int3.zero;
            viewYSector = 0;
            panOffset = Vector2.zero;
            shouldFrameUniverse = true;
            hasGenerated = true;
            ReloadVisibleSectors(configuration, coordinates.UniverseID);
        }

        public void DrawInspector(
            SpaceEngineConfiguration configuration,
            CoordinatesData coordinates)
        {
            GUILayout.Label("Universe", EditorStyles.boldLabel);

            if (!hasGenerated)
            {
                EditorGUILayout.HelpBox(
                    "Universe data has not been generated yet.",
                    MessageType.None);
                return;
            }

            if (!string.IsNullOrEmpty(generationError))
                EditorGUILayout.HelpBox(generationError, MessageType.Error);

            EditorGUILayout.LabelField("Universe ID", coordinates.UniverseID.ToString());
            EditorGUILayout.LabelField(
                "Universe Map",
                "Built into SpaceEngine");
            EditorGUILayout.LabelField(
                "Current Sector",
                $"({centerSectorCoordinates.x}, {centerSectorCoordinates.y}, {centerSectorCoordinates.z})");
            EditorGUILayout.LabelField("Loaded Sector Cube", "5 × 5 × 5");
            EditorGUILayout.LabelField("Generated Galaxies", galaxies.Count.ToString());
            EditorGUILayout.LabelField(
                "Galaxy Generator Assets",
                configuration.GalaxyGenerators.Count.ToString());

            EditorGUI.BeginChangeCheck();
            viewYSector = EditorGUILayout.IntField("Y Sector Layer", viewYSector);
            if (EditorGUI.EndChangeCheck())
            {
                centerSectorCoordinates.y = viewYSector;
                ReloadVisibleSectors(configuration, coordinates.UniverseID);
            }

            GUILayout.Space(8.0f);
            if (GUILayout.Button("Center Universe"))
            {
                centerSectorCoordinates = int3.zero;
                viewYSector = 0;
                panOffset = Vector2.zero;
                shouldFrameUniverse = true;
                ReloadVisibleSectors(configuration, coordinates.UniverseID);
            }

            if (GUILayout.Button("Frame Current Area"))
            {
                panOffset = Vector2.zero;
                shouldFrameUniverse = true;
            }

            GUILayout.Space(8.0f);
            EditorGUILayout.HelpBox(
                "Every point is a real GalaxyLocationData produced by the engine universe map. " +
                "Click a point to inspect that galaxy.",
                MessageType.Info);
        }

        public void DrawCanvas(
            Rect canvasRect,
            SpaceEngineConfiguration configuration,
            CoordinatesData coordinates)
        {
            EditorGUI.DrawRect(canvasRect, new Color(0.012f, 0.016f, 0.03f));

            if (!hasGenerated)
            {
                DrawCenteredLabel(canvasRect, "Universe data has not been generated yet.");
                return;
            }

            UpdateSectorsForCurrentView(configuration, coordinates.UniverseID, canvasRect);

            if (shouldFrameUniverse)
            {
                FrameCurrentArea(canvasRect);
                shouldFrameUniverse = false;
            }

            DrawSectorGrid(canvasRect);
            DrawGalaxies(canvasRect, coordinates.GalaxyID);
            DrawHeader(canvasRect, configuration);
            HandleInput(canvasRect, configuration, coordinates.UniverseID);
        }

        private void ReloadVisibleSectors(
            SpaceEngineConfiguration configuration,
            long universeID)
        {
            galaxies.Clear();
            generationError = null;

            for (var y = -SECTOR_RADIUS; y <= SECTOR_RADIUS; y++)
            {
                for (var z = -SECTOR_RADIUS; z <= SECTOR_RADIUS; z++)
                {
                    for (var x = -SECTOR_RADIUS; x <= SECTOR_RADIUS; x++)
                    {
                        var sectorCoordinates = centerSectorCoordinates + new int3(x, y, z);

                        if (!UniverseInspectorGeneration.TryGenerateUniverseSector(
                                configuration,
                                universeID,
                                sectorCoordinates,
                                out var sector,
                                out generationError))
                        {
                            return;
                        }

                        for (var index = 0; index < sector.Galaxies.Length; index++)
                            galaxies.Add(sector.Galaxies[index]);
                    }
                }
            }
        }

        private void UpdateSectorsForCurrentView(
            SpaceEngineConfiguration configuration,
            long universeID,
            Rect canvasRect)
        {
            if (zoom <= 0.0f)
                return;

            // WorldToCanvas() uses absolute universe coordinates:
            //
            //   screen = canvasCenter + panOffset + world * zoom
            //
            // Therefore the world position under the canvas centre is derived
            // solely from panOffset. Re-basing panOffset when the streamed
            // sector changes applies the sector delta a second time and makes
            // the whole map jump out of view after crossing a sector boundary.
            var viewedPosition = new double3(
                -panOffset.x / zoom,
                0.0,
                panOffset.y / zoom);

            var newCenter = UniverseSectorUtility.GetCoordinates(viewedPosition);
            newCenter.y = viewYSector;

            if (newCenter.Equals(centerSectorCoordinates))
                return;

            // Changing the loaded sector window must not change the canvas
            // transform. The visible world remains continuous while only the
            // cached galaxy sectors are replaced around the new view centre.
            centerSectorCoordinates = newCenter;
            ReloadVisibleSectors(configuration, universeID);
        }

        private void FrameCurrentArea(Rect canvasRect)
        {
            var visibleWidth = UniverseGeneration.SectorSizeLightYears *
                               (SECTOR_RADIUS * 2 + 1);
            zoom = Mathf.Clamp(
                canvasRect.width * 0.80f / (float)visibleWidth,
                MINIMUM_ZOOM,
                MAXIMUM_ZOOM);
            panOffset = Vector2.zero;
        }

        private void DrawSectorGrid(Rect canvasRect)
        {
            var sectorSizePixels = (float)(
                UniverseGeneration.SectorSizeLightYears * zoom);

            if (sectorSizePixels < 18.0f)
                return;

            var center = canvasRect.center + panOffset;
            var lineColor = new Color(0.15f, 0.18f, 0.28f, 0.55f);

            Handles.BeginGUI();
            var oldColor = Handles.color;
            Handles.color = lineColor;

            for (var x = center.x % sectorSizePixels;
                 x < canvasRect.xMax;
                 x += sectorSizePixels)
            {
                Handles.DrawLine(new Vector3(x, canvasRect.yMin), new Vector3(x, canvasRect.yMax));
            }

            for (var y = center.y % sectorSizePixels;
                 y < canvasRect.yMax;
                 y += sectorSizePixels)
            {
                Handles.DrawLine(new Vector3(canvasRect.xMin, y), new Vector3(canvasRect.xMax, y));
            }

            Handles.color = oldColor;
            Handles.EndGUI();
        }

        private void DrawGalaxies(Rect canvasRect, long selectedGalaxyID)
        {
            for (var index = 0; index < galaxies.Count; index++)
            {
                var galaxy = galaxies[index];
                var point = WorldToCanvas(canvasRect, galaxy.UniversePositionLightYears);

                if (!canvasRect.Contains(point))
                    continue;

                var radius = Mathf.Clamp(
                    (float)(galaxy.RadiusLightYears * zoom * 0.08),
                    2.0f,
                    11.0f);
                var color = UniverseInspectorGeneration.GetStableColor(galaxy.GalaxyID);
                color.a = galaxy.GalaxyID == selectedGalaxyID ? 1.0f : 0.72f;

                EditorGUI.DrawRect(
                    new Rect(point.x - radius, point.y - radius, radius * 2.0f, radius * 2.0f),
                    color);

                if (galaxy.GalaxyID == selectedGalaxyID)
                {
                    GUI.Label(
                        new Rect(point.x + radius + 3.0f, point.y - 10.0f, 180.0f, 20.0f),
                        $"Selected · Galaxy {galaxy.GalaxyID}",
                        EditorStyles.whiteMiniLabel);
                }
            }
        }

        private void DrawHeader(Rect canvasRect, SpaceEngineConfiguration configuration)
        {
            GUI.Label(
                new Rect(canvasRect.xMin + 12.0f, canvasRect.yMin + 10.0f, 420.0f, 22.0f),
                "Universe map · engine hierarchy · scroll to zoom · drag with middle mouse",
                EditorStyles.whiteMiniLabel);
        }

        private void HandleInput(
            Rect canvasRect,
            SpaceEngineConfiguration configuration,
            long universeID)
        {
            var currentEvent = Event.current;
            if (!canvasRect.Contains(currentEvent.mousePosition))
                return;

            if (currentEvent.type == EventType.ScrollWheel)
            {
                var previousZoom = zoom;
                zoom = Mathf.Clamp(
                    zoom * (currentEvent.delta.y > 0.0f ? 0.82f : 1.22f),
                    MINIMUM_ZOOM,
                    MAXIMUM_ZOOM);

                if (Math.Abs(previousZoom - zoom) > 0.00000000001f)
                {
                    var mouseDelta = currentEvent.mousePosition - canvasRect.center - panOffset;
                    panOffset += mouseDelta * (1.0f - zoom / previousZoom);
                    RepaintCurrentWindow();
                }

                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
            {
                for (var index = galaxies.Count - 1; index >= 0; index--)
                {
                    var point = WorldToCanvas(canvasRect, galaxies[index].UniversePositionLightYears);
                    if ((point - currentEvent.mousePosition).sqrMagnitude > 144.0f)
                        continue;

                    selectGalaxy?.Invoke(galaxies[index].GalaxyID);
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
                RepaintCurrentWindow();
                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseUp && currentEvent.button == 2)
            {
                isPanning = false;
                currentEvent.Use();
            }
        }

        private Vector2 WorldToCanvas(Rect canvasRect, double3 universePosition)
        {
            return canvasRect.center + panOffset + new Vector2(
                (float)(universePosition.x * zoom),
                (float)(-universePosition.z * zoom));
        }

        private static void RepaintCurrentWindow()
        {
            EditorWindow.focusedWindow?.Repaint();
        }

        private static void DrawCenteredLabel(Rect canvasRect, string text)
        {
            GUI.Label(canvasRect, text, EditorStyles.centeredGreyMiniLabel);
        }
    }
}
