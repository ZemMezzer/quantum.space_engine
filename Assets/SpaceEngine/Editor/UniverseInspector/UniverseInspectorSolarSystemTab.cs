using System;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.Planet;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Generation.SolarSystem;
using UnityEditor;
using UnityEngine;

namespace SpaceEngine.Editor.UniverseInspector
{
    /// <summary>
    /// Editor-only top-down projection of a generated stellar system.
    /// </summary>
    public sealed class UniverseInspectorSolarSystemTab : IUniverseInspectorTab
    {
        private const double AstronomicalUnitMeters = 149_597_870_700.0;
        private const double SolarMassKg = 1.98847e30;
        private const double SolarRadiusMeters = 6.957e8;
        private const double EarthMassKg = 5.9722e24;
        private const double EarthRadiusMeters = 6_371_000.0;

        // Zoom is converted into pixels per AU through: zoom * 100.
        // 0.001 = 0.1 pixels per AU, enough for very wide systems.
        private const float MinimumZoom = 0.001f;
        private const float MaximumZoom = 100f;

        private SolarSystemData _solarSystem;
        private bool _hasGeneratedSystem;

        private float _zoom = 1f;
        private Vector2 _panOffset;

        private bool _isPanning;
        private Vector2 _previousMousePosition;
        private bool _shouldFrameSystem;

        public void Generate(CoordinatesData coordinates)
        {
            _solarSystem = SolarSystemGenerator.Generate(coordinates);
            _hasGeneratedSystem = true;

            _panOffset = Vector2.zero;
            _shouldFrameSystem = true;
        }

        public void DrawInspector(CoordinatesData coordinates)
        {
            GUILayout.Label(
                "Solar System",
                EditorStyles.boldLabel);

            if (!_hasGeneratedSystem)
            {
                EditorGUILayout.HelpBox(
                    "System has not been generated yet.",
                    MessageType.None);

                return;
            }

            EditorGUILayout.LabelField(
                "System Seed",
                _solarSystem.Seed.ToString());

            EditorGUILayout.LabelField(
                "Star Count",
                _solarSystem.StarCount.ToString());

            EditorGUILayout.LabelField(
                "Planet Count",
                _solarSystem.PlanetCount.ToString());

            GUILayout.Space(8f);

            if (GUILayout.Button("Frame System"))
            {
                _panOffset = Vector2.zero;
                _shouldFrameSystem = true;
            }

            GUILayout.Space(8f);

            DrawStarsInspector();
            DrawPlanetsInspector();
        }

        public void DrawCanvas(
            Rect canvasRect,
            CoordinatesData coordinates)
        {
            if (_hasGeneratedSystem && _shouldFrameSystem)
            {
                FrameSystem(canvasRect);
                _shouldFrameSystem = false;
            }

            DrawGrid(canvasRect);
            DrawHeader(canvasRect);

            if (!_hasGeneratedSystem)
            {
                DrawCenteredLabel(
                    canvasRect,
                    "System data has not been generated yet.",
                    EditorStyles.centeredGreyMiniLabel);

                HandleInput(canvasRect);
                return;
            }

            var center = canvasRect.center + _panOffset;

            DrawBarycenter(center);
            DrawPlanetOrbits(center);
            DrawStarOrbits(center);
            DrawPlanets(center);
            DrawStars(center);

            HandleInput(canvasRect);
        }

        private void FrameSystem(Rect canvasRect)
        {
            var maximumRadiusAu = GetMaximumSystemRadiusAu();

            if (maximumRadiusAu <= 0.0)
                maximumRadiusAu = 1.0;

            // Leave a visible margin around outermost orbit.
            var availableRadiusPixels =
                Mathf.Min(canvasRect.width, canvasRect.height) * 0.40f;

            var pixelsPerAu =
                availableRadiusPixels / (float)maximumRadiusAu;

            _zoom = Mathf.Clamp(
                pixelsPerAu / 100f,
                MinimumZoom,
                MaximumZoom);

            _panOffset = Vector2.zero;
        }

