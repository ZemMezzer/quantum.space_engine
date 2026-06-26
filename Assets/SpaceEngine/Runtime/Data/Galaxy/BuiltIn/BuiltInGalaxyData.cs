using Unity.Mathematics;
using SpaceEngine.Runtime.Data.Galaxy;

namespace SpaceEngine.Runtime.Data.Galaxy.BuiltIn
{
    /// <summary>
    /// Shared immutable data base for the current built-in galaxy generators.
    /// Each concrete generated kind has its own data type so a renderer can
    /// recognise it without IDs, presets or engine-side type switches.
    /// </summary>
    public abstract class BuiltInGalaxyData : GalaxyData
    {
        protected BuiltInGalaxyData(
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
            : base(
                universeID,
                galaxyID,
                seed,
                universePositionLightYears,
                rotationRadians,
                radiusLightYears,
                coreRadiusLightYears,
                diskThicknessLightYears,
                massKg,
                baseSystemDensityPerCubicLightYear,
                metallicity,
                spiralArmCount,
                spiralArmTightness,
                barLengthLightYears,
                ellipticity,
                ringRadiusLightYears,
                ringWidthLightYears,
                irregularity)
        {
        }
    }

    public sealed class SpiralGalaxyData : BuiltInGalaxyData
    {
        public SpiralGalaxyData(long universeID, long galaxyID, ulong seed, double3 universePositionLightYears, double rotationRadians, double radiusLightYears, double coreRadiusLightYears, double diskThicknessLightYears, double massKg, double baseSystemDensityPerCubicLightYear, double metallicity, byte spiralArmCount, double spiralArmTightness, double barLengthLightYears, double ellipticity, double ringRadiusLightYears, double ringWidthLightYears, double irregularity)
            : base(universeID, galaxyID, seed, universePositionLightYears, rotationRadians, radiusLightYears, coreRadiusLightYears, diskThicknessLightYears, massKg, baseSystemDensityPerCubicLightYear, metallicity, spiralArmCount, spiralArmTightness, barLengthLightYears, ellipticity, ringRadiusLightYears, ringWidthLightYears, irregularity)
        {
        }
    }

    public sealed class BarredSpiralGalaxyData : BuiltInGalaxyData
    {
        public BarredSpiralGalaxyData(long universeID, long galaxyID, ulong seed, double3 universePositionLightYears, double rotationRadians, double radiusLightYears, double coreRadiusLightYears, double diskThicknessLightYears, double massKg, double baseSystemDensityPerCubicLightYear, double metallicity, byte spiralArmCount, double spiralArmTightness, double barLengthLightYears, double ellipticity, double ringRadiusLightYears, double ringWidthLightYears, double irregularity)
            : base(universeID, galaxyID, seed, universePositionLightYears, rotationRadians, radiusLightYears, coreRadiusLightYears, diskThicknessLightYears, massKg, baseSystemDensityPerCubicLightYear, metallicity, spiralArmCount, spiralArmTightness, barLengthLightYears, ellipticity, ringRadiusLightYears, ringWidthLightYears, irregularity)
        {
        }
    }

    public sealed class EllipticalGalaxyData : BuiltInGalaxyData
    {
        public EllipticalGalaxyData(long universeID, long galaxyID, ulong seed, double3 universePositionLightYears, double rotationRadians, double radiusLightYears, double coreRadiusLightYears, double diskThicknessLightYears, double massKg, double baseSystemDensityPerCubicLightYear, double metallicity, byte spiralArmCount, double spiralArmTightness, double barLengthLightYears, double ellipticity, double ringRadiusLightYears, double ringWidthLightYears, double irregularity)
            : base(universeID, galaxyID, seed, universePositionLightYears, rotationRadians, radiusLightYears, coreRadiusLightYears, diskThicknessLightYears, massKg, baseSystemDensityPerCubicLightYear, metallicity, spiralArmCount, spiralArmTightness, barLengthLightYears, ellipticity, ringRadiusLightYears, ringWidthLightYears, irregularity)
        {
        }
    }

