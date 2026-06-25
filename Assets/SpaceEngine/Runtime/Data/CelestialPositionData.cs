using SpaceEngine.Runtime.Data.Galaxy;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Data
{
    /// <summary>
    /// A fully resolved location that an anchor can occupy.
    ///
    /// Coordinates identify the generated solar system. Positions are then
    /// expressed in its barycentric local frame, in metres. GalaxyLocalPosition
    /// is retained for UI, maps and renderers that need a larger-scale frame.
    /// </summary>
    public readonly struct CelestialPositionData
    {
        public readonly CoordinatesData Coordinates;
        public readonly CelestialPositionKind Kind;
        public readonly CelestialBodyCoordinatesData BodyCoordinates;
        public readonly bool HasBodyCoordinates;

        /// <summary>
        /// Position of the target solar-system barycentre in the galaxy frame.
        /// Unit: light-years.
        /// </summary>
        public readonly double3 GalaxyLocalPositionLightYears;

        /// <summary>
        /// Position relative to the target solar-system barycentre.
        /// Unit: metres.
        /// </summary>
        public readonly double3 SolarSystemLocalPositionMeters;

        /// <summary>
        /// Physical radius of the resolved target body. Zero for a system
        /// barycentre or an unresolved body type.
        /// </summary>
        public readonly double BodyRadiusMeters;

        public bool IsValid => Kind != CelestialPositionKind.None;

        public bool IsCelestialBody =>
            Kind == CelestialPositionKind.CelestialBody;

        public CelestialPositionData(
            CoordinatesData coordinates,
            double3 galaxyLocalPositionLightYears,
            double3 solarSystemLocalPositionMeters)
        {
            Coordinates = coordinates;
            Kind = CelestialPositionKind.SolarSystem;
            BodyCoordinates = default;
            HasBodyCoordinates = false;
            GalaxyLocalPositionLightYears = galaxyLocalPositionLightYears;
            SolarSystemLocalPositionMeters = solarSystemLocalPositionMeters;
            BodyRadiusMeters = 0.0;
        }

        public CelestialPositionData(
            CelestialBodyCoordinatesData bodyCoordinates,
            double3 galaxyLocalPositionLightYears,
            double3 solarSystemLocalPositionMeters,
            double bodyRadiusMeters)
        {
            Coordinates = bodyCoordinates.SolarSystemCoordinates;
            Kind = CelestialPositionKind.CelestialBody;
            BodyCoordinates = bodyCoordinates;
            HasBodyCoordinates = true;
            GalaxyLocalPositionLightYears = galaxyLocalPositionLightYears;
            SolarSystemLocalPositionMeters = solarSystemLocalPositionMeters;
            BodyRadiusMeters = bodyRadiusMeters;
        }

        public CelestialPositionData WithSolarSystemLocalPosition(
            double3 solarSystemLocalPositionMeters)
        {
            return HasBodyCoordinates
                ? new CelestialPositionData(
                    BodyCoordinates,
                    GalaxyLocalPositionLightYears,
                    solarSystemLocalPositionMeters,
                    BodyRadiusMeters)
                : new CelestialPositionData(
                    Coordinates,
                    GalaxyLocalPositionLightYears,
                    solarSystemLocalPositionMeters);
        }

        public static CelestialPositionData FromSolarSystem(
            CoordinatesData coordinates,
            in SolarSystemLocationData location,
            double3 solarSystemLocalPositionMeters)
        {
            return new CelestialPositionData(
                coordinates,
                location.GalaxyLocalPositionLightYears,
                solarSystemLocalPositionMeters);
        }
    }

    public enum CelestialPositionKind : byte
    {
        None = 0,
        SolarSystem = 1,
        CelestialBody = 2
    }
}
