using System.Collections.Generic;
using SpaceEngine.Runtime.Content.Generation;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Utils;
using UnityEngine;

namespace SpaceEngine.Runtime.Content.StellarObjects.Generation.SolarSystems
{
    [CreateAssetMenu(
        fileName = "Single Stellar Solar System Generator",
        menuName = "Space Engine/Stellar Objects/Solar Systems/Generators/Single Stellar")]
    public sealed class SingleStellarSolarSystemGenerator
        : PlanetarySolarSystemGeneratorBase
    {
        [SerializeField, Range(0.0f, 1.0f)]
        private float relativeWeight = 0.85f;

        public override float GetWeight(
            in SolarSystemGenerationContext context)
        {
            // The central solar-system address is still only a coordinate
            // convention. The engine never marks this system as special.
            return context.Coordinates.SolarSystemID == 0L
                ? 1.0f
                : context.GetSelectionWeight(relativeWeight);
        }

        public override SolarSystemData Generate(
            in SolarSystemGenerationContext context,
            ref QuantumRandom random)
        {
            var primary = GenerateStellarObject(
                context,
                0,
                default,
                ref random);

            if (primary == null)
                return null;

            var objects = new List<StellarObjectData>()
            {
                primary
            };

            // This is authored built-in system logic, not an engine
            // assumption about what primary object types exist.
            if (primary.MassKg > 0.0 &&
                primary.LuminosityWatts > 0.0000001)
            {
                var innerOrbitAu = GetInnerPlanetOrbitAu(
                    primary.LuminosityWatts,
                    primary.RadiusMeters);
                var outerOrbitAu = GetOuterPlanetOrbitAu(
                    primary.LuminosityWatts,
                    primary.MassKg,
                    innerOrbitAu);

                GeneratePlanetaryBodies(
                    context,
                    objects,
                    new[] { primary },
                    primary.MassKg,
                    primary.LuminosityWatts,
                    innerOrbitAu,
                    outerOrbitAu,
                    ref random);
            }

            return new SolarSystemData(
                context.SystemSeed,
                objects.ToArray());
        }
    }
}
