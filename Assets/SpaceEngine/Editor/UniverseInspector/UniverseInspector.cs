using SpaceEngine.Runtime.Content;
using RuntimeSpaceEngine = global::SpaceEngine.Runtime.Core.SpaceEngine;
using SpaceEngine.Runtime.Data;
using UnityEditor;
using UnityEngine;

namespace SpaceEngine.Editor.UniverseInspector
{
    internal interface IUniverseInspectorTab
    {
        void Generate(
            SpaceEngineConfiguration configuration,
            CoordinatesData coordinates);

        void DrawInspector(
            SpaceEngineConfiguration configuration,
            CoordinatesData coordinates);

        void DrawCanvas(
            Rect canvasRect,
            SpaceEngineConfiguration configuration,
            CoordinatesData coordinates);
    }

    /// <summary>
    /// Inspects generated hierarchy data through the configured public content
    /// contracts. It never chooses concrete galaxy or stellar-object types.
    /// </summary>
    public sealed class UniverseInspector : EditorWindow
    {
        private const float SidebarWidth = 330.0f;

        [SerializeField] private RuntimeSpaceEngine spaceEngine;
        [SerializeField] private SpaceEngineConfiguration configurationOverride;

        private UniverseInspectorUniverseTab universeTab;
        private readonly UniverseInspectorGalaxyTab galaxyTab = new();
        private readonly UniverseInspectorSolarSystemTab solarSystemTab = new();
        private readonly UniverseInspectorStellarObjectTab stellarObjectTab = new();
        private readonly UniverseInspectorEntitySearchTab entitySearchTab = new();

        private int selectedTabIndex;
        private long universeID = 1;
        private long galaxyID = 1;
        private long solarSystemID = 1;
        private long stellarObjectIndex;

        private SpaceEngineConfiguration lastConfiguration;
        private CoordinatesData lastGeneratedCoordinates;
        private long lastGeneratedStellarObjectIndex;
        private int lastGeneratedTabIndex = -1;
        private bool hasGeneratedCurrentView;

        [MenuItem("Space Engine/Universe Inspector")]
        public static void Open()
        {
            var window = GetWindow<UniverseInspector>();
            window.titleContent = new GUIContent("Universe Inspector");
            window.minSize = new Vector2(980.0f, 640.0f);
            window.Show();
        }

        private void OnEnable()
        {
            universeTab ??= new UniverseInspectorUniverseTab(
                SelectGalaxyFromUniverseMap);

            galaxyTab.SetSolarSystemSelectionCallback(
                SelectSolarSystemFromGalaxyMap);

            solarSystemTab.SetStellarObjectSelectionCallback(
                SelectStellarObjectFromSystemMap);

            entitySearchTab.SetObjectSelectionCallback(
                SelectObjectFromEntitySearch);

            hasGeneratedCurrentView = false;
            lastGeneratedTabIndex = -1;
        }

        private void OnGUI()
        {
            if (universeTab == null)
                OnEnable();

            var configuration = DrawSidebar();
            DrawCanvas(configuration);
        }

        private SpaceEngineConfiguration DrawSidebar()
        {
            var sidebarRect = new Rect(
                0.0f,
                0.0f,
                SidebarWidth,
                position.height);

            EditorGUI.DrawRect(sidebarRect, new Color(0.11f, 0.12f, 0.15f));
            GUILayout.BeginArea(sidebarRect);

            GUILayout.Space(10.0f);
            GUILayout.Label("Universe Inspector", EditorStyles.boldLabel);
            GUILayout.Space(8.0f);

            DrawConfigurationFields();
            var configuration = GetConfiguration();

            GUILayout.Space(10.0f);
            selectedTabIndex = GUILayout.Toolbar(
                selectedTabIndex,
                new[] { "Universe", "Galaxy", "System", "Object", "Find" });

            GUILayout.Space(12.0f);
            GUILayout.Label("Coordinates", EditorStyles.boldLabel);

            universeID = DrawLongField("Universe ID", universeID);
            galaxyID = DrawLongField("Galaxy ID", galaxyID);
            solarSystemID = DrawLongField("Solar System ID", solarSystemID);
            stellarObjectIndex = DrawLongField(
                "Stellar Object Index",
                stellarObjectIndex < 0L ? 0L : stellarObjectIndex);

            GUILayout.Space(10.0f);

            if (!UniverseInspectorGeneration.TryValidateConfiguration(
                    configuration,
                    out var configurationError))
            {
                EditorGUILayout.HelpBox(configurationError, MessageType.Warning);
            }
            else
            {
                var coordinates = GetCoordinates();
                EnsureCurrentViewGenerated(configuration, coordinates);
                GetSelectedTab().DrawInspector(configuration, coordinates);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.HelpBox(
                "The inspector uses the same configured generator assets as SpaceEngine. " +
                "Galaxy and object choices remain inside their GetWeight implementations; " +
                "this window only displays returned generic data.",
                MessageType.Info);

            GUILayout.EndArea();
            return configuration;
        }

        private void DrawCanvas(SpaceEngineConfiguration configuration)
        {
            var canvasRect = new Rect(
                SidebarWidth,
                0.0f,
                Mathf.Max(1.0f, position.width - SidebarWidth),
                position.height);

            EditorGUI.DrawRect(canvasRect, new Color(0.025f, 0.03f, 0.055f));

            if (!UniverseInspectorGeneration.TryValidateConfiguration(
                    configuration,
                    out var configurationError))
            {
                DrawCenteredLabel(canvasRect, configurationError);
                return;
            }

            var coordinates = GetCoordinates();
            EnsureCurrentViewGenerated(configuration, coordinates);
            GetSelectedTab().DrawCanvas(canvasRect, configuration, coordinates);
        }

        private void DrawConfigurationFields()
        {
            EditorGUI.BeginChangeCheck();

            spaceEngine = (RuntimeSpaceEngine)EditorGUILayout.ObjectField(
                "Space Engine",
                spaceEngine,
                typeof(RuntimeSpaceEngine),
                true);

            if (spaceEngine == null)
            {
                configurationOverride =
                    (SpaceEngineConfiguration)EditorGUILayout.ObjectField(
                        "Configuration",
                        configurationOverride,
                        typeof(SpaceEngineConfiguration),
                        false);
            }
            else
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(
                        "Configuration",
                        GetConfigurationFromSpaceEngine(),
                        typeof(SpaceEngineConfiguration),
                        false);
                }
            }

