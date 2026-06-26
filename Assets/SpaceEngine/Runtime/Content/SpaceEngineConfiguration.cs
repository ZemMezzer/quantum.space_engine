using System;
using System.Collections.Generic;
using SpaceEngine.Runtime.Content.Entities;
using SpaceEngine.Runtime.Content.StellarObjects.Generation;
using UnityEngine;

namespace SpaceEngine.Runtime.Content
{
    /// <summary>
    /// Authored references only. This asset never selects, creates or caches
    /// generated data. Every content entry pairs one generator with its
    /// StellarEntity identity; the fixed generation pipeline merely chooses
    /// the winning generator by weight and attaches that entity to its result.
    /// </summary>
    [CreateAssetMenu(
        fileName = "Space Engine Configuration",
        menuName = "Space Engine/Configuration")]
    public sealed class SpaceEngineConfiguration : ScriptableObject
    {
        [Header("Galaxy entities and generators")]
        [SerializeField] private GalaxyGeneratorBinding[] galaxyGenerators =
            Array.Empty<GalaxyGeneratorBinding>();

        [Header("Solar-system structure generators")]
        [SerializeField] private SolarSystemGenerator[] solarSystemGenerators =
            Array.Empty<SolarSystemGenerator>();

        [Header("Primary stellar-object entities and generators")]
        [SerializeField] private StellarObjectGeneratorBinding[]
            stellarObjectGenerators =
                Array.Empty<StellarObjectGeneratorBinding>();

        [Header("Orbiting-object entities and generators")]
        [SerializeField] private PlanetGeneratorBinding[] planetGenerators =
            Array.Empty<PlanetGeneratorBinding>();

        public IReadOnlyList<GalaxyGeneratorBinding> GalaxyGenerators =>
            galaxyGenerators ?? Array.Empty<GalaxyGeneratorBinding>();

        public IReadOnlyList<SolarSystemGenerator> SolarSystemGenerators =>
            solarSystemGenerators ?? Array.Empty<SolarSystemGenerator>();

        public IReadOnlyList<StellarObjectGeneratorBinding>
            StellarObjectGenerators =>
                stellarObjectGenerators ??
                Array.Empty<StellarObjectGeneratorBinding>();

        public IReadOnlyList<PlanetGeneratorBinding> PlanetGenerators =>
            planetGenerators ?? Array.Empty<PlanetGeneratorBinding>();
    }
}