        private double GetMaximumSystemRadiusAu()
        {
            var maximumRadiusMeters = 0.0;

            for (var i = 0; i < _solarSystem.StarCount; i++)
            {
                var orbit = _solarSystem.Stars[i].BarycentricOrbit;

                maximumRadiusMeters = Math.Max(
                    maximumRadiusMeters,
                    GetOrbitApoapsisMeters(orbit));
            }

            for (var i = 0; i < _solarSystem.PlanetCount; i++)
            {
                var orbit = _solarSystem.Planets[i].Orbit;

                maximumRadiusMeters = Math.Max(
                    maximumRadiusMeters,
                    GetOrbitApoapsisMeters(orbit));
            }

            return maximumRadiusMeters / AstronomicalUnitMeters;
        }

        private static double GetOrbitApoapsisMeters(OrbitData orbit)
        {
            if (orbit.SemiMajorAxisMeters <= 0.0)
                return 0.0;

            return orbit.SemiMajorAxisMeters *
                   (1.0 + orbit.Eccentricity);
        }

        private void DrawStarsInspector()
        {
            GUILayout.Label(
                "Stars",
                EditorStyles.boldLabel);

            for (var i = 0; i < _solarSystem.StarCount; i++)
            {
                var star = _solarSystem.Stars[i];

                GUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.LabelField(
                    $"Star {i}",
                    star.Type.ToString());

                EditorGUILayout.LabelField(
                    "Mass",
                    $"{star.MassKg / SolarMassKg:F2} M☉");

                EditorGUILayout.LabelField(
                    "Radius",
                    $"{star.RadiusMeters / SolarRadiusMeters:F2} R☉");

                EditorGUILayout.LabelField(
                    "Temperature",
                    $"{star.TemperatureKelvin:F0} K");

                if (star.BarycentricOrbit.SemiMajorAxisMeters > 0.0)
                {
                    EditorGUILayout.LabelField(
                        "Orbit Radius",
                        $"{star.BarycentricOrbit.SemiMajorAxisMeters / AstronomicalUnitMeters:F3} AU");

                    EditorGUILayout.LabelField(
                        "Eccentricity",
                        star.BarycentricOrbit.Eccentricity.ToString("F3"));
                }
                else
                {
                    EditorGUILayout.LabelField(
                        "Orbit",
                        "System barycenter");
                }

                GUILayout.EndVertical();
            }
        }

        private void DrawPlanetsInspector()
        {
            GUILayout.Space(8f);

            GUILayout.Label(
                "Planets",
                EditorStyles.boldLabel);

            if (_solarSystem.PlanetCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "No planets were generated for this system.",
                    MessageType.None);

                return;
            }

            for (var i = 0; i < _solarSystem.PlanetCount; i++)
            {
                var planet = _solarSystem.Planets[i];

                GUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.LabelField(
                    $"Planet {i}",
                    planet.Type.ToString());

                EditorGUILayout.LabelField(
                    "Orbit Radius",
                    $"{planet.Orbit.SemiMajorAxisMeters / AstronomicalUnitMeters:F3} AU");

                EditorGUILayout.LabelField(
                    "Mass",
                    $"{planet.MassKg / EarthMassKg:F3} M⊕");

                EditorGUILayout.LabelField(
                    "Radius",
                    $"{planet.RadiusMeters / EarthRadiusMeters:F3} R⊕");

                EditorGUILayout.LabelField(
                    "Temperature",
                    $"{planet.SurfaceTemperatureKelvin:F0} K");

                EditorGUILayout.LabelField(
                    "Moons",
                    planet.MoonCount.ToString());

                if (planet.HasRings)
                {
                    EditorGUILayout.LabelField(
                        "Rings",
                        "Yes");
                }

                GUILayout.EndVertical();
            }
        }

