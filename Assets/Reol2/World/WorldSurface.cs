using System;
using UnityEngine;

namespace Reol2.World
{
    internal sealed class WorldSurface
    {
        internal const float HighestTerrain = 9140f;
        internal const float DeepestSeabed = -11200f;
        internal const float LakeLevel = 237f;
        internal const int BasinEast = 31500;
        internal const int BasinNorth = -115000;
        internal const int BasinWidth = 26300;
        internal const int BasinLength = 15900;
        internal const int RiverHead = 72000;
        internal const int RiverEnd = -110000;
        private const int Step = ContinentField.ChunkSize;
        private const int CacheMask = (1 << 15) - 1;
        private readonly struct HeightNode
        {
            internal readonly ulong Key;
            internal readonly float Value;

            internal HeightNode(ulong key, float value)
            {
                Key = key;
                Value = value;
            }
        }

        private readonly HeightNode[] nodes = new HeightNode[CacheMask + 1];
        private readonly WorldDefinition world;
        private readonly WorldAtlas atlas;
        internal Vector2Int Center => world.ContinentCenter;

        internal WorldSurface(WorldDefinition world)
        {
            this.world = world;
            atlas = new WorldAtlas(world);
        }

        internal float HeightAt(double x, double z)
        {
            int ix = Math.Clamp((int)Math.Floor(x / Step) * Step, -WorldDefinition.Extent, WorldDefinition.Extent - Step);
            int iz = Math.Clamp((int)Math.Floor(z / Step) * Step, -WorldDefinition.Extent, WorldDefinition.Extent - Step);
            float u = Mathf.Clamp01((float)((x - ix) / Step));
            float v = Mathf.Clamp01((float)((z - iz) / Step));
            return Interpolate(Node(ix, iz), Node(ix + Step, iz), Node(ix, iz + Step), Node(ix + Step, iz + Step), u, v);
        }

        internal static float Interpolate(float a, float b, float c, float d, float u, float v) => u + v <= 1f
            ? a + (b - a) * u + (c - a) * v
            : d + (c - d) * (1f - u) + (b - d) * (1f - v);

        internal float Node(int x, int z)
        {
            x = Math.Clamp(x, -WorldDefinition.Extent, WorldDefinition.Extent);
            z = Math.Clamp(z, -WorldDefinition.Extent, WorldDefinition.Extent);
            ulong key = (1UL << 63) | ((ulong)(x + WorldDefinition.Extent) << 28) | (uint)(z + WorldDefinition.Extent);
            int slot = (int)(WorldNoise.Hash(x, z, 0) & CacheMask);
            ref HeightNode cached = ref nodes[slot];
            if (cached.Key == key)
                return cached.Value;
            float height = Generate(x, z);
            cached = new HeightNode(key, height);
            return height;
        }

        private float Generate(int x, int z)
        {
            ulong seed = world.SeedBits;
            int east = x - world.ContinentCenter.x;
            int north = z - world.ContinentCenter.y;
            float px = east / 320000f;
            float pz = north / 398000f;
            float radius = Mathf.Sqrt(px * px + pz * pz);
            float headlands = WorldNoise.Value(x, z, 15, seed + 17);
            float mainland = 1f - radius + 0.19f * headlands + 0.05f * WorldNoise.Value(x, z, 12, seed + 19);
            float land = Mathf.Lerp(mainland, atlas.LandAt(x, z) + 0.17f * headlands,
                WorldNoise.Ramp((radius - 1.32f) / 0.65f));
            if (land <= 0f)
            {
                float shelf = WorldNoise.Ramp(-land / 0.1f);
                float basin = WorldNoise.Ramp((-land - 0.085f) / 0.38f);
                float trench = WorldNoise.Ramp((WorldNoise.Value(x, z, 17, seed + 43) - 0.40f) / 0.49f);
                return -shelf * (72f + basin * (3100f + 8000f * trench));
            }

            float inland = WorldNoise.Ramp(land / 0.23f);
            float hills = WorldNoise.Value(x, z, 10, seed + 67);
            float plateau = WorldNoise.Ramp((WorldNoise.Value(x, z, 14, seed + 79) + 0.18f) / 0.72f);
            float ridgeCenter = RidgeAt(north, seed);
            float chain = (1f - WorldNoise.Ramp(Mathf.Abs(east - ridgeCenter) / 44000f))
                          * (1f - WorldNoise.Ramp((Mathf.Abs(north) - 88500f) / 47000f));
            float ridge = 1f - Mathf.Abs(WorldNoise.Value(x, z, 13, seed + 83));
            float remote = WorldNoise.Ramp((radius - 1.3f) / 0.75f);
            float regionalRelief = remote > 0 ? WorldNoise.Ramp((WorldNoise.Value(x, z, 18, seed + 87) - 0.08f) / 0.76f) : 0f;
            float peaks = 690f * plateau + chain * (2300f + ridge * ridge * 5300f)
                + remote * regionalRelief * regionalRelief * ridge * ridge * 5600f;
            float height = land * 360f + inland * (peaks + (hills + 1f) * 38f
                + WorldNoise.Value(x, z, 7, seed + 91) * 3f);
            if (north < RiverHead + 8 && north > BasinNorth - BasinLength && east > -6000 && east < 69000)
                height = ShapeRiverAndBasin(east, north, height);
            return Mathf.Min(HighestTerrain, height);
        }

