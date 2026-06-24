using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.Planet;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Generation.SolarSystem;
using UnityEditor;
using UnityEngine;

namespace SpaceEngine.Editor.UniverseInspector
{
    /// <summary>
    /// Editor-only inspector for a requested celestial body inside a system.
    /// Currently it resolves IDs only against the generated planet list.
    /// </summary>
    public sealed class UniverseInspectorPlanetTab : IUniverseInspectorTab
    {
        private const double AstronomicalUnitMeters = 149_597_870_700.0;
        private const double EarthMassKg = 5.9722e24;
        private const double EarthRadiusMeters = 6_371_000.0;
        private const double EarthGravityMetersPerSecondSquared = 9.80665;
        private const double EarthAtmospherePressurePascals = 101_325.0;

        private SolarSystemData _solarSystem;
        private PlanetData _planet;

        private CelestialBodyCoordinatesData _bodyCoordinates;

        private bool _hasGeneratedSystem;
        private bool _hasSelectedPlanet;

        public void SetCelestialBodyCoordinates(
            CelestialBodyCoordinatesData coordinates)
        {
            _bodyCoordinates = coordinates;
        }

        public void Generate(CoordinatesData coordinates)
        {
            _solarSystem = SolarSystemGenerator.Generate(coordinates);
            _hasGeneratedSystem = true;

            _hasSelectedPlanet =
                _bodyCoordinates.CelestialBodyID <
                _solarSystem.PlanetCount;

            if (_hasSelectedPlanet)
            {
                _planet = _solarSystem.Planets[
                    (int)_bodyCoordinates.CelestialBodyID];
            }
        }

        public void DrawInspector(CoordinatesData coordinates)
        {
            GUILayout.Label(
                "Celestial Body",
                EditorStyles.boldLabel);

            if (!_hasGeneratedSystem)
            {
                EditorGUILayout.HelpBox(
                    "System data has not been generated yet.",
                    MessageType.None);

                return;
            }

            EditorGUILayout.LabelField(
                "System Seed",
                _solarSystem.Seed.ToString());

            EditorGUILayout.LabelField(
                "Available Planets",
                _solarSystem.PlanetCount.ToString());

            EditorGUILayout.LabelField(
                "Requested Body ID",
                _bodyCoordinates.CelestialBodyID.ToString());

            GUILayout.Space(8f);

            if (!_hasSelectedPlanet)
            {
                EditorGUILayout.HelpBox(
                    "The requested body does not currently resolve to a planet. " +
                    "Moon and asteroid resolution will be added with the common " +
                    "celestial-body hierarchy.",
                    MessageType.Warning);

                return;
            }

            DrawPlanetData();
        }

        public void DrawCanvas(
            Rect canvasRect,
            CoordinatesData coordinates)
        {
            EditorGUI.DrawRect(
                canvasRect,
                GetPlanetBackgroundColor());

            if (!_hasGeneratedSystem)
            {
                DrawCenteredLabel(
                    canvasRect,
                    "System data has not been generated yet.",
                    EditorStyles.centeredGreyMiniLabel);

                return;
            }

            if (!_hasSelectedPlanet)
            {
                DrawCenteredLabel(
                    canvasRect,
                    "Requested celestial body is not currently a generated planet.",
                    EditorStyles.centeredGreyMiniLabel);

                return;
            }

            DrawPlanetPreview(canvasRect);
        }

        private void DrawPlanetData()
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField(
                "Resolved Type",
                _planet.Type.ToString());

            EditorGUILayout.LabelField(
                "Mass",
                $"{_planet.MassKg / EarthMassKg:F3} M⊕");

            EditorGUILayout.LabelField(
                "Radius",
                $"{_planet.RadiusMeters / EarthRadiusMeters:F3} R⊕");

            EditorGUILayout.LabelField(
                "Density",
                $"{_planet.DensityKgPerCubicMeter:F0} kg/m³");

            EditorGUILayout.LabelField(
                "Surface Gravity",
                $"{_planet.SurfaceGravityMetersPerSecondSquared:F2} m/s² " +
                $"({_planet.SurfaceGravityMetersPerSecondSquared / EarthGravityMetersPerSecondSquared:F2} g)");

            EditorGUILayout.LabelField(
                "Temperature",
                $"{_planet.SurfaceTemperatureKelvin:F0} K");

            EditorGUILayout.LabelField(
                "Orbit Radius",
                $"{_planet.Orbit.SemiMajorAxisMeters / AstronomicalUnitMeters:F3} AU");

            EditorGUILayout.LabelField(
                "Eccentricity",
                _planet.Orbit.Eccentricity.ToString("F3"));

            EditorGUILayout.LabelField(
                "Moons",
                _planet.MoonCount.ToString());

            EditorGUILayout.LabelField(
                "Rings",
                _planet.HasRings ? "Yes" : "No");

            GUILayout.Space(5f);

