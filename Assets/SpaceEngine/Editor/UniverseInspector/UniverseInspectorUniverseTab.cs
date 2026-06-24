using System;
using System.Collections.Generic;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Galaxy;
using SpaceEngine.Runtime.Generation.Universe;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace SpaceEngine.Editor.UniverseInspector
{
    /// <summary>
    /// Editor-only streamed map of the procedural universe.
    /// The whole supported universe is available for navigation, while only
    /// sectors around the current map view are generated and kept in memory.
    /// </summary>
    public sealed class UniverseInspectorUniverseTab : IUniverseInspectorTab
    {
        private const int SectorRadius = 2;

        private const float MinimumZoom = 0.00000001f;
        private const float MaximumZoom = 1f;

        private readonly Action<ulong> _selectGalaxy;
        private readonly List<GalaxyLocationData> _galaxies = new();

        private int3 _centerSectorCoordinates;
        private int _viewYSector;

        private bool _hasGenerated;
        private bool _shouldFrameUniverse = true;

        private float _zoom = 0.000025f;
        private Vector2 _panOffset;

        private bool _isPanning;
        private Vector2 _previousMousePosition;

        public UniverseInspectorUniverseTab(Action<ulong> selectGalaxy)
        {
            _selectGalaxy = selectGalaxy;
        }

        public void Generate(CoordinatesData coordinates)
        {
            // The default map camera starts at the actual center of the
            // supported procedural universe, not near the selected galaxy.
            _centerSectorCoordinates = int3.zero;
            _viewYSector = 0;

            _panOffset = Vector2.zero;
            _shouldFrameUniverse = true;
            _hasGenerated = true;

            ReloadVisibleSectors(coordinates.UniverseID);
        }

        public void DrawInspector(CoordinatesData coordinates)
        {
            GUILayout.Label(
                "Universe",
                EditorStyles.boldLabel);

            EditorGUILayout.LabelField(
                "Universe ID",
                coordinates.UniverseID.ToString());

            EditorGUILayout.LabelField(
                "Current Sector",
                $"({_centerSectorCoordinates.x}, " +
                $"{_centerSectorCoordinates.y}, " +
                $"{_centerSectorCoordinates.z})");

            EditorGUILayout.LabelField(
                "Loaded Sector Cube",
                "3 × 3 × 3");

            EditorGUILayout.LabelField(
                "Generated Galaxies",
                _galaxies.Count.ToString());

            EditorGUILayout.LabelField(
                "Supported Sector Range",
                $"{GalaxyIDUtility.MINIMUM_SECTOR_COORDINATE} to " +
                $"{GalaxyIDUtility.MAXIMUM_SECTOR_COORDINATE}");

            EditorGUI.BeginChangeCheck();

            _viewYSector = EditorGUILayout.IntField(
                "Y Sector Layer",
                _viewYSector);

            if (EditorGUI.EndChangeCheck())
            {
                _centerSectorCoordinates.y = ClampSectorCoordinate(
                    _viewYSector);

                _viewYSector = _centerSectorCoordinates.y;

                ReloadVisibleSectors(coordinates.UniverseID);
            }

            GUILayout.Space(8f);

            if (GUILayout.Button("Center Universe"))
            {
                _centerSectorCoordinates = int3.zero;
                _viewYSector = 0;
                _panOffset = Vector2.zero;
                _shouldFrameUniverse = true;

                ReloadVisibleSectors(coordinates.UniverseID);
            }

            if (GUILayout.Button("Frame Current Area"))
            {
                _panOffset = Vector2.zero;
                _shouldFrameUniverse = true;
            }

            GUILayout.Space(8f);

            EditorGUILayout.HelpBox(
                "The universe is not generated into memory at once. " +
                "Move with the middle mouse button: the inspector generates " +
                "new surrounding sectors as the view crosses sector borders.\n\n" +
                "Click a galaxy to open its Galaxy tab.",
                MessageType.Info);
        }

        public void DrawCanvas(
            Rect canvasRect,
            CoordinatesData coordinates)
        {
            EditorGUI.DrawRect(
                canvasRect,
                new Color(0.012f, 0.016f, 0.03f));

            if (!_hasGenerated)
            {
                DrawCenteredLabel(
                    canvasRect,
                    "Universe data has not been generated yet.",
                    EditorStyles.centeredGreyMiniLabel);

                return;
            }

            UpdateSectorsForCurrentView(
                canvasRect,
                coordinates.UniverseID);

            if (_shouldFrameUniverse)
            {
                FrameCurrentArea(canvasRect);
                _shouldFrameUniverse = false;
            }

            DrawSectorGrid(canvasRect);
            DrawGalaxies(canvasRect, coordinates.GalaxyID);
            DrawHeader(canvasRect);

            HandleInput(canvasRect, coordinates.UniverseID);
        }

        private void ReloadVisibleSectors(ulong universeID)
        {
            _galaxies.Clear();

            for (var y = -SectorRadius; y <= SectorRadius; y++)
            {
                for (var z = -SectorRadius; z <= SectorRadius; z++)
                {
                    for (var x = -SectorRadius; x <= SectorRadius; x++)
                    {
                        var sectorCoordinates = ClampSectorCoordinates(
                            _centerSectorCoordinates +
                            new int3(x, y, z));

                        var sector = UniverseSectorGenerator.Generate(
                            universeID,
                            sectorCoordinates);

                        for (var i = 0; i < sector.Galaxies.Length; i++)
                            _galaxies.Add(sector.Galaxies[i]);
                    }
                }
            }
        }

        private void UpdateSectorsForCurrentView(
            Rect canvasRect,
            ulong universeID)
        {
            if (_zoom <= 0.0f)
                return;

            var viewedUniversePosition = GetViewedUniversePosition(
                canvasRect);

            var newCenterSector = ClampSectorCoordinates(
                UniverseSectorUtility.GetCoordinates(
                    viewedUniversePosition));

            newCenterSector.y = ClampSectorCoordinate(
                _viewYSector);

            if (newCenterSector.Equals(_centerSectorCoordinates))
                return;

            PreserveCameraPosition(newCenterSector);

            _centerSectorCoordinates = newCenterSector;
            _viewYSector = newCenterSector.y;

            ReloadVisibleSectors(universeID);
        }

        private void PreserveCameraPosition(int3 newCenterSector)
        {
            var oldCenterPosition = GetSectorCenterPosition(
                _centerSectorCoordinates);

            var newCenterPosition = GetSectorCenterPosition(
                newCenterSector);

            var delta = newCenterPosition - oldCenterPosition;

            _panOffset += new Vector2(
                (float)(delta.x * _zoom),
                (float)(-delta.z * _zoom));
        }

        private double3 GetViewedUniversePosition(Rect canvasRect)
        {
            var centerPosition = GetSectorCenterPosition(
                _centerSectorCoordinates);

            return new double3(
                centerPosition.x - _panOffset.x / _zoom,
                centerPosition.y,
                centerPosition.z + _panOffset.y / _zoom);
        }

        private void FrameCurrentArea(Rect canvasRect)
        {
            var visibleWidthLightYears =
                UniverseSectorGenerator.SECTOR_SIZE_LIGHT_YEARS *
                (SectorRadius * 2 + 1);

            _zoom = Mathf.Clamp(
                canvasRect.width * 0.82f /
                (float)visibleWidthLightYears,
                MinimumZoom,
                MaximumZoom);

            _panOffset = Vector2.zero;
        }

        private void DrawSectorGrid(Rect canvasRect)
        {
            var sectorSizePixels =
                (float)UniverseSectorGenerator.SECTOR_SIZE_LIGHT_YEARS *
                _zoom;

            if (sectorSizePixels < 6f)
                return;

            var center = canvasRect.center + _panOffset;

            Handles.BeginGUI();
            Handles.color = new Color(
                0.24f,
                0.31f,
                0.5f,
                0.22f);

            for (var x = -SectorRadius; x <= SectorRadius + 1; x++)
            {
                var screenX = center.x +
                              (x - 0.5f) * sectorSizePixels;

                Handles.DrawLine(
                    new Vector3(screenX, canvasRect.yMin),
                    new Vector3(screenX, canvasRect.yMax));
            }

            for (var z = -SectorRadius; z <= SectorRadius + 1; z++)
            {
                var screenY = center.y +
                              (z - 0.5f) * sectorSizePixels;

                Handles.DrawLine(
                    new Vector3(canvasRect.xMin, screenY),
                    new Vector3(canvasRect.xMax, screenY));
            }

            Handles.color = new Color(
                0.55f,
                0.68f,
                1f,
                0.65f);

            Handles.DrawWireDisc(
                center,
                Vector3.forward,
                Mathf.Max(4f, sectorSizePixels * 0.03f));

            Handles.EndGUI();
        }

        private void DrawGalaxies(
            Rect canvasRect,
            ulong selectedGalaxyID)
        {
            var centerPosition = GetSectorCenterPosition(
                _centerSectorCoordinates);

            for (var i = 0; i < _galaxies.Count; i++)
            {
                var galaxy = _galaxies[i];

                var screenPosition = ToScreenPosition(
                    canvasRect,
                    galaxy.UniversePositionLightYears);

                if (!canvasRect.Contains(screenPosition))
                    continue;

                var yDistance = math.abs(
                    galaxy.UniversePositionLightYears.y -
                    centerPosition.y);

                var maximumYDistance =
                    UniverseSectorGenerator.SECTOR_SIZE_LIGHT_YEARS *
                    (SectorRadius + 1);

                var yFade = Mathf.Clamp01(
                    1f - (float)(yDistance / maximumYDistance));

                var color = GetGalaxyColor(galaxy.Type);
                color.a = Mathf.Lerp(0.25f, 0.95f, yFade);

                var isSelected =
                    galaxy.GalaxyID == selectedGalaxyID;

                var radius = isSelected ? 6f : 3f;

                Handles.BeginGUI();

                Handles.color = color;
                Handles.DrawSolidDisc(
                    screenPosition,
                    Vector3.forward,
                    radius);

                if (isSelected)
                {
                    Handles.color = Color.white;

                    Handles.DrawWireDisc(
                        screenPosition,
                        Vector3.forward,
                        radius + 3f);
                }

                Handles.EndGUI();

                if (isSelected)
                {
                    GUI.Label(
                        new Rect(
                            screenPosition.x + 9f,
                            screenPosition.y - 10f,
                            260f,
                            20f),
                        $"Selected · {galaxy.Type}",
                        EditorStyles.miniLabel);
                }
            }
        }

        private void DrawHeader(Rect canvasRect)
        {
            GUI.Label(
                new Rect(
                    canvasRect.x + 12f,
                    canvasRect.y + 10f,
                    760f,
                    20f),
                $"Universe sector ({_centerSectorCoordinates.x}, " +
                $"{_centerSectorCoordinates.y}, " +
                $"{_centerSectorCoordinates.z}) · " +
                $"{GetLightYearsPerPixel():F0} ly per pixel",
                EditorStyles.miniLabel);

            GUI.Label(
                new Rect(
                    canvasRect.x + 12f,
                    canvasRect.y + 30f,
                    760f,
                    20f),
                "Top-down X/Z projection · sectors generate while moving",
                EditorStyles.miniLabel);
        }

        private void HandleInput(
            Rect canvasRect,
            ulong universeID)
        {
            var currentEvent = Event.current;

            if (!canvasRect.Contains(currentEvent.mousePosition))
                return;

            if (currentEvent.type == EventType.ScrollWheel)
            {
                var previousZoom = _zoom;

                _zoom = Mathf.Clamp(
                    _zoom * (1f - currentEvent.delta.y * 0.08f),
                    MinimumZoom,
                    MaximumZoom);

                var center = canvasRect.center + _panOffset;
                var mouseOffset = currentEvent.mousePosition - center;

                if (previousZoom > 0.0f)
                {
                    _panOffset += mouseOffset *
                                  (1f - _zoom / previousZoom);
                }

                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseDown &&
                currentEvent.button == 0)
            {
                if (TryGetGalaxyAtScreenPosition(
                    canvasRect,
                    currentEvent.mousePosition,
                    out var galaxyID))
                {
                    _selectGalaxy?.Invoke(galaxyID);
                    currentEvent.Use();
                    return;
                }
            }

            if (currentEvent.button != 2)
                return;

            if (currentEvent.type == EventType.MouseDown)
            {
                _isPanning = true;
                _previousMousePosition = currentEvent.mousePosition;

                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseDrag && _isPanning)
            {
                _panOffset += currentEvent.mousePosition -
                              _previousMousePosition;

                _previousMousePosition =
                    currentEvent.mousePosition;

                UpdateSectorsForCurrentView(
                    canvasRect,
                    universeID);

                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseUp)
            {
                _isPanning = false;
                currentEvent.Use();
            }
        }

        private bool TryGetGalaxyAtScreenPosition(
            Rect canvasRect,
            Vector2 mousePosition,
            out ulong galaxyID)
        {
            const float HitRadiusPixels = 10f;

            var nearestDistanceSquared =
                HitRadiusPixels * HitRadiusPixels;

            galaxyID = 0UL;

            for (var i = 0; i < _galaxies.Count; i++)
            {
                var screenPosition = ToScreenPosition(
                    canvasRect,
                    _galaxies[i].UniversePositionLightYears);

                var distanceSquared =
                    (screenPosition - mousePosition).sqrMagnitude;

                if (distanceSquared >= nearestDistanceSquared)
                    continue;

                nearestDistanceSquared = distanceSquared;
                galaxyID = _galaxies[i].GalaxyID;
            }

            return galaxyID != 0UL;
        }

        private Vector2 ToScreenPosition(
            Rect canvasRect,
            double3 universePosition)
        {
            var relative = universePosition -
                           GetSectorCenterPosition(
                               _centerSectorCoordinates);

            var center = canvasRect.center + _panOffset;

            return new Vector2(
                center.x + (float)relative.x * _zoom,
                center.y - (float)relative.z * _zoom);
        }

        private static double3 GetSectorCenterPosition(
            int3 sectorCoordinates)
        {
            var sectorSize =
                UniverseSectorGenerator.SECTOR_SIZE_LIGHT_YEARS;

            return UniverseSectorUtility.GetOriginLightYears(
                       sectorCoordinates) +
                   new double3(
                       sectorSize * 0.5,
                       sectorSize * 0.5,
                       sectorSize * 0.5);
        }

        private static int3 ClampSectorCoordinates(int3 coordinates)
        {
            return new int3(
                ClampSectorCoordinate(coordinates.x),
                ClampSectorCoordinate(coordinates.y),
                ClampSectorCoordinate(coordinates.z));
        }

        private static int ClampSectorCoordinate(int value)
        {
            return math.clamp(
                value,
                GalaxyIDUtility.MINIMUM_SECTOR_COORDINATE,
                GalaxyIDUtility.MAXIMUM_SECTOR_COORDINATE);
        }

        private float GetLightYearsPerPixel()
        {
            return 1f / Mathf.Max(
                _zoom,
                MinimumZoom);
        }

        private static Color GetGalaxyColor(GalaxyType type)
        {
            switch (type)
            {
                case GalaxyType.Spiral:
                    return new Color(0.45f, 0.7f, 1f);

                case GalaxyType.BarredSpiral:
                    return new Color(0.95f, 0.74f, 0.36f);

                case GalaxyType.Elliptical:
                    return new Color(1f, 0.82f, 0.58f);

                case GalaxyType.Lenticular:
                    return new Color(0.72f, 0.82f, 0.92f);

                case GalaxyType.Irregular:
                    return new Color(0.45f, 1f, 0.68f);

                case GalaxyType.Ring:
                    return new Color(0.95f, 0.45f, 0.82f);

                case GalaxyType.Dwarf:
                    return new Color(0.68f, 0.68f, 0.75f);

                default:
                    return Color.white;
            }
        }

        private static void DrawCenteredLabel(
            Rect rect,
            string text,
            GUIStyle style)
        {
            var content = new GUIContent(text);
            var size = style.CalcSize(content);

            GUI.Label(
                new Rect(
                    rect.center.x - size.x * 0.5f,
                    rect.center.y - size.y * 0.5f,
                    size.x,
                    size.y),
                content,
                style);
        }
    }
}