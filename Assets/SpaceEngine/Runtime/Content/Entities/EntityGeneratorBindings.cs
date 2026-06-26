using System;
using SpaceEngine.Runtime.Content.StellarObjects.Generation.Galaxies;
using SpaceEngine.Runtime.Content.StellarObjects.Generation.Planets;
using SpaceEngine.Runtime.Content.StellarObjects.Generation.Stars;
using UnityEngine;

namespace SpaceEngine.Runtime.Content.Entities
{
    /// <summary>
    /// Serialized pair stored by SpaceEngineConfiguration. The entity is
    /// attached to the generator result after successful generation.
    /// </summary>
    [Serializable]
    public sealed class GalaxyGeneratorBinding
    {
        [SerializeField] private StellarEntity entity;
        [SerializeField] private GalaxyGenerator generator;

        public StellarEntity Entity => entity;
        public GalaxyGenerator Generator => generator;
        public bool IsValid => entity != null && generator != null;
    }

    /// <summary>
    /// Serialized generator/entity pair for central or primary stellar-system
    /// objects. It carries no runtime state.
    /// </summary>
    [Serializable]
    public sealed class StellarObjectGeneratorBinding
    {
        [SerializeField] private StellarEntity entity;
        [SerializeField] private StellarObjectGenerator generator;

        public StellarEntity Entity => entity;
        public StellarObjectGenerator Generator => generator;
        public bool IsValid => entity != null && generator != null;
    }

    /// <summary>
    /// Serialized generator/entity pair for objects emitted into a planetary
    /// orbit by a SolarSystemGenerator. A "planet" here is only the current
    /// content list name; custom generators can return any StellarObjectData.
    /// </summary>
    [Serializable]
    public sealed class PlanetGeneratorBinding
    {
        [SerializeField] private StellarEntity entity;
        [SerializeField] private PlanetGenerator generator;

        public StellarEntity Entity => entity;
        public PlanetGenerator Generator => generator;
        public bool IsValid => entity != null && generator != null;
    }
}
