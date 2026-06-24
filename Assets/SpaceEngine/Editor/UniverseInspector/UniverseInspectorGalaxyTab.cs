using System;
using System.Collections.Generic;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Generation.Galaxy;
using SpaceEngine.Runtime.Utils;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace SpaceEngine.Editor.UniverseInspector
{
    /// <summary>
    /// Editor-only top-down projection of one generated galaxy.
    /// At close zoom it replaces the visual star sample with real generated
    /// stellar systems from nearby galaxy streaming sectors.
    /// </summary>
    public sealed class UniverseInspectorGalaxyTab : IUniverseInspectorTab
    {
        private const int StarSampleAttempts = 12_000;
        private const int MaximumVisibleStars = 5_000;

        private const int SectorRadius = 2;
        private const float SystemLayerMinimumPixelsPerLightYear = 0.75f;
        private const float MinimumZoom = 0.000001f;
        private const float MaximumZoom = 100f;

        private readonly List<SolarSystemLocationData> _visibleSolarSystems =
            new();

        private Action<ulong> _selectSolarSystem;

        private GalaxyData _galaxy;
        private bool _hasGeneratedGalaxy;

        // Broad-view visual sample only. It is never selectable.
        private Vector2[] _starPositions = Array.Empty<Vector2>();
        private float[] _starBrightness = Array.Empty<float>();

        private int3 _centerSystemSector;
        private bool _hasLoadedSystemSectors;

        private float _zoom = 1f;
        private Vector2 _panOffset;

        private bool _isPanning;
        private Vector2 _previousMousePosition;
        private bool _shouldFrameGalaxy;

        public void SetSolarSystemSelectionCallback(
            Action<ulong> selectSolarSystem)
        {
            _selectSolarSystem = selectSolarSystem;
        }

        public void Generate(CoordinatesData coordinates)
        {
            _galaxy = GalaxyGenerator.Generate(
                coordinates.UniverseID,
                coordinates.GalaxyID);

            _hasGeneratedGalaxy = true;

            GenerateVisualStarSample();

            _visibleSolarSystems.Clear();
            _centerSystemSector = int3.zero;
            _hasLoadedSystemSectors = false;

            _panOffset = Vector2.zero;
            _shouldFrameGalaxy = true;
        }

        public void DrawInspector(CoordinatesData coordinates)
        {
            GUILayout.Label(
                "Galaxy",
                EditorStyles.boldLabel);

            if (!_hasGeneratedGalaxy)
            {
                EditorGUILayout.HelpBox(
                    "Galaxy data has not been generated yet.",
                    MessageType.None);

                return;
            }

            EditorGUILayout.LabelField(
                "Galaxy ID",
                _galaxy.GalaxyID.ToString());

            EditorGUILayout.LabelField(
                "Seed",
                _galaxy.Seed.ToString());

            EditorGUILayout.LabelField(
                "Type",
                _galaxy.Type.ToString());

            EditorGUILayout.LabelField(
                "Radius",
                $"{_galaxy.RadiusLightYears:F0} ly");

            EditorGUILayout.LabelField(
                "Core Radius",
                $"{_galaxy.CoreRadiusLightYears:F0} ly");

            EditorGUILayout.LabelField(
                "Mass",
                $"{_galaxy.MassKg:E3} kg");

            EditorGUILayout.LabelField(
                "System Density",
                $"{_galaxy.BaseSystemDensityPerCubicLightYear:E3} systems / ly³");

            EditorGUILayout.LabelField(
                "Gas Density",
                _galaxy.GasDensity.ToString("F3"));

            EditorGUILayout.LabelField(
                "Metallicity",
                _galaxy.Metallicity.ToString("F4"));

            EditorGUILayout.LabelField(
                "Rotation",
                $"{_galaxy.RotationRadians * Mathf.Rad2Deg:F1}°");

            GUILayout.Space(8f);

            DrawMorphologyInspector();

            GUILayout.Space(8f);

            if (GUILayout.Button("Frame Galaxy"))
            {
                _panOffset = Vector2.zero;
                _shouldFrameGalaxy = true;
            }

            GUILayout.Space(8f);

            if (IsSystemLayerActive())
            {
                EditorGUILayout.LabelField(
                    "System Layer",
                    "Active");

                EditorGUILayout.LabelField(
                    "Center Sector",
                    $"({_centerSystemSector.x}, " +
                    $"{_centerSystemSector.y}, " +
                    $"{_centerSystemSector.z})");

                EditorGUILayout.LabelField(
                    "Visible Systems",
                    _visibleSolarSystems.Count.ToString());

                EditorGUILayout.HelpBox(
                    "Displayed points are real generated solar systems. " +
                    "Left-click a point to open the System tab.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"Broad visual sample: {_starPositions.Length} points. " +
                    $"Zoom in to at least {SystemLayerMinimumPixelsPerLightYear:F2} " +
                    "pixels per light-year to load selectable systems.",
                    MessageType.None);
            }
        }

        public void DrawCanvas(
            Rect canvasRect,
            CoordinatesData coordinates)
        {
            EditorGUI.DrawRect(
                canvasRect,
                new Color(0.015f, 0.018f, 0.035f));

            if (!_hasGeneratedGalaxy)
            {
                DrawCenteredLabel(
                    canvasRect,
                    "Galaxy data has not been generated yet.",
                    EditorStyles.centeredGreyMiniLabel);

                return;
            }

            if (_shouldFrameGalaxy)
            {
                FrameGalaxy(canvasRect);
                _shouldFrameGalaxy = false;
            }

            UpdateSystemSectorsForCurrentView();

            DrawGrid(canvasRect);
            DrawGalaxy(canvasRect);
            DrawHeader(canvasRect);

            HandleInput(canvasRect);
        }

        private void DrawMorphologyInspector()
        {
            GUILayout.Label(
                "Morphology",
                EditorStyles.boldLabel);

            GUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField(
                "Disk Thickness",
                $"{_galaxy.DiskThicknessLightYears:F0} ly");

            EditorGUILayout.LabelField(
                "Ellipticity",
                _galaxy.Ellipticity.ToString("F3"));

            if (_galaxy.SpiralArmCount > 0)
            {
                EditorGUILayout.LabelField(
                    "Spiral Arms",
                    _galaxy.SpiralArmCount.ToString());

                EditorGUILayout.LabelField(
                    "Arm Tightness",
                    _galaxy.SpiralArmTightness.ToString("F3"));
            }

            if (_galaxy.BarLengthLightYears > 0.0)
            {
                EditorGUILayout.LabelField(
                    "Bar Length",
                    $"{_galaxy.BarLengthLightYears:F0} ly");
            }

            if (_galaxy.RingRadiusLightYears > 0.0)
            {
                EditorGUILayout.LabelField(
                    "Ring Radius",
                    $"{_galaxy.RingRadiusLightYears:F0} ly");

                EditorGUILayout.LabelField(
                    "Ring Width",
                    $"{_galaxy.RingWidthLightYears:F0} ly");
            }

            if (_galaxy.Irregularity > 0.0)
            {
                EditorGUILayout.LabelField(
                    "Irregularity",
                    _galaxy.Irregularity.ToString("F3"));
            }

            GUILayout.EndVertical();
        }

        private void GenerateVisualStarSample()
        {
            var random = new QuantumRandom(
                GalaxyIDUtility.Combine(
                    _galaxy.Seed,
                    0x535441525F564953UL));

            var positions = new Vector2[MaximumVisibleStars];
            var brightness = new float[MaximumVisibleStars];

            var count = 0;
            var radius = _galaxy.RadiusLightYears;

            for (var i = 0;
                 i < StarSampleAttempts && count < MaximumVisibleStars;
                 i++)
            {
                var localPosition = new Vector2(
                    (float)random.NextDouble(-radius, radius),
                    (float)random.NextDouble(-radius, radius));

                var density = GalaxyDensityUtility.GetDensity(
                    _galaxy,
                    new double3(
                        localPosition.x,
                        0.0,
                        localPosition.y));

                if (density <= 0.0)
                    continue;

                if (random.NextDouble() > density)
                    continue;

                positions[count] = localPosition;
                brightness[count] = Mathf.Lerp(
                    0.15f,
                    1f,
                    (float)density);

                count++;
            }

            _starPositions = new Vector2[count];
            _starBrightness = new float[count];

            Array.Copy(positions, _starPositions, count);
            Array.Copy(brightness, _starBrightness, count);
        }

        private void UpdateSystemSectorsForCurrentView()
        {
            if (!IsSystemLayerActive())
            {
                _hasLoadedSystemSectors = false;
                _visibleSolarSystems.Clear();
                return;
            }

            var viewedPosition = GetViewedGalaxyPosition();
            var newCenterSector = GalaxySectorUtility.GetCoordinates(
                viewedPosition);

            if (_hasLoadedSystemSectors &&
                newCenterSector.Equals(_centerSystemSector))
            {
                return;
            }

            _centerSystemSector = newCenterSector;
            _hasLoadedSystemSectors = true;

            ReloadVisibleSystemSectors();
        }

        private void ReloadVisibleSystemSectors()
        {
            _visibleSolarSystems.Clear();

            for (var y = -SectorRadius; y <= SectorRadius; y++)
            {
                for (var z = -SectorRadius; z <= SectorRadius; z++)
                {
                    for (var x = -SectorRadius; x <= SectorRadius; x++)
                    {
                        var sectorCoordinates =
                            _centerSystemSector + new int3(x, y, z);

                        var sector = GalaxySectorGenerator.Generate(
                            _galaxy,
                            sectorCoordinates);

                        for (var i = 0;
                             i < sector.SolarSystems.Length;
                             i++)
                        {
                            _visibleSolarSystems.Add(
                                sector.SolarSystems[i]);
                        }
                    }
                }
            }
        }

        private void FrameGalaxy(Rect canvasRect)
        {
            var availableRadiusPixels =
                Mathf.Min(
                    canvasRect.width,
                    canvasRect.height) * 0.42f;

            _zoom = Mathf.Max(
                0.0001f,
                availableRadiusPixels /
                (float)_galaxy.RadiusLightYears);

            _panOffset = Vector2.zero;
        }

        private void DrawGrid(Rect canvasRect)
        {
            var pixelsPerLightYear = _zoom;

            if (pixelsPerLightYear <= 0.0f)
                return;

            var desiredStepLightYears =
                100f / pixelsPerLightYear;

            var stepLightYears = GetRoundedGridStep(
                desiredStepLightYears);

            var gridStepPixels =
                stepLightYears * pixelsPerLightYear;

            if (gridStepPixels < 12f)
                return;

            var center = canvasRect.center + _panOffset;

            Handles.BeginGUI();

            Handles.color = new Color(
                0.24f,
                0.3f,
                0.46f,
                0.12f);

            var startX = center.x % gridStepPixels;

            while (startX > canvasRect.xMin)
                startX -= gridStepPixels;

            while (startX < canvasRect.xMin)
                startX += gridStepPixels;

            for (var x = startX;
                 x <= canvasRect.xMax;
                 x += gridStepPixels)
            {
                Handles.DrawLine(
                    new Vector3(x, canvasRect.yMin),
                    new Vector3(x, canvasRect.yMax));
            }

            var startY = center.y % gridStepPixels;

            while (startY > canvasRect.yMin)
                startY -= gridStepPixels;

            while (startY < canvasRect.yMin)
                startY += gridStepPixels;

            for (var y = startY;
                 y <= canvasRect.yMax;
                 y += gridStepPixels)
            {
                Handles.DrawLine(
                    new Vector3(canvasRect.xMin, y),
                    new Vector3(canvasRect.xMax, y));
            }

            Handles.EndGUI();
        }

        private void DrawGalaxy(Rect canvasRect)
        {
            var center = canvasRect.center + _panOffset;
            var galaxyColor = GetGalaxyColor(_galaxy.Type);

            DrawGalaxyBoundary(center);

            if (IsSystemLayerActive())
            {
                DrawSystemSectorGrid(canvasRect);
                DrawSelectableSolarSystems(canvasRect);
            }
            else
            {
                DrawVisualStarSample(canvasRect, center, galaxyColor);
            }

            Handles.BeginGUI();

            Handles.color = new Color(1f, 1f, 1f, 0.7f);
            Handles.DrawSolidDisc(
                center,
                Vector3.forward,
                3f);

            Handles.color = new Color(
                galaxyColor.r,
                galaxyColor.g,
                galaxyColor.b,
                0.55f);

            Handles.DrawWireDisc(
                center,
                Vector3.forward,
                Mathf.Max(
                    4f,
                    (float)_galaxy.CoreRadiusLightYears * _zoom));

            Handles.EndGUI();
        }

        private void DrawVisualStarSample(
            Rect canvasRect,
            Vector2 center,
            Color galaxyColor)
        {
            for (var i = 0; i < _starPositions.Length; i++)
            {
                var screenPosition = center +
                                     _starPositions[i] * _zoom;

                if (!canvasRect.Contains(screenPosition))
                    continue;

                var intensity = _starBrightness[i];

                var pointColor = new Color(
                    galaxyColor.r,
                    galaxyColor.g,
                    galaxyColor.b,
                    Mathf.Lerp(0.18f, 0.95f, intensity));

                EditorGUI.DrawRect(
                    new Rect(
                        screenPosition.x,
                        screenPosition.y,
                        intensity > 0.75f ? 2f : 1f,
                        intensity > 0.75f ? 2f : 1f),
                    pointColor);
            }
        }

        private void DrawSystemSectorGrid(Rect canvasRect)
        {
            var sectorSizePixels =
                (float)GalaxySectorGenerator.SECTOR_SIZE_LIGHT_YEARS *
                _zoom;

            if (sectorSizePixels < 8f)
                return;

            var center = canvasRect.center + _panOffset;

            Handles.BeginGUI();
            Handles.color = new Color(
                0.28f,
                0.5f,
                0.9f,
                0.18f);

            var startX = center.x % sectorSizePixels;

            while (startX > canvasRect.xMin)
                startX -= sectorSizePixels;

            while (startX < canvasRect.xMin)
                startX += sectorSizePixels;

            for (var x = startX;
                 x <= canvasRect.xMax;
                 x += sectorSizePixels)
            {
                Handles.DrawLine(
                    new Vector3(x, canvasRect.yMin),
                    new Vector3(x, canvasRect.yMax));
            }

            var startY = center.y % sectorSizePixels;

            while (startY > canvasRect.yMin)
                startY -= sectorSizePixels;

            while (startY < canvasRect.yMin)
                startY += sectorSizePixels;

            for (var y = startY;
                 y <= canvasRect.yMax;
                 y += sectorSizePixels)
            {
                Handles.DrawLine(
                    new Vector3(canvasRect.xMin, y),
                    new Vector3(canvasRect.xMax, y));
            }

            Handles.EndGUI();
        }

        private void DrawSelectableSolarSystems(Rect canvasRect)
        {
            for (var i = 0; i < _visibleSolarSystems.Count; i++)
            {
                var system = _visibleSolarSystems[i];

                var screenPosition = ToScreenPosition(
                    canvasRect,
                    system.GalaxyLocalPositionLightYears);

                if (!canvasRect.Contains(screenPosition))
                    continue;

                var radius = GetSystemVisualRadius(system);

                Handles.BeginGUI();

                Handles.color = GetSystemColor(system);
                Handles.DrawSolidDisc(
                    screenPosition,
                    Vector3.forward,
                    radius);

                Handles.color = new Color(1f, 1f, 1f, 0.45f);
                Handles.DrawWireDisc(
                    screenPosition,
                    Vector3.forward,
                    radius + 1f);

                Handles.EndGUI();
            }
        }

        private void DrawGalaxyBoundary(Vector2 center)
        {
            var radius =
                (float)_galaxy.RadiusLightYears * _zoom;

            if (radius < 2f)
                return;

            Handles.BeginGUI();

            var color = GetGalaxyColor(_galaxy.Type);

            Handles.color = new Color(
                color.r,
                color.g,
                color.b,
                0.20f);

            Handles.DrawWireDisc(
                center,
                Vector3.forward,
                radius);

            Handles.EndGUI();
        }

        private void DrawHeader(Rect canvasRect)
        {
            var mode = IsSystemLayerActive()
                ? "System selection"
                : "Galaxy overview";

            GUI.Label(
                new Rect(
                    canvasRect.x + 12f,
                    canvasRect.y + 10f,
                    760f,
                    20f),
                $"{_galaxy.Type} · " +
                $"Radius {_galaxy.RadiusLightYears:F0} ly · " +
                $"Scale {GetLightYearsPerPixel():F2} ly per pixel · " +
                mode,
                EditorStyles.miniLabel);

            GUI.Label(
                new Rect(
                    canvasRect.x + 12f,
                    canvasRect.y + 30f,
                    760f,
                    20f),
                IsSystemLayerActive()
                    ? $"Real systems: {_visibleSolarSystems.Count} · " +
                      "left-click a star to open its system"
                    : "Zoom in to load real selectable systems",
                EditorStyles.miniLabel);
        }

        private void HandleInput(Rect canvasRect)
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
                var mouseOffset =
                    currentEvent.mousePosition - center;

                if (previousZoom > 0.0f)
                {
                    _panOffset += mouseOffset *
                                  (1f - _zoom / previousZoom);
                }

                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseDown &&
                currentEvent.button == 0 &&
                IsSystemLayerActive())
            {
                if (TryGetSolarSystemAtScreenPosition(
                    canvasRect,
                    currentEvent.mousePosition,
                    out var solarSystemID))
                {
                    _selectSolarSystem?.Invoke(solarSystemID);
                    currentEvent.Use();
                    return;
                }
            }

            if (currentEvent.button != 2)
                return;

            if (currentEvent.type == EventType.MouseDown)
            {
                _isPanning = true;
                _previousMousePosition =
                    currentEvent.mousePosition;

                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseDrag &&
                _isPanning)
            {
                _panOffset +=
                    currentEvent.mousePosition -
                    _previousMousePosition;

                _previousMousePosition =
                    currentEvent.mousePosition;

                UpdateSystemSectorsForCurrentView();

                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseUp)
            {
                _isPanning = false;
                currentEvent.Use();
            }
        }

        private bool TryGetSolarSystemAtScreenPosition(
            Rect canvasRect,
            Vector2 mousePosition,
            out ulong solarSystemID)
        {
            const float HitRadiusPixels = 12f;

            var nearestDistanceSquared =
                HitRadiusPixels * HitRadiusPixels;

            solarSystemID = 0UL;

            for (var i = 0; i < _visibleSolarSystems.Count; i++)
            {
                var screenPosition = ToScreenPosition(
                    canvasRect,
                    _visibleSolarSystems[i]
                        .GalaxyLocalPositionLightYears);

                var distanceSquared =
                    (screenPosition - mousePosition).sqrMagnitude;

                if (distanceSquared >= nearestDistanceSquared)
                    continue;

                nearestDistanceSquared = distanceSquared;
                solarSystemID = _visibleSolarSystems[i].SolarSystemID;
            }

            return solarSystemID != 0UL;
        }

        private Vector2 ToScreenPosition(
            Rect canvasRect,
            double3 galaxyLocalPosition)
        {
            var center = canvasRect.center + _panOffset;

            return new Vector2(
                center.x + (float)galaxyLocalPosition.x * _zoom,
                center.y - (float)galaxyLocalPosition.z * _zoom);
        }

        private double3 GetViewedGalaxyPosition()
        {
            return new double3(
                -_panOffset.x / _zoom,
                0.0,
                _panOffset.y / _zoom);
        }

        private bool IsSystemLayerActive()
        {
            return _zoom >= SystemLayerMinimumPixelsPerLightYear;
        }

        private float GetSystemVisualRadius(
            SolarSystemLocationData system)
        {
            return Mathf.Clamp(
                3f +
                Mathf.Sqrt(
                    Mathf.Max(
                        0.0f,
                        (float)system.EstimatedSystemMassSolarMasses)) *
                1.4f,
                3f,
                8f);
        }

        private static Color GetSystemColor(
            SolarSystemLocationData system)
        {
            var normalizedMass = Mathf.Clamp01(
                (float)(system.EstimatedSystemMassSolarMasses / 4.0));

            return Color.Lerp(
                new Color(0.7f, 0.82f, 1f, 0.95f),
                new Color(1f, 0.72f, 0.3f, 0.95f),
                normalizedMass);
        }

        private float GetLightYearsPerPixel()
        {
            return 1f / Mathf.Max(_zoom, MinimumZoom);
        }

        private static float GetRoundedGridStep(
            float desiredStep)
        {
            var power = Mathf.Pow(
                10f,
                Mathf.Floor(Mathf.Log10(
                    Mathf.Max(desiredStep, 0.0001f))));

            var normalized = desiredStep / power;

            if (normalized <= 1f)
                return power;

            if (normalized <= 2f)
                return 2f * power;

            if (normalized <= 5f)
                return 5f * power;

            return 10f * power;
        }

        private static Color GetGalaxyColor(
            GalaxyType type)
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