            if (EditorGUI.EndChangeCheck())
                hasGeneratedCurrentView = false;
        }

        private SpaceEngineConfiguration GetConfiguration()
        {
            return spaceEngine == null
                ? configurationOverride
                : GetConfigurationFromSpaceEngine();
        }

        private SpaceEngineConfiguration GetConfigurationFromSpaceEngine()
        {
            if (spaceEngine == null)
                return null;

            var serializedObject = new SerializedObject(spaceEngine);
            return serializedObject.FindProperty("configuration")
                ?.objectReferenceValue as SpaceEngineConfiguration;
        }

        private void EnsureCurrentViewGenerated(
            SpaceEngineConfiguration configuration,
            CoordinatesData coordinates)
        {
            var isObjectTab = selectedTabIndex == 3;
            var selectionChanged =
                !hasGeneratedCurrentView ||
                lastConfiguration != configuration ||
                lastGeneratedTabIndex != selectedTabIndex ||
                lastGeneratedCoordinates != coordinates ||
                (isObjectTab &&
                 lastGeneratedStellarObjectIndex != stellarObjectIndex);

            if (!selectionChanged)
                return;

            if (isObjectTab)
                stellarObjectTab.SetObjectIndex(stellarObjectIndex);

            GetSelectedTab().Generate(configuration, coordinates);

            lastConfiguration = configuration;
            lastGeneratedCoordinates = coordinates;
            lastGeneratedStellarObjectIndex = stellarObjectIndex;
            lastGeneratedTabIndex = selectedTabIndex;
            hasGeneratedCurrentView = true;
            Repaint();
        }

        private void SelectGalaxyFromUniverseMap(long selectedGalaxyID)
        {
            galaxyID = selectedGalaxyID;
            selectedTabIndex = 1;
            hasGeneratedCurrentView = false;
            Repaint();
        }

        private void SelectSolarSystemFromGalaxyMap(long selectedSolarSystemID)
        {
            solarSystemID = selectedSolarSystemID;
            selectedTabIndex = 2;
            hasGeneratedCurrentView = false;
            Repaint();
        }

        private void SelectStellarObjectFromSystemMap(long selectedObjectIndex)
        {
            stellarObjectIndex = selectedObjectIndex < 0L ? 0L : selectedObjectIndex;
            selectedTabIndex = 3;
            hasGeneratedCurrentView = false;
            Repaint();
        }

        private void SelectObjectFromEntitySearch(
            long selectedSolarSystemID,
            long selectedObjectIndex)
        {
            solarSystemID = selectedSolarSystemID;
            stellarObjectIndex = selectedObjectIndex < 0L
                ? 0L
                : selectedObjectIndex;
            selectedTabIndex = 3;
            hasGeneratedCurrentView = false;
            Repaint();
        }

        private CoordinatesData GetCoordinates()
        {
            return new CoordinatesData(universeID, galaxyID, solarSystemID);
        }

        private IUniverseInspectorTab GetSelectedTab()
        {
            switch (selectedTabIndex)
            {
                case 1:
                    return galaxyTab;
                case 2:
                    return solarSystemTab;
                case 3:
                    return stellarObjectTab;
                case 4:
                    return entitySearchTab;
                default:
                    return universeTab;
            }
        }

        private static long DrawLongField(string label, long value)
        {
            var text = EditorGUILayout.TextField(label, value.ToString());
            return long.TryParse(text, out var parsed) ? parsed : value;
        }

        private static void DrawCenteredLabel(Rect rect, string text)
        {
            GUI.Label(rect, text, EditorStyles.centeredGreyMiniLabel);
        }
    }
}
