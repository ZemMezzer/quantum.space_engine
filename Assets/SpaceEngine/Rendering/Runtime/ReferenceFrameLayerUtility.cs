using UnityEngine;

namespace SpaceEngine.Rendering.Runtime
{
    internal static class ReferenceFrameLayerUtility
    {
        public static bool TryGetSingleLayerIndex(
            LayerMask layerMask,
            out int layerIndex)
        {
            var value = layerMask.value;
            if (value == 0 || (value & (value - 1)) != 0)
            {
                layerIndex = 0;
                return false;
            }

            for (var index = 0; index < 32; index++)
            {
                if ((value & (1 << index)) == 0)
                    continue;

                layerIndex = index;
                return true;
            }

            layerIndex = 0;
            return false;
        }

        public static int GetSingleLayerIndexOrDefault(
            LayerMask layerMask,
            int fallbackLayer = 0)
        {
            return TryGetSingleLayerIndex(layerMask, out var index)
                ? index
                : fallbackLayer;
        }
    }
}