    public sealed class LenticularGalaxyData : BuiltInGalaxyData
    {
        public LenticularGalaxyData(long universeID, long galaxyID, ulong seed, double3 universePositionLightYears, double rotationRadians, double radiusLightYears, double coreRadiusLightYears, double diskThicknessLightYears, double massKg, double baseSystemDensityPerCubicLightYear, double metallicity, byte spiralArmCount, double spiralArmTightness, double barLengthLightYears, double ellipticity, double ringRadiusLightYears, double ringWidthLightYears, double irregularity)
            : base(universeID, galaxyID, seed, universePositionLightYears, rotationRadians, radiusLightYears, coreRadiusLightYears, diskThicknessLightYears, massKg, baseSystemDensityPerCubicLightYear, metallicity, spiralArmCount, spiralArmTightness, barLengthLightYears, ellipticity, ringRadiusLightYears, ringWidthLightYears, irregularity)
        {
        }
    }

    public sealed class IrregularGalaxyData : BuiltInGalaxyData
    {
        public IrregularGalaxyData(long universeID, long galaxyID, ulong seed, double3 universePositionLightYears, double rotationRadians, double radiusLightYears, double coreRadiusLightYears, double diskThicknessLightYears, double massKg, double baseSystemDensityPerCubicLightYear, double metallicity, byte spiralArmCount, double spiralArmTightness, double barLengthLightYears, double ellipticity, double ringRadiusLightYears, double ringWidthLightYears, double irregularity)
            : base(universeID, galaxyID, seed, universePositionLightYears, rotationRadians, radiusLightYears, coreRadiusLightYears, diskThicknessLightYears, massKg, baseSystemDensityPerCubicLightYear, metallicity, spiralArmCount, spiralArmTightness, barLengthLightYears, ellipticity, ringRadiusLightYears, ringWidthLightYears, irregularity)
        {
        }
    }

    public sealed class RingGalaxyData : BuiltInGalaxyData
    {
        public RingGalaxyData(long universeID, long galaxyID, ulong seed, double3 universePositionLightYears, double rotationRadians, double radiusLightYears, double coreRadiusLightYears, double diskThicknessLightYears, double massKg, double baseSystemDensityPerCubicLightYear, double metallicity, byte spiralArmCount, double spiralArmTightness, double barLengthLightYears, double ellipticity, double ringRadiusLightYears, double ringWidthLightYears, double irregularity)
            : base(universeID, galaxyID, seed, universePositionLightYears, rotationRadians, radiusLightYears, coreRadiusLightYears, diskThicknessLightYears, massKg, baseSystemDensityPerCubicLightYear, metallicity, spiralArmCount, spiralArmTightness, barLengthLightYears, ellipticity, ringRadiusLightYears, ringWidthLightYears, irregularity)
        {
        }
    }

    public sealed class DwarfGalaxyData : BuiltInGalaxyData
    {
        public DwarfGalaxyData(long universeID, long galaxyID, ulong seed, double3 universePositionLightYears, double rotationRadians, double radiusLightYears, double coreRadiusLightYears, double diskThicknessLightYears, double massKg, double baseSystemDensityPerCubicLightYear, double metallicity, byte spiralArmCount, double spiralArmTightness, double barLengthLightYears, double ellipticity, double ringRadiusLightYears, double ringWidthLightYears, double irregularity)
            : base(universeID, galaxyID, seed, universePositionLightYears, rotationRadians, radiusLightYears, coreRadiusLightYears, diskThicknessLightYears, massKg, baseSystemDensityPerCubicLightYear, metallicity, spiralArmCount, spiralArmTightness, barLengthLightYears, ellipticity, ringRadiusLightYears, ringWidthLightYears, irregularity)
        {
        }
    }
}
