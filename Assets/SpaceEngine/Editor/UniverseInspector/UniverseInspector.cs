using SpaceEngine.Runtime.Data;
using UnityEditor;
using UnityEngine;

namespace SpaceEngine.Editor.UniverseInspector
{
    internal interface IUniverseInspectorTab
    {
        void Generate(CoordinatesData coordinates);

        void DrawInspector(CoordinatesData coordinates);

        void DrawCanvas(
            Rect canvasRect,
            CoordinatesData coordinates);
    }

    /// <summary>
    /// Editor tool for inspecting generated universe layers by logical IDs.
    /// Sectors are resolved internally and never form part of user-facing coordinates.
    /// </summary>
    public sealed class UniverseInspector : EditorWindow
    {
        private const float SidebarWidth = 300f;

        private UniverseInspectorUniverseTab _universeTab;
        private readonly UniverseInspectorGalaxyTab _galaxyTab = new();
        private readonly UniverseInspectorSolarSystemTab _solarSystemTab = new();
        private readonly UniverseInspectorPlanetTab _planetTab = new();

        private int _selectedTabIndex;

        private ulong _universeID = 1;
        private ulong _galaxyID = 1;
        private ulong _solarSystemID = 1;
        private ulong _celestialBodyID;

        private CoordinatesData _lastGeneratedCoordinates;
        private CelestialBodyCoordinatesData _lastGeneratedBodyCoordinates;

        private int _lastGeneratedTabIndex = -1;
        private bool _hasGeneratedCurrentView;

        [MenuItem("Space Engine/Universe Inspector")]
        public static void Open()
        {
            var window = GetWindow<UniverseInspector>();

            window.titleContent = new GUIContent("Universe Inspector");
            window.minSize = new Vector2(960f, 620f);
            window.Show();
        }

        private void OnEnable()
        {
            _universeTab ??= new UniverseInspectorUniverseTab(
                SelectGalaxyFromUniverseMap);

            _galaxyTab.SetSolarSystemSelectionCallback(
                SelectSolarSystemFromGalaxyMap);

            _hasGeneratedCurrentView = false;
            _lastGeneratedTabIndex = -1;
        }

        private void OnGUI()
        {
            if (_universeTab == null)
                OnEnable();

            DrawSidebar();
            DrawCanvas();
        }

        private void DrawSidebar()
        {
            var sidebarRect = new Rect(
                0f,
                0f,
                SidebarWidth,
                position.height);

            EditorGUI.DrawRect(
                sidebarRect,
                new Color(0.11f, 0.12f, 0.15f));

            GUILayout.BeginArea(sidebarRect);

            GUILayout.Space(10f);

            GUILayout.Label(
                "Universe Inspector",
                EditorStyles.boldLabel);

            GUILayout.Space(8f);

            _selectedTabIndex = GUILayout.Toolbar(
                _selectedTabIndex,
                new[]
                {
                    "Universe",
                    "Galaxy",
                    "System",
                    "Planet"
                });

            GUILayout.Space(12f);

            GUILayout.Label(
                "Coordinates",
                EditorStyles.boldLabel);

            _universeID = DrawULongField(
                "Universe ID",
                _universeID);

            _galaxyID = DrawULongField(
                "Galaxy ID",
                _galaxyID);

            _solarSystemID = DrawULongField(
                "Solar System ID",
                _solarSystemID);

            _celestialBodyID = DrawULongField(
                "Celestial Body ID",
                _celestialBodyID);

            var coordinates = GetCoordinates();

            var bodyCoordinates = GetCelestialBodyCoordinates(
                coordinates);

            EnsureCurrentViewGenerated(
                coordinates,
                bodyCoordinates);

            GUILayout.Space(12f);

            GetSelectedTab().DrawInspector(coordinates);

            GUILayout.FlexibleSpace();

            EditorGUILayout.HelpBox(
                "Universe ID, Galaxy ID and Solar System ID form the " +
                "logical address of an existing system. Internal sectors are " +
                "used only for generation and streaming.",
                MessageType.Info);

            GUILayout.EndArea();
        }

        private void DrawCanvas()
        {
            var canvasRect = new Rect(
                SidebarWidth,
                0f,
                position.width - SidebarWidth,
                position.height);

            EditorGUI.DrawRect(
                canvasRect,
                new Color(0.025f, 0.03f, 0.055f));

            var coordinates = GetCoordinates();

            var bodyCoordinates = GetCelestialBodyCoordinates(
                coordinates);

            EnsureCurrentViewGenerated(
                coordinates,
                bodyCoordinates);

            GetSelectedTab().DrawCanvas(
                canvasRect,
                coordinates);
        }

        private void EnsureCurrentViewGenerated(
            CoordinatesData coordinates,
            CelestialBodyCoordinatesData bodyCoordinates)
        {
            var tabChanged =
                _selectedTabIndex != _lastGeneratedTabIndex;

            var systemCoordinatesChanged =
                !_hasGeneratedCurrentView ||
                _lastGeneratedCoordinates != coordinates;

            var bodyCoordinatesChanged =
                _selectedTabIndex == 3 &&
                (!_hasGeneratedCurrentView ||
                 _lastGeneratedBodyCoordinates != bodyCoordinates);

            if (!tabChanged &&
                !systemCoordinatesChanged &&
                !bodyCoordinatesChanged)
            {
                return;
            }

            if (_selectedTabIndex == 3)
            {
                _planetTab.SetCelestialBodyCoordinates(
                    bodyCoordinates);
            }

            GetSelectedTab().Generate(coordinates);

            _lastGeneratedCoordinates = coordinates;
            _lastGeneratedBodyCoordinates = bodyCoordinates;
            _lastGeneratedTabIndex = _selectedTabIndex;
            _hasGeneratedCurrentView = true;

            Repaint();
        }

        private void SelectGalaxyFromUniverseMap(ulong galaxyID)
        {
            _galaxyID = galaxyID;

            _selectedTabIndex = 1;
            _hasGeneratedCurrentView = false;

            Repaint();
        }

        private void SelectSolarSystemFromGalaxyMap(
            ulong solarSystemID)
        {
            _solarSystemID = solarSystemID;

            _selectedTabIndex = 2;
            _hasGeneratedCurrentView = false;

            Repaint();
        }

        private CoordinatesData GetCoordinates()
        {
            return new CoordinatesData(
                _universeID,
                _galaxyID,
                _solarSystemID);
        }

        private CelestialBodyCoordinatesData GetCelestialBodyCoordinates(
            CoordinatesData coordinates)
        {
            return new CelestialBodyCoordinatesData(
                coordinates,
                _celestialBodyID);
        }

        private IUniverseInspectorTab GetSelectedTab()
        {
            switch (_selectedTabIndex)
            {
                case 0:
                    return _universeTab;

                case 1:
                    return _galaxyTab;

                case 2:
                    return _solarSystemTab;

                case 3:
                    return _planetTab;

                default:
                    return _universeTab;
            }
        }

        private static ulong DrawULongField(
            string label,
            ulong value)
        {
            var text = EditorGUILayout.TextField(
                label,
                value.ToString());

            return ulong.TryParse(
                text,
                out var parsedValue)
                ? parsedValue
                : value;
        }
    }
}