            EditorGUILayout.LabelField(
                "Atmosphere",
                _planet.HasAtmosphere ? "Yes" : "No");

            if (_planet.HasAtmosphere)
            {
                EditorGUILayout.LabelField(
                    "Atmosphere Pressure",
                    $"{_planet.AtmospherePressurePascals:F0} Pa " +
                    $"({_planet.AtmospherePressurePascals / EarthAtmospherePressurePascals:F2} atm)");
            }

            EditorGUILayout.LabelField(
                "Water Coverage",
                $"{_planet.WaterCoverage * 100.0:F0}%");

            EditorGUILayout.LabelField(
                "Ice Coverage",
                $"{_planet.IceCoverage * 100.0:F0}%");

            EditorGUILayout.LabelField(
                "Volcanic Activity",
                $"{_planet.VolcanicActivity * 100.0:F0}%");

            GUILayout.EndVertical();
        }

        private void DrawPlanetPreview(Rect canvasRect)
        {
            var center = canvasRect.center;

            var radius = Mathf.Clamp(
                Mathf.Min(canvasRect.width, canvasRect.height) * 0.22f,
                80f,
                220f);

            var planetColor = GetPlanetColor(_planet.Type);

            Handles.BeginGUI();

            Handles.color = planetColor;
            Handles.DrawSolidDisc(
                center,
                Vector3.forward,
                radius);

            DrawPlanetSurfaceHints(center, radius);

            if (_planet.HasRings)
            {
                Handles.color = new Color(
                    0.9f,
                    0.85f,
                    0.65f,
                    0.65f);

                Handles.DrawWireDisc(
                    center,
                    Vector3.forward,
                    radius * 1.28f);

                Handles.DrawWireDisc(
                    center,
                    Vector3.forward,
                    radius * 1.44f);
            }

            Handles.color = new Color(
                1f,
                1f,
                1f,
                0.35f);

            Handles.DrawWireDisc(
                center,
                Vector3.forward,
                radius);

            Handles.EndGUI();

            GUI.Label(
                new Rect(
                    canvasRect.x + 12f,
                    canvasRect.y + 12f,
                    420f,
                    22f),
                $"{_planet.Type} · {_planet.Orbit.SemiMajorAxisMeters / AstronomicalUnitMeters:F3} AU",
                EditorStyles.boldLabel);

            GUI.Label(
                new Rect(
                    canvasRect.x + 12f,
                    canvasRect.y + 34f,
                    420f,
                    20f),
                $"{_planet.SurfaceTemperatureKelvin:F0} K · " +
                $"{_planet.MassKg / EarthMassKg:F2} M⊕ · " +
                $"{_planet.MoonCount} moons",
                EditorStyles.miniLabel);
        }

        private void DrawPlanetSurfaceHints(
            Vector2 center,
            float radius)
        {
            Handles.BeginGUI();

            if (_planet.WaterCoverage > 0.05)
            {
                Handles.color = new Color(
                    0.15f,
                    0.45f,
                    0.95f,
                    Mathf.Clamp01((float)_planet.WaterCoverage));

                Handles.DrawSolidDisc(
                    center + new Vector2(-radius * 0.22f, radius * 0.1f),
                    Vector3.forward,
                    radius * 0.45f);
            }

            if (_planet.IceCoverage > 0.05)
            {
                Handles.color = new Color(
                    0.8f,
                    0.95f,
                    1f,
                    Mathf.Clamp01((float)_planet.IceCoverage));

                Handles.DrawSolidDisc(
                    center + new Vector2(radius * 0.12f, -radius * 0.25f),
                    Vector3.forward,
                    radius * 0.30f);
            }

            if (_planet.VolcanicActivity > 0.10)
            {
                Handles.color = new Color(
                    1f,
                    0.25f,
                    0.05f,
                    Mathf.Clamp01((float)_planet.VolcanicActivity));

                Handles.DrawSolidDisc(
                    center + new Vector2(radius * 0.26f, radius * 0.22f),
                    Vector3.forward,
                    radius * 0.12f);
            }

            Handles.EndGUI();
        }

        private Color GetPlanetBackgroundColor()
        {
            if (!_hasSelectedPlanet)
                return new Color(0.025f, 0.03f, 0.055f);

            return Color.Lerp(
                new Color(0.02f, 0.025f, 0.045f),
                GetPlanetColor(_planet.Type),
                0.10f);
        }

        private static Color GetPlanetColor(PlanetType planetType)
        {
            switch (planetType)
            {
                case PlanetType.Terrestrial:
                    return new Color(0.68f, 0.38f, 0.18f);

                case PlanetType.Ocean:
                    return new Color(0.12f, 0.46f, 0.92f);

                case PlanetType.GasGiant:
                    return new Color(0.88f, 0.63f, 0.28f);

                case PlanetType.IceGiant:
                    return new Color(0.35f, 0.82f, 0.96f);

                case PlanetType.DwarfPlanet:
                    return new Color(0.55f, 0.55f, 0.60f);

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