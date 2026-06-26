using System;

namespace SpaceEngine.Runtime.Content.Generation
{
    internal static class StableIdentifierUtility
    {
        /// <summary>
        /// FNV-1a with a final avalanche. String.GetHashCode is intentionally
        /// not used because it is not stable between editor/runtime sessions.
        /// </summary>
        public static ulong Hash(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0UL;

            unchecked
            {
                const ulong offset = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;

                var hash = offset;
                for (var index = 0; index < value.Length; index++)
                {
                    hash ^= value[index];
                    hash *= prime;
                }

                hash ^= hash >> 30;
                hash *= 0xBF58476D1CE4E5B9UL;
                hash ^= hash >> 27;
                hash *= 0x94D049BB133111EBUL;
                hash ^= hash >> 31;
                return hash;
            }
        }
        public static ulong Mix(ulong value)
        {
            unchecked
            {
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return value;
            }
        }

    }
}
