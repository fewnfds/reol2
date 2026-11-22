using System;
using UnityEngine;

namespace Reol2.World
{
    internal sealed class WorldDefinition
    {
        internal const int Extent = 1 << 26;
        internal const int MinY = -32768;
        internal const int MaxY = 32768;
        internal const int GeneratorVersion = 1;

        internal long Seed { get; }
        internal ulong SeedBits => unchecked((ulong)Seed);
        internal Vector2Int ContinentCenter { get; }

        internal WorldDefinition(long seed)
        {
            Seed = seed;
            ulong variation = WorldNoise.Hash(0, 0, SeedBits);
            ContinentCenter = new Vector2Int((int)(variation & 65535) - 32768,
                                            (int)(variation >> 32 & 65535) - 32768);
        }

        internal static bool Contains(WorldPosition p) => p.X >= -Extent && p.X < Extent
            && p.Z >= -Extent && p.Z < Extent && p.Y >= MinY && p.Y < MaxY;

        internal static bool ContainsPage(Vector3Int page) =>
            page.x >= -Extent / ContinentField.ChunkSize && page.x < Extent / ContinentField.ChunkSize
            && page.z >= -Extent / ContinentField.ChunkSize && page.z < Extent / ContinentField.ChunkSize
            && page.y >= MinY / ContinentField.ChunkSize && page.y < MaxY / ContinentField.ChunkSize;

        internal static Vector3Int ClampSample(Vector3Int sample) => new(
            Math.Clamp(sample.x, -2 * Extent, 2 * Extent), Math.Clamp(sample.y, 2 * MinY, 2 * MaxY),
            Math.Clamp(sample.z, -2 * Extent, 2 * Extent));
    }
}
