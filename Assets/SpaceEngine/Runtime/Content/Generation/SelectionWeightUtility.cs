using System;
using Unity.Mathematics;

namespace SpaceEngine.Runtime.Content.Generation
{
    public static class SelectionWeightUtility
    {
        private const double UNIT_SAMPLE_DIVISOR = 16_777_217.0;

        public static float GetDeterministicSelectionWeight(
            ulong seed,
            ulong candidateID,
            float relativeWeight)
        {
            relativeWeight = math.saturate(relativeWeight);

            if (relativeWeight <= 0.0f)
                return 0.0f;

            var mixed = StableIdentifierUtility.Mix(seed ^ candidateID);
            var unitSample = ((mixed & 0x00FFFFFFUL) + 1.0) /
                             UNIT_SAMPLE_DIVISOR;
            var cost = -Math.Log(unitSample) / relativeWeight;
            return (float)(1.0 / (1.0 + cost));
        }
    }
}