        private void DrawGrid(Rect canvasRect)
        {
            var center = canvasRect.center + _panOffset;
            var pixelsPerAu = GetPixelsPerAu();
            var gridStepAu = GetGridStepAu(pixelsPerAu);
            var gridStepPixels = gridStepAu * pixelsPerAu;

            if (gridStepPixels < 8f)
                return;

            var gridColor = new Color(
                0.22f,
                0.28f,
                0.40f,
                0.12f);

            Handles.BeginGUI();
            Handles.color = gridColor;

            var startX = center.x % gridStepPixels;

            if (startX < canvasRect.xMin)
                startX += gridStepPixels;

            while (startX > canvasRect.xMin)
                startX -= gridStepPixels;

            for (var x = startX; x <= canvasRect.xMax; x += gridStepPixels)
            {
                Handles.DrawLine(
                    new Vector3(x, canvasRect.yMin),
                    new Vector3(x, canvasRect.yMax));
            }

            var startY = center.y % gridStepPixels;

            if (startY < canvasRect.yMin)
                startY += gridStepPixels;

            while (startY > canvasRect.yMin)
                startY -= gridStepPixels;

            for (var y = startY; y <= canvasRect.yMax; y += gridStepPixels)
            {
                Handles.DrawLine(
                    new Vector3(canvasRect.xMin, y),
                    new Vector3(canvasRect.xMax, y));
            }

            Handles.EndGUI();
        }

        private static float GetGridStepAu(float pixelsPerAu)
        {
            var desiredStepAu = 80f / Mathf.Max(pixelsPerAu, 0.0001f);
            var powerOfTen = Mathf.Pow(
                10f,
                Mathf.Floor(Mathf.Log10(desiredStepAu)));

            var normalized = desiredStepAu / powerOfTen;

            if (normalized <= 1f)
                return powerOfTen;

            if (normalized <= 2f)
                return 2f * powerOfTen;

            if (normalized <= 5f)
                return 5f * powerOfTen;

            return 10f * powerOfTen;
        }

        private void DrawHeader(Rect canvasRect)
        {
            GUI.Label(
                new Rect(
                    canvasRect.x + 12f,
                    canvasRect.y + 10f,
                    500f,
                    20f),
                $"Top-down projection · {GetAuPerPixel():F4} AU per pixel · " +
                $"Visible radius: {GetMaximumSystemRadiusAu():F2} AU",
                EditorStyles.miniLabel);
        }

        private void DrawBarycenter(Vector2 center)
        {
            Handles.BeginGUI();

            Handles.color = new Color(
                0.8f,
                0.88f,
                1f,
                0.8f);

            Handles.DrawSolidDisc(
                center,
                Vector3.forward,
                4f);

            Handles.color = new Color(
                0.65f,
                0.75f,
                1f,
                0.45f);

            Handles.DrawWireDisc(
                center,
                Vector3.forward,
                9f);

            Handles.EndGUI();

            GUI.Label(
                new Rect(
                    center.x + 11f,
                    center.y + 2f,
                    100f,
                    20f),
                "Barycenter",
                EditorStyles.miniLabel);
        }

        private void DrawStarOrbits(Vector2 center)
        {
            for (var i = 0; i < _solarSystem.StarCount; i++)
            {
                var star = _solarSystem.Stars[i];
                var orbit = star.BarycentricOrbit;

                if (orbit.SemiMajorAxisMeters <= 0.0)
                    continue;

                DrawOrbit(
                    center,
                    orbit,
                    GetStarColor(star.Type, 0.55f),
                    128);
            }
        }

        private void DrawPlanetOrbits(Vector2 center)
        {
            for (var i = 0; i < _solarSystem.PlanetCount; i++)
            {
                var planet = _solarSystem.Planets[i];

                if (planet.Orbit.SemiMajorAxisMeters <= 0.0)
                    continue;

                DrawOrbit(
                    center,
                    planet.Orbit,
                    GetPlanetColor(planet.Type, 0.32f),
                    96);
            }
        }

        private void DrawOrbit(
            Vector2 center,
            OrbitData orbit,
            Color color,
            int segments)
        {
            Handles.BeginGUI();
            Handles.color = color;

            var hasPreviousPosition = false;
            var previousPosition = Vector2.zero;

            for (var i = 0; i <= segments; i++)
            {
                var meanAnomaly =
                    i / (double)segments * Math.PI * 2.0;

                var eccentricAnomaly = SolveEccentricAnomaly(
                    meanAnomaly,
                    orbit.Eccentricity);

                var x = orbit.SemiMajorAxisMeters *
                        (Math.Cos(eccentricAnomaly) -
                         orbit.Eccentricity);

                var y = orbit.SemiMajorAxisMeters *
                        Math.Sqrt(
                            1.0 -
                            orbit.Eccentricity *
                            orbit.Eccentricity) *
                        Math.Sin(eccentricAnomaly);

                var projected = RotateOrbit(
                    x,
                    y,
                    orbit.ArgumentOfPeriapsisRadians +
                    orbit.LongitudeOfAscendingNodeRadians);

                var screenPosition = ToScreenPosition(
                    center,
                    projected);

                if (hasPreviousPosition)
                {
                    Handles.DrawLine(
                        previousPosition,
                        screenPosition);
                }

                previousPosition = screenPosition;
                hasPreviousPosition = true;
            }

            Handles.EndGUI();
        }

