using System;
using System.Collections.Generic;
using SpaceEngine.Runtime.Content.Generation;
using SpaceEngine.Runtime.Data;
using SpaceEngine.Runtime.Data.SolarSystem;
using SpaceEngine.Runtime.Utils;

namespace SpaceEngine.Runtime.Content.StellarObjects.Generation.SolarSystems
{
    /// <summary>
    /// Shared authored policy for built-in systems that have planetary orbits.
    /// It lives at the solar-system level: object generators never decide how
    /// many planets a system receives.
    /// </summary>
    public abstract class PlanetarySolarSystemGeneratorBase : SolarSystemGenerator
    {
        protected void GeneratePlanetaryBodies(
            in SolarSystemGenerationContext context,
            List<StellarObjectData> objects,
            IReadOnlyList<StellarObjectData> centralObjects,
            double centralMassKg,
            double centralLuminosityWatts,
            double minimumOrbitAu,
            double maximumOrbitAu,
            ref QuantumRandom random)
        {
            if (objects == null ||
                centralObjects == null ||
                centralMassKg <= 0.0 ||
                centralLuminosityWatts <= 0.0000001 ||
                minimumOrbitAu <= 0.0 ||
                maximumOrbitAu <= minimumOrbitAu)
            {
                return;
            }

            var requestedCount = random.NextInt(3, 11);
            var orbitAu = minimumOrbitAu;

            for (var slot = 0;
                 slot < requestedCount;
                 slot++)
            {
                orbitAu *= random.NextDouble(1.35, 2.05);
                if (orbitAu > maximumOrbitAu)
                    break;

                if (random.NextDouble() < 0.12)
                    continue;

                var orbit = new OrbitData(
                    orbitAu * StellarObjectGenerationUtility
                        .AstronomicalUnitMeters,
                    random.NextDouble(0.0, 0.18),
                    random.NextDouble(0.0, Math.PI * 7.0 / 180.0),
                    random.NextDouble(0.0, Math.PI * 2.0),
                    random.NextDouble(0.0, Math.PI * 2.0),
                    random.NextDouble(0.0, Math.PI * 2.0),
                    0.0);

                var planet = GeneratePlanet(
                    context,
                    centralObjects,
                    objects.Count,
                    slot,
                    centralMassKg,
                    centralLuminosityWatts,
                    orbit,
                    ref random);

                if (planet != null)
                    objects.Add(planet);
            }
        }

        protected static double GetInnerPlanetOrbitAu(
            double luminosityWatts,
            double maximumCentralRadiusMeters)
        {
            var luminositySolar = luminosityWatts /
                                  SolarConstants.SOLAR_LUMINOSITY_WATTS;
            var thermalFloor = Math.Max(
                0.05,
                0.12 * Math.Sqrt(Math.Max(0.0, luminositySolar)));
            var radiusFloor = Math.Max(
                0.001,
                maximumCentralRadiusMeters /
                StellarObjectGenerationUtility.AstronomicalUnitMeters *
                8.0);

            return Math.Max(thermalFloor, radiusFloor);
        }

        protected static double GetOuterPlanetOrbitAu(
            double luminosityWatts,
            double massKg,
            double minimumOrbitAu)
        {
            var luminositySolar = luminosityWatts /
                                  SolarConstants.SOLAR_LUMINOSITY_WATTS;
            var massSolar = massKg / SolarConstants.SOLAR_MASS_KG;
            var natural = Math.Max(
                8.0,
                35.0 *
                Math.Sqrt(Math.Max(0.0, luminositySolar)) *
                Math.Sqrt(Math.Max(0.0, massSolar)));

            return Math.Max(natural, minimumOrbitAu * 16.0);
        }
    }
}
