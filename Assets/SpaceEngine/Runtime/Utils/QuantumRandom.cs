namespace SpaceEngine.Runtime.Utils
{
    public struct QuantumRandom
    {
        private ulong _state;

        public QuantumRandom(ulong seed) => _state = seed;

        private ulong NextULongInternal()
        {
            ulong z = (_state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong NextULong() => NextULongInternal();
        public bool NextBool() => (NextULongInternal() & 1UL) != 0;

        public double NextDouble()
        {
            return (NextULongInternal() >> 11) * (1.0 / (1UL << 53));
        }

        public double NextDouble(double min, double max)
        {
            return min >= max ? min : min + (max - min) * NextDouble();
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive >= maxExclusive)
                return minInclusive;

            uint range = (uint)(maxExclusive - minInclusive);
            ulong limit = ulong.MaxValue - (ulong.MaxValue % range);
            ulong value;
            do value = NextULongInternal(); while (value >= limit);
            return (int)(value % range) + minInclusive;
        }
    }
}
