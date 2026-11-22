using UnityEngine;

namespace Reol2.World
{
    internal static class WorldNoise
    {
        internal static ulong Hash(int x, int z, ulong seed)
        {
            unchecked
            {
                ulong key = ((ulong)(uint)x << 32) | (uint)z;
                ulong value = key + seed + 0x9e3779b97f4a7c15UL;
                value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
                value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
                return value ^ (value >> 31);
            }
        }

        internal static float Value(int x, int z, int shift, ulong seed)
        {
            int cellX = x >> shift;
            int cellZ = z >> shift;
            int span = 1 << shift;
            float u = Fade((x & (span - 1)) / (float)span);
            float v = Fade((z & (span - 1)) / (float)span);
            float a = Mathf.LerpUnclamped(Unit(Hash(cellX, cellZ, seed)), Unit(Hash(cellX + 1, cellZ, seed)), u);
            float b = Mathf.LerpUnclamped(Unit(Hash(cellX, cellZ + 1, seed)), Unit(Hash(cellX + 1, cellZ + 1, seed)), u);
            return Mathf.LerpUnclamped(a, b, v) * 2f - 1f;
        }

        internal static float Unit(ulong random) => (random >> 40) * (1f / 16777216f);

        internal static float Fade(float t) => t * t * (3f - 2f * t);

        internal static float Ramp(float t) => Fade(Mathf.Clamp01(t));
    }
}
