using System;
using System.Collections.Generic;
using System.Reflection;
using SpaceEngine.Runtime.Content;
using SpaceEngine.Runtime.Content.Entities;
using SpaceEngine.Runtime.Content.StellarObjects.Generation.Galaxies;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Generation.Galaxy;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace SpaceEngine.Editor.UniverseInspector
{
    /// <summary>
    /// Bounded deterministic search over generated systems in a galaxy sector
    /// neighbourhood. It is editor-only: runtime gameplay finds known objects
    /// by CoordinatesData + StellarObjectIndex and does not scan the universe.
    /// </summary>
    public sealed class UniverseInspectorEntitySearchTab : IUniverseInspectorTab
    {
        private const int MaximumSearchRadius = 8;
        private const int MaximumResults = 128;

        private readonly List<SearchResult> results = new();

        private StellarEntity entity;
        private int centerSectorX;
        private int centerSectorY;
        private int centerSectorZ;
        private int sectorRadius = 1;
        private int verticalSectorRadius;
        private bool requireAccretionDisk;
        private string status;

        private Action<long, long> selectObject;

        private readonly struct SearchResult
        {
            public readonly long SolarSystemID;
            public readonly int ObjectIndex;
            public readonly StellarObjectData Data;
            public readonly double3 PositionLightYears;

            public SearchResult(
                long solarSystemID,
                int objectIndex,
                StellarObjectData data,
                double3 positionLightYears)
            {
                SolarSystemID = solarSystemID;
                ObjectIndex = objectIndex;
                Data = data;
                PositionLightYears = positionLightYears;
            }
        }

        public void SetObjectSelectionCallback(Action<long, long> callback)
        {
            selectObject = callback;
        }

        public void Generate(
            SpaceEngineConfiguration configuration,
            CoordinatesData coordinates)
        {
            // Searches are explicit because even editor-only scans can be
            // expensive. Generation only refreshes the surrounding context.
            status = configuration == null
                ? "Assign a SpaceEngineConfiguration."
                : $"Choose an entity and search sectors in Galaxy {coordinates.GalaxyID}.";
        }

        public void DrawInspector(
            SpaceEngineConfiguration configuration,
            CoordinatesData coordinates)
        {
            GUILayout.Label("Entity Search", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Search is bounded to the selected galaxy-sector neighbourhood. " +
                "It regenerates systems deterministically and returns their " +
                "SolarSystemID and object index; no runtime object registry is required.",
                MessageType.Info);

            entity = (StellarEntity)EditorGUILayout.ObjectField(
                "Stellar Entity",
                entity,
                typeof(StellarEntity),
                false);

            centerSectorX = EditorGUILayout.IntField(
                "Center Sector X", centerSectorX);
            centerSectorY = EditorGUILayout.IntField(
                "Center Sector Y", centerSectorY);
            centerSectorZ = EditorGUILayout.IntField(
                "Center Sector Z", centerSectorZ);
            sectorRadius = EditorGUILayout.IntSlider(
                "Horizontal Radius",
                sectorRadius,
                0,
                MaximumSearchRadius);
            verticalSectorRadius = EditorGUILayout.IntSlider(
                "Vertical Radius",
                verticalSectorRadius,
                0,
                MaximumSearchRadius);

            requireAccretionDisk = EditorGUILayout.Toggle(
                "Require Accretion Disk",
                requireAccretionDisk);

            using (new EditorGUI.DisabledScope(entity == null))
            {
                if (GUILayout.Button("Search Generated Systems"))
                    Search(configuration, coordinates);
            }

            if (!string.IsNullOrWhiteSpace(status))
                EditorGUILayout.HelpBox(status, MessageType.None);

            if (results.Count == 0)
                return;

            GUILayout.Space(8.0f);
            GUILayout.Label($"Results ({results.Count})", EditorStyles.boldLabel);

            for (var index = 0; index < results.Count; index++)
            {
                var result = results[index];
                GUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(
                    $"{UniverseInspectorGeneration.GetDataName(result.Data)} · Object {result.ObjectIndex}");
                EditorGUILayout.LabelField(
                    "Solar System ID",
                    result.SolarSystemID.ToString());
                EditorGUILayout.LabelField(
                    "Galaxy Position",
                    $"({result.PositionLightYears.x:F2}, " +
                    $"{result.PositionLightYears.y:F2}, " +
                    $"{result.PositionLightYears.z:F2}) ly");
                EditorGUILayout.LabelField(
                    "Mass",
                    $"{result.Data.MassKg:E4} kg");

                if (GUILayout.Button("Open Object"))
                    selectObject?.Invoke(
                        result.SolarSystemID,
                        result.ObjectIndex);

                GUILayout.EndVertical();
            }
        }

        public void DrawCanvas(
            Rect canvasRect,
            SpaceEngineConfiguration configuration,
            CoordinatesData coordinates)
        {
            EditorGUI.DrawRect(canvasRect, new Color(0.012f, 0.016f, 0.03f));

            if (entity == null)
            {
                GUI.Label(
                    canvasRect,
                    "Choose a Stellar Entity to search.",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var color = entity.DebugColor;
            color.a = 1.0f;

            GUI.Label(
                new Rect(
                    canvasRect.xMin + 12.0f,
                    canvasRect.yMin + 10.0f,
                    740.0f,
                    22.0f),
                $"Search target: {entity.DisplayName} · {results.Count} result(s)",
                EditorStyles.whiteMiniLabel);

            if (results.Count == 0)
            {
                GUI.Label(
                    canvasRect,
                    "Run a bounded deterministic search from the sidebar.",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var center = canvasRect.center;
            var radius = Mathf.Min(canvasRect.width, canvasRect.height) *
                         0.38f;
            var maxDistance = 1.0;

            for (var index = 0; index < results.Count; index++)
            {
                var position = results[index].PositionLightYears;
                maxDistance = Math.Max(
                    maxDistance,
                    Math.Sqrt(position.x * position.x +
                              position.z * position.z));
            }

            for (var index = 0; index < results.Count; index++)
            {
                var position = results[index].PositionLightYears;
                var point = center + new Vector2(
                    (float)(position.x / maxDistance * radius),
                    (float)(-position.z / maxDistance * radius));

                EditorGUI.DrawRect(
                    new Rect(point.x - 3.0f, point.y - 3.0f, 6.0f, 6.0f),
                    color);
            }
        }

        private void Search(
            SpaceEngineConfiguration configuration,
            CoordinatesData coordinates)
        {
            results.Clear();

            if (entity == null)
            {
                status = "Choose a Stellar Entity.";
                return;
            }

            if (!UniverseInspectorGeneration.TryGenerateGalaxy(
                    configuration,
                    coordinates.UniverseID,
                    coordinates.GalaxyID,
                    out var galaxy,
                    out GalaxyGenerator generator,
                    out var error))
            {
                status = error;
                return;
            }

            var center = new int3(
                centerSectorX,
                centerSectorY,
                centerSectorZ);
            var horizontalRadius = Mathf.Clamp(
                sectorRadius,
                0,
                MaximumSearchRadius);
            var verticalRadius = Mathf.Clamp(
                verticalSectorRadius,
                0,
                MaximumSearchRadius);

            for (var z = -horizontalRadius;
                 z <= horizontalRadius && results.Count < MaximumResults;
                 z++)
            {
                for (var y = -verticalRadius;
                     y <= verticalRadius && results.Count < MaximumResults;
                     y++)
                {
                    for (var x = -horizontalRadius;
                         x <= horizontalRadius &&
                         results.Count < MaximumResults;
                         x++)
                    {
                        if (x * x + z * z >
                            horizontalRadius * horizontalRadius)
                        {
                            continue;
                        }

                        var sectorCoordinates = center + new int3(x, y, z);
                        if (!SolarSystemIDUtility
                                .IsSectorCoordinateInRange(
                                    sectorCoordinates))
                        {
                            continue;
                        }

                        if (!UniverseInspectorGeneration
                                .TryGenerateGalaxySector(
                                    generator,
                                    galaxy,
                                    sectorCoordinates,
                                    out var sector,
                                    out error))
                        {
                            status = error;
                            return;
                        }

                        SearchSector(
                            configuration,
                            coordinates,
                            sector,
                            ref error);
                        if (error != null)
                        {
                            status = error;
                            return;
                        }
                    }
                }
            }

            status = results.Count == 0
                ? $"No {entity.DisplayName} was generated in the searched sectors."
                : $"Found {results.Count} {entity.DisplayName} result(s).";
        }

        private void SearchSector(
            SpaceEngineConfiguration configuration,
            CoordinatesData baseCoordinates,
            GalaxySectorData sector,
            ref string error)
        {
            for (var systemIndex = 0;
                 systemIndex < sector.SolarSystems.Length &&
                 results.Count < MaximumResults;
                 systemIndex++)
            {
                var location = sector.SolarSystems[systemIndex];
                var systemCoordinates = new CoordinatesData(
                    baseCoordinates.UniverseID,
                    baseCoordinates.GalaxyID,
                    location.SolarSystemID);

                if (!UniverseInspectorGeneration.TryGenerateSolarSystem(
                        configuration,
                        systemCoordinates,
                        out var system,
                        out error))
                {
                    return;
                }

                var objects = system.StellarObjects;
                for (var objectIndex = 0;
                     objectIndex < objects.Length &&
                     results.Count < MaximumResults;
                     objectIndex++)
                {
                    var data = objects[objectIndex];
                    if (data?.Entity != entity)
                        continue;

                    if (requireAccretionDisk &&
                        !GetBooleanProperty(data, "HasAccretionDisk"))
                    {
                        continue;
                    }

                    results.Add(new SearchResult(
                        location.SolarSystemID,
                        objectIndex,
                        data,
                        location.GalaxyLocalPositionLightYears));
                }
            }
        }

        private static bool GetBooleanProperty(
            StellarObjectData data,
            string propertyName)
        {
            var property = data.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public);

            return property?.PropertyType == typeof(bool) &&
                   property.GetValue(data) is bool value &&
                   value;
        }
    }
}
