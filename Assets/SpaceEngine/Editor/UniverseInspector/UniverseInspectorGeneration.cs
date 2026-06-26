using System;
using SpaceEngine.Runtime.Content;
using SpaceEngine.Runtime.Content.Entities;
using SpaceEngine.Runtime.Content.StellarObjects.Generation.Galaxies;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.Galaxy;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Generation.Coordinates;
using SpaceEngine.Runtime.Generation.SolarSystem;
using SpaceEngine.Runtime.Generation.Universe;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceEngine.Editor.UniverseInspector
{
    /// <summary>
    /// Editor-only bridge to the same public content contracts used at runtime.
    /// It contains no content selection policy: every selection remains inside
    /// the configured ScriptableObject generators and their GetWeight calls.
    /// </summary>
    internal static class UniverseInspectorGeneration
    {
        public static bool TryValidateConfiguration(
            SpaceEngineConfiguration configuration,
            out string error)
        {
            if (configuration == null)
            {
                error = "Assign a SpaceEngineConfiguration or a SpaceEngine with one assigned.";
                return false;
            }

            if (configuration.GalaxyGenerators.Count == 0)
            {
                error = "SpaceEngineConfiguration has no GalaxyGeneratorBinding entries.";
                return false;
            }

            if (!HasValidGalaxyBinding(configuration))
            {
                error = "SpaceEngineConfiguration has no valid Galaxy entity/generator binding.";
                return false;
            }

            if (configuration.SolarSystemGenerators.Count == 0)
            {
                error = "SpaceEngineConfiguration has no SolarSystemGenerator entries.";
                return false;
            }

            if (!HasValidStellarObjectBinding(configuration))
            {
                error = "SpaceEngineConfiguration has no valid Stellar Entity/Generator binding.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool HasValidGalaxyBinding(
            SpaceEngineConfiguration configuration)
        {
            for (var index = 0;
                 index < configuration.GalaxyGenerators.Count;
                 index++)
            {
                if (configuration.GalaxyGenerators[index]?.IsValid == true)
                    return true;
            }

            return false;
        }

        private static bool HasValidStellarObjectBinding(
            SpaceEngineConfiguration configuration)
        {
            for (var index = 0;
                 index < configuration.StellarObjectGenerators.Count;
                 index++)
            {
                if (configuration.StellarObjectGenerators[index]?.IsValid ==
                    true)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryGenerateUniverseSector(
            SpaceEngineConfiguration configuration,
            long universeID,
            int3 sectorCoordinates,
            out UniverseSectorData sector,
            out string error)
        {
            sector = default;

            if (!TryValidateConfiguration(configuration, out error))
                return false;

            try
            {
                sector = UniverseGeneration.GenerateSector(
                    configuration.GalaxyGenerators,
                    universeID,
                    sectorCoordinates);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public static bool TryGenerateGalaxy(
            SpaceEngineConfiguration configuration,
            long universeID,
            long galaxyID,
            out GalaxyData galaxy,
            out GalaxyGenerator generator,
            out string error)
        {
            galaxy = null;
            generator = null;

            if (!TryValidateConfiguration(configuration, out error))
                return false;

            try
            {
                var position = LogicalCoordinatesResolver
                    .ResolveGalaxyUniversePosition(universeID, galaxyID);

                var binding = UniverseGeneration.ResolveGalaxyBinding(
                    configuration.GalaxyGenerators,
                    universeID,
                    galaxyID,
                    position);

                generator = binding?.Generator;

                if (generator == null || binding.Entity == null)
                {
                    error = "No configured GalaxyGeneratorBinding returned a positive GetWeight for this location.";
                    return false;
                }

                galaxy = UniverseGeneration.GenerateGalaxy(
                    configuration.GalaxyGenerators,
                    universeID,
                    galaxyID,
                    position);

                return galaxy != null;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public static bool TryGenerateGalaxySector(
            GalaxyGenerator generator,
            GalaxyData galaxy,
            int3 sectorCoordinates,
            out GalaxySectorData sector,
            out string error)
        {
            sector = default;

            if (generator == null || galaxy == null)
            {
                error = "Galaxy data or its selected generator is unavailable.";
                return false;
            }

            try
            {
                sector = generator.GenerateSector(galaxy, sectorCoordinates);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public static bool TryGenerateSolarSystem(
            SpaceEngineConfiguration configuration,
            in CoordinatesData coordinates,
            out SolarSystemData solarSystem,
            out string error)
        {
            solarSystem = null;

            if (!TryValidateConfiguration(configuration, out error))
                return false;

            try
            {
                solarSystem = SolarSystemGeneration.Generate(
                    coordinates,
                    configuration.SolarSystemGenerators,
                    configuration.StellarObjectGenerators,
                    configuration.PlanetGenerators);

                if (solarSystem == null)
                {
                    error = "Solar-system generation returned null.";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public static string GetDataName(object data)
        {
            if (data is StellarObjectData stellarObject &&
                stellarObject.Entity != null)
            {
                return stellarObject.Entity.DisplayName;
            }

            if (data is GalaxyData galaxy && galaxy.Entity != null)
                return galaxy.Entity.DisplayName;

            if (data == null)
                return "None";

            var name = data.GetType().Name;
            return name.EndsWith("Data", StringComparison.Ordinal)
                ? name.Substring(0, name.Length - "Data".Length)
                : name;
        }

        public static Color GetDebugColor(StellarEntity entity, long fallback)
        {
            return entity != null
                ? entity.DebugColor
                : GetStableColor(fallback);
        }

        public static Color GetStableColor(long identifier, float saturation = 0.58f)
        {
            unchecked
            {
                var value = (ulong)identifier;
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;

                var hue = (value & 0xFFFFUL) / 65535.0f;
                return Color.HSVToRGB(hue, saturation, 1.0f);
            }
        }
    }
}
