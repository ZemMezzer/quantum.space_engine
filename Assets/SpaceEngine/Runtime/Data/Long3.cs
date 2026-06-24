using Unity.Mathematics;

namespace SpaceEngine.Runtime.Data
{
    public readonly struct Long3
    {
        public readonly long X;
        public readonly long Y;
        public readonly long Z;

        public Long3(long x, long y, long z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public Long3(double3 vector)
        {
            X = (long)vector.x;
            Y = (long)vector.y;
            Z = (long)vector.z;
        }
    }
}