using UnityEngine;

namespace SpaceEngine.Runtime.Content.StellarObjects.Generation.Stars
{
    [CreateAssetMenu(
        fileName = "Pulsar Generator",
        menuName = "Space Engine/Stellar Objects/Stars/Generators/Pulsar")]
    public sealed class PulsarStellarObjectGenerator : CompactStellarObjectGeneratorBase
    {
        protected override float PrimaryWeight => 0.001f;
        protected override double MinimumRotationSeconds => 0.001;
        protected override double MaximumRotationSeconds => 2.0;
        protected override double MinimumTemperatureKelvin => 500_000.0;
        protected override double MaximumTemperatureKelvin => 5_000_000.0;
    }
}