        private void DrawStars(Vector2 center)
        {
            for (var i = 0; i < _solarSystem.StarCount; i++)
            {
                var star = _solarSystem.Stars[i];

                var worldPosition = GetPositionAtEpoch(
                    star.BarycentricOrbit);

                var screenPosition = ToScreenPosition(
                    center,
                    worldPosition);

                var radius = GetVisualStarRadius(star);

                Handles.BeginGUI();

                Handles.color = GetStarColor(
                    star.Type,
                    1f);

                Handles.DrawSolidDisc(
                    screenPosition,
                    Vector3.forward,
                    radius);

                Handles.color = new Color(
                    1f,
                    1f,
                    1f,
                    0.5f);

                Handles.DrawWireDisc(
                    screenPosition,
                    Vector3.forward,
                    radius + 1f);

                Handles.EndGUI();

                GUI.Label(
                    new Rect(
                        screenPosition.x + radius + 5f,
                        screenPosition.y - 9f,
                        220f,
                        20f),
                    $"{star.Type} · {star.MassKg / SolarMassKg:F2} M☉",
                    EditorStyles.miniLabel);
            }
        }

        private void DrawPlanets(Vector2 center)
        {
            for (var i = 0; i < _solarSystem.PlanetCount; i++)
            {
                var planet = _solarSystem.Planets[i];

                var worldPosition = GetPositionAtEpoch(
                    planet.Orbit);

                var screenPosition = ToScreenPosition(
                    center,
                    worldPosition);

                var radius = GetVisualPlanetRadius(planet);

                Handles.BeginGUI();

                Handles.color = GetPlanetColor(
                    planet.Type,
                    1f);

                Handles.DrawSolidDisc(
                    screenPosition,
                    Vector3.forward,
                    radius);

                if (planet.HasRings)
                {
                    Handles.color = GetPlanetColor(
                        planet.Type,
                        0.55f);

                    Handles.DrawWireDisc(
                        screenPosition,
                        Vector3.forward,
                        radius + 3f);
                }

                Handles.EndGUI();

                GUI.Label(
                    new Rect(
                        screenPosition.x + radius + 4f,
                        screenPosition.y - 8f,
                        220f,
                        18f),
                    $"P{i} · {planet.Type}",
                    EditorStyles.miniLabel);
            }
        }

        private Vector2 GetPositionAtEpoch(OrbitData orbit)
        {
            if (orbit.SemiMajorAxisMeters <= 0.0)
                return Vector2.zero;

            var eccentricAnomaly = SolveEccentricAnomaly(
                orbit.MeanAnomalyAtEpochRadians,
                orbit.Eccentricity);

            var x = orbit.SemiMajorAxisMeters *
                    (Math.Cos(eccentricAnomaly) -
                     orbit.Eccentricity);

            var y = orbit.SemiMajorAxisMeters *
                    Math.Sqrt(
                        1.0 -
                        orbit.Eccentricity *
                        orbit.Eccentricity) *
                    Math.Sin(eccentricAnomaly);

            return RotateOrbit(
                x,
                y,
                orbit.ArgumentOfPeriapsisRadians +
                orbit.LongitudeOfAscendingNodeRadians);
        }

        private Vector2 RotateOrbit(
            double x,
            double y,
            double angleRadians)
        {
            var cos = Math.Cos(angleRadians);
            var sin = Math.Sin(angleRadians);

            return new Vector2(
                (float)(x * cos - y * sin),
                (float)(x * sin + y * cos));
        }

