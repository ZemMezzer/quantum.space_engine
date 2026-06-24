using Unity.Mathematics;

namespace SpaceEngine.Runtime.Data.Galaxy
{
    /// <summary>
    /// Immutable physical and morphological description of one existing galaxy.
    /// All distances are expressed in light-years unless a field says otherwise.
    /// </summary>
    public readonly struct GalaxyData
    {
        public readonly ulong UniverseID;
        public readonly ulong GalaxyID;
        public readonly ulong Seed;
        public readonly GalaxyType Type;

        public readonly double3 UniversePositionLightYears;
        public readonly double RotationRadians;

        public readonly double RadiusLightYears;
        public readonly double CoreRadiusLightYears;
        public readonly double DiskThicknessLightYears;
        public readonly double MassKg;

        /// <summary>
        /// Mean generated stellar-system density at normalized density 1.
        /// Unit: systems per cubic light-year.
        /// </summary>
        public readonly double BaseSystemDensityPerCubicLightYear;

        public readonly double GasDensity;
        public readonly double Metallicity;

        public readonly byte SpiralArmCount;
        public readonly double SpiralArmTightness;
        public readonly double BarLengthLightYears;
        public readonly double Ellipticity;
        public readonly double RingRadiusLightYears;
        public readonly double RingWidthLightYears;
        public readonly double Irregularity;

        internal GalaxyData(
            ulong universeID,
            ulong galaxyID,
            ulong seed,
            GalaxyType type,
            double3 universePositionLightYears,
            double rotationRadians,
            double radiusLightYears,
            double coreRadiusLightYears,
            double diskThicknessLightYears,
            double massKg,
            double baseSystemDensityPerCubicLightYear,
            double gasDensity,
            double metallicity,
            byte spiralArmCount,
            double spiralArmTightness,
            double barLengthLightYears,
            double ellipticity,
            double ringRadiusLightYears,
            double ringWidthLightYears,
            double irregularity)
        {
            UniverseID = universeID;
            GalaxyID = galaxyID;
            Seed = seed;
            Type = type;
            UniversePositionLightYears = universePositionLightYears;
            RotationRadians = rotationRadians;
            RadiusLightYears = radiusLightYears;
            CoreRadiusLightYears = coreRadiusLightYears;
            DiskThicknessLightYears = diskThicknessLightYears;
            MassKg = massKg;
            BaseSystemDensityPerCubicLightYear = baseSystemDensityPerCubicLightYear;
            GasDensity = gasDensity;
            Metallicity = metallicity;
            SpiralArmCount = spiralArmCount;
            SpiralArmTightness = spiralArmTightness;
            BarLengthLightYears = barLengthLightYears;
            Ellipticity = ellipticity;
            RingRadiusLightYears = ringRadiusLightYears;
            RingWidthLightYears = ringWidthLightYears;
            Irregularity = irregularity;
        }
    }
}
