using System;
using System.Collections.Generic;
using SpaceEngine.Runtime.Content.Generation;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Utils;
using UnityEngine;

namespace SpaceEngine.Runtime.Content.StellarObjects.Generation.SolarSystems
{
    [CreateAssetMenu(
        fileName = "Binary Stellar Solar System Generator",
        menuName = "Space Engine/Stellar Objects/Solar Systems/Generators/Binary Stellar")]
    public sealed class BinaryStellarSolarSystemGenerator
        : PlanetarySolarSystemGeneratorBase
    {
        [SerializeField, Range(0.0f, 1.0f)]
        private float relativeWeight = 0.22f;

        [SerializeField, Min(0.01f)]
        private double minimumSeparationAu = 0.08;

        [SerializeField, Min(0.02f)]
        private double maximumSeparationAu = 8.0;

        public override float GetWeight(
            in SolarSystemGenerationContext context)
        {
            // System 0 is reserved only by content weights: the single-system
            // generator will win and its primary object generator can choose
            // the galactic black hole. No special core flag is involved.
            if (context.Coordinates.SolarSystemID == 0L)
                return 0.0f;

            return context.GetSelectionWeight(relativeWeight);
        }

        public override SolarSystemData Generate(
            in SolarSystemGenerationContext context,
            ref QuantumRandom random)
        {
            var first = GenerateStellarObject(
                context,
                0,
                default,
                ref random);
            var second = GenerateStellarObject(
                context,
                1,
                default,
                ref random);

            if (first == null || second == null)
                return null;

            var totalMassKg = first.MassKg + second.MassKg;
            if (totalMassKg <= 0.0)
                return null;

            var separationAu = random.NextDouble(
                Math.Max(0.001, minimumSeparationAu),
                Math.Max(minimumSeparationAu + 0.001, maximumSeparationAu));
            var separationMeters = separationAu *
                                   StellarObjectGenerationUtility
                                       .AstronomicalUnitMeters;
            var eccentricity = random.NextDouble(0.0, 0.35);
            var inclination = random.NextDouble(
                0.0,
                Math.PI * 8.0 / 180.0);
            var node = random.NextDouble(0.0, Math.PI * 2.0);
            var periapsis = random.NextDouble(0.0, Math.PI * 2.0);
            var phase = random.NextDouble(0.0, Math.PI * 2.0);

            var firstAxis = separationMeters * second.MassKg / totalMassKg;
            var secondAxis = separationMeters * first.MassKg / totalMassKg;

            first = first.WithOrbit(new OrbitData(
                firstAxis,
                eccentricity,
                inclination,
                periapsis,
                node,
                phase,
                0.0));

            second = second.WithOrbit(new OrbitData(
                secondAxis,
                eccentricity,
                inclination,
                periapsis,
                node,
                phase + Math.PI,
                0.0));

            var objects = new List<StellarObjectData>()
            {
                first,
                second
            };

            var luminosityWatts = first.LuminosityWatts +
                                  second.LuminosityWatts;
            if (luminosityWatts > 0.0000001)
            {
                var maximumRadius = Math.Max(
                    first.RadiusMeters,
                    second.RadiusMeters);
                var innerOrbitAu = Math.Max(
                    separationAu * 3.0,
                    GetInnerPlanetOrbitAu(
                        luminosityWatts,
                        maximumRadius));
                var outerOrbitAu = GetOuterPlanetOrbitAu(
                    luminosityWatts,
                    totalMassKg,
                    innerOrbitAu);

                GeneratePlanetaryBodies(
                    context,
                    objects,
                    new[] { first, second },
                    totalMassKg,
                    luminosityWatts,
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
