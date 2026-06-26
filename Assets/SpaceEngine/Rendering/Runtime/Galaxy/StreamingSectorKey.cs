using System;
using Unity.Mathematics;

namespace SpaceEngine.Rendering.Runtime.Galaxy
{
    internal readonly struct StreamingSectorKey :
        IEquatable<StreamingSectorKey>
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Z;

        public StreamingSectorKey(int3 coordinates)
        {
            X = coordinates.x;
            Y = coordinates.y;
            Z = coordinates.z;
        }

        public bool Equals(StreamingSectorKey other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is StreamingSectorKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = X;
                hash = hash * 397 ^ Y;
                hash = hash * 397 ^ Z;
                return hash;
            }
        }
    }
}
