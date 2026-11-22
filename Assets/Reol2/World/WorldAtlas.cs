using System;
using UnityEngine;

namespace Reol2.World
{
    internal sealed class WorldAtlas
    {
        private const int NodeSpacing = 1 << 19;
        private const int NodesPerAxis = 2 * WorldDefinition.Extent / NodeSpacing + 1;
        private readonly float[] land = new float[NodesPerAxis * NodesPerAxis];

        internal WorldAtlas(WorldDefinition world)
        {
            for (int z = 0; z < NodesPerAxis; z++)
            for (int x = 0; x < NodesPerAxis; x++)
            {
                int wx = x * NodeSpacing - WorldDefinition.Extent;
                int wz = z * NodeSpacing - WorldDefinition.Extent;
                float islands = 0.61f * WorldNoise.Value(wx, wz, 22, world.SeedBits)
                              + 0.29f * WorldNoise.Value(wx, wz, 20, world.SeedBits + 157) - 0.15f;
                int margin = WorldDefinition.Extent - Math.Max(Math.Abs(wx), Math.Abs(wz));
                land[x + z * NodesPerAxis] = Mathf.Lerp(-0.72f, islands, WorldNoise.Ramp(margin / (float)NodeSpacing));
            }
        }

        internal float LandAt(int x, int z)
        {
            int offsetX = x + WorldDefinition.Extent;
            int offsetZ = z + WorldDefinition.Extent;
            int ix = Math.Min(offsetX / NodeSpacing, NodesPerAxis - 2);
            int iz = Math.Min(offsetZ / NodeSpacing, NodesPerAxis - 2);
            float u = (offsetX - ix * NodeSpacing) / (float)NodeSpacing;
            float v = (offsetZ - iz * NodeSpacing) / (float)NodeSpacing;
            int a = ix + iz * NodesPerAxis;
            return Mathf.LerpUnclamped(Mathf.LerpUnclamped(land[a], land[a + 1], u),
                Mathf.LerpUnclamped(land[a + NodesPerAxis], land[a + NodesPerAxis + 1], u), v);
        }
    }
}
