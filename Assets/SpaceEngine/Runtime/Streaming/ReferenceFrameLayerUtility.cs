using UnityEngine;

namespace SpaceEngine.Runtime.Streaming
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
            return TryGetSingleLayerIndex(
                layerMask,
                out var layerIndex)
                ? layerIndex
                : fallbackLayer;
        }

        public static bool AreDifferentSingleLayers(
            LayerMask first,
            LayerMask second,
            LayerMask third,
            LayerMask fourth)
        {
            if (!TryGetSingleLayerIndex(first, out var firstIndex) ||
                !TryGetSingleLayerIndex(second, out var secondIndex) ||
                !TryGetSingleLayerIndex(third, out var thirdIndex) ||
                !TryGetSingleLayerIndex(fourth, out var fourthIndex))
            {
                return false;
            }

            return firstIndex != secondIndex &&
                   firstIndex != thirdIndex &&
                   firstIndex != fourthIndex &&
                   secondIndex != thirdIndex &&
                   secondIndex != fourthIndex &&
                   thirdIndex != fourthIndex;
        }
    }
}
