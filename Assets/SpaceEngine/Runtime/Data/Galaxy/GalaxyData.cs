using SpaceEngine.Runtime.Content.Entities;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Data.Galaxy
{
    /// <summary>
    /// Physical result returned by a galaxy generator.
    ///
    /// Entity is assigned once by the winning GalaxyGeneratorBinding. The
    /// engine uses only the shared spatial data below; renderer selection, UI
    /// and editor queries use Entity reference equality instead of morphology
    /// enums or concrete data-class switches.
    /// </summary>
    public class GalaxyData
    {
        public StellarEntity Entity { get; private set; }
        public long UniverseID { get; }
        public long GalaxyID { get; }
        public ulong Seed { get; }
        public double3 UniversePositionLightYears { get; }
        public double RotationRadians { get; }
        public double RadiusLightYears { get; }
        public double CoreRadiusLightYears { get; }
        public double DiskThicknessLightYears { get; }
        public double MassKg { get; }
        public double BaseSystemDensityPerCubicLightYear { get; }
        public double Metallicity { get; }

        // Shared generated geometric values. They are data, not a morphology
        // classification: concrete generators and renderers decide how to use
        // them without engine-side branching.
        public byte SpiralArmCount { get; }
        public double SpiralArmTightness { get; }
        public double BarLengthLightYears { get; }
        public double Ellipticity { get; }
        public double RingRadiusLightYears { get; }
        public double RingWidthLightYears { get; }
        public double Irregularity { get; }

        public GalaxyData(
            long universeID,
            long galaxyID,
            ulong seed,
            double3 universePositionLightYears,
            double rotationRadians,
            double radiusLightYears,
            double coreRadiusLightYears,
            double diskThicknessLightYears,
            double massKg,
            double baseSystemDensityPerCubicLightYear,
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
            UniversePositionLightYears = universePositionLightYears;
            RotationRadians = rotationRadians;
            RadiusLightYears = radiusLightYears;
            CoreRadiusLightYears = coreRadiusLightYears;
            DiskThicknessLightYears = diskThicknessLightYears;
            MassKg = massKg;
            BaseSystemDensityPerCubicLightYear =
                baseSystemDensityPerCubicLightYear;
            Metallicity = metallicity;
            SpiralArmCount = spiralArmCount;
            SpiralArmTightness = spiralArmTightness;
            BarLengthLightYears = barLengthLightYears;
            Ellipticity = ellipticity;
            RingRadiusLightYears = ringRadiusLightYears;
            RingWidthLightYears = ringWidthLightYears;
            Irregularity = irregularity;
        }

        public bool IsEntity(StellarEntity entity)
        {
            return Entity == entity;
        }

        public GalaxyData WithEntity(StellarEntity entity)
        {
            if (entity == null)
                return this;

            if (Entity != null && Entity != entity)
            {
                throw new System.InvalidOperationException(
                    "A generated GalaxyData instance cannot be rebound to a " +
                    "different StellarEntity.");
            }

            Entity = entity;
            return this;
        }
    }
}