        internal static float RidgeAt(int north, ulong seed) =>
            -66000f + 28000f * WorldNoise.Value(north, 310, 16, seed + 193);

        internal Vector3 RiverAt(int north)
        {
            ulong seed = world.SeedBits;
            float riverEast = 31500f + 10400f * WorldNoise.Value(north, 810, 14, seed + 197)
                            + 760f * WorldNoise.Value(north, 810, 11, seed + 199);
            float progress = Mathf.Clamp01((RiverHead - north) / (float)(RiverHead - RiverEnd));
            float fade = WorldNoise.Ramp(progress * 13f);
            float water = Mathf.Lerp(530f, LakeLevel, progress);
            float width = (108f + progress * 185f) * fade;
            return new Vector3(riverEast, water, width);
        }

        internal float WaterAt(double worldX, double worldZ)
        {
            double east = worldX - Center.x;
            double north = worldZ - Center.y;
            float lake = LakeRadius((float)east, (float)north) <= 0.66f ? LakeLevel : 0f;
            if (north < RiverEnd || north > RiverHead || east < 16000 || east > 47000)
                return lake;
            Vector3 river = RiverAt((int)Math.Floor(north));
            return Math.Abs(east - river.x) <= river.z ? Math.Max(lake, river.y) : lake;
        }

        internal WorldPosition FindDeepSea()
        {
            const int spacing = 1 << 17;
            int bestX = -WorldDefinition.Extent + spacing;
            int bestZ = bestX;
            float lowest = 0f;
            for (int i = 1; i <= 160; i++)
            {
                int x = -WorldDefinition.Extent + i * spacing;
                int z = -WorldDefinition.Extent + spacing;
                float sample = Node(x, z);
                if (sample < lowest)
                {
                    lowest = sample;
                    bestX = x;
                    bestZ = z;
                    if (lowest < -10800f)
                        break;
                }
            }
            return new WorldPosition(bestX + 0.5, -1.35, bestZ + 0.5);
        }

        private float ShapeRiverAndBasin(int east, int north, float height)
        {
            // Both sides of any grid line read the same trunk center and downstream profile.
            if (north >= RiverEnd && north <= RiverHead)
            {
                Vector3 river = RiverAt(north);
                float channel = Mathf.Abs(east - river.x);
                if (channel < 9200f)
                {
                    float bed = river.y - 7.5f;
                    float riverbank = WorldNoise.Ramp((channel - river.z * 0.7f) / (river.z * 0.3f + 60f));
                    float valley = WorldNoise.Ramp((channel - river.z - 80f) / (9200f - river.z - 80f));
                    float fade = WorldNoise.Ramp((RiverHead - north) / 10000f) * WorldNoise.Ramp((north - RiverEnd) / 5000f);
                    height = Mathf.Lerp(height, Mathf.Lerp(bed + riverbank * 20f, height, valley), fade);
                }
            }

            float basin = LakeRadius(east, north);
            if (basin < 1f)
                height = Mathf.Lerp(229f + basin * 13f, height, WorldNoise.Ramp((basin - 0.28f) / 0.72f));
            return height;
        }

        private static float LakeRadius(float east, float north)
        {
            float x = (east - BasinEast) / BasinWidth;
            float z = (north - BasinNorth) / BasinLength;
            return Mathf.Sqrt(x * x + z * z);
        }
    }
}