        private Vector2 ToScreenPosition(
            Vector2 center,
            Vector2 positionMeters)
        {
            var scale = GetPixelsPerAu() /
                        (float)AstronomicalUnitMeters;

            return new Vector2(
                center.x + positionMeters.x * scale,
                center.y - positionMeters.y * scale);
        }

        private float GetPixelsPerAu()
        {
            return _zoom * 100f;
        }

        private float GetVisualStarRadius(StarData star)
        {
            var radiusInSolarRadii =
                star.RadiusMeters / SolarRadiusMeters;

            return Mathf.Clamp(
                7f +
                Mathf.Sqrt((float)radiusInSolarRadii) * 8f,
                7f,
                24f);
        }

        private float GetVisualPlanetRadius(PlanetData planet)
        {
            var radiusInEarthRadii =
                planet.RadiusMeters / EarthRadiusMeters;

            return Mathf.Clamp(
                3f +
                Mathf.Sqrt((float)radiusInEarthRadii) * 1.8f,
                3f,
                11f);
        }

        private float GetAuPerPixel()
        {
            return 1f / GetPixelsPerAu();
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

                var previousPixelsPerAu = previousZoom * 100f;
                var newPixelsPerAu = _zoom * 100f;

                var center = canvasRect.center + _panOffset;
                var mouseOffset = currentEvent.mousePosition - center;

                if (previousPixelsPerAu > 0.0001f)
                {
                    _panOffset += mouseOffset *
                                  (1f - newPixelsPerAu / previousPixelsPerAu);
                }

                currentEvent.Use();
                return;
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
                _panOffset +=
                    currentEvent.mousePosition -
                    _previousMousePosition;

                _previousMousePosition =
                    currentEvent.mousePosition;

                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseUp)
            {
                _isPanning = false;
                currentEvent.Use();
            }
        }

        private static double SolveEccentricAnomaly(
            double meanAnomaly,
            double eccentricity)
        {
            if (eccentricity <= 0.000001)
                return meanAnomaly;

            var eccentricAnomaly = meanAnomaly;

            for (var i = 0; i < 8; i++)
            {
                var sin = Math.Sin(eccentricAnomaly);
                var cos = Math.Cos(eccentricAnomaly);

                var correction =
                    (eccentricAnomaly -
                     eccentricity * sin -
                     meanAnomaly) /
                    (1.0 - eccentricity * cos);

                eccentricAnomaly -= correction;

                if (Math.Abs(correction) < 0.0000001)
                    break;
            }

            return eccentricAnomaly;
        }

        private static Color GetStarColor(
            StarType starType,
            float alpha)
        {
            Color color;

            switch (starType)
            {
                case StarType.RedDwarf:
                case StarType.RedGiant:
                    color = new Color(1f, 0.28f, 0.18f);
                    break;

                case StarType.OrangeDwarf:
                    color = new Color(1f, 0.62f, 0.18f);
                    break;

                case StarType.YellowDwarf:
                    color = new Color(1f, 0.92f, 0.48f);
                    break;

                case StarType.WhiteDwarf:
                    color = new Color(0.72f, 0.88f, 1f);
                    break;

                case StarType.NeutronStar:
                case StarType.Pulsar:
                    color = new Color(0.3f, 0.7f, 1f);
                    break;

                case StarType.BlackHole:
                    color = new Color(0.12f, 0.12f, 0.16f);
                    break;

                default:
                    color = Color.white;
                    break;
            }

            color.a = alpha;
            return color;
        }

        private static Color GetPlanetColor(
            PlanetType planetType,
            float alpha)
        {
            Color color;

            switch (planetType)
            {
                case PlanetType.Terrestrial:
                    color = new Color(0.78f, 0.47f, 0.24f);
                    break;

                case PlanetType.Ocean:
                    color = new Color(0.2f, 0.55f, 1f);
                    break;

                case PlanetType.GasGiant:
                    color = new Color(0.92f, 0.68f, 0.35f);
                    break;

                case PlanetType.IceGiant:
                    color = new Color(0.42f, 0.9f, 0.95f);
                    break;

                case PlanetType.DwarfPlanet:
                    color = new Color(0.62f, 0.62f, 0.68f);
                    break;

                default:
                    color = Color.white;
                    break;
            }

            color.a = alpha;
            return color;
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