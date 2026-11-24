using System;
using UnityEngine;

namespace Reol2.World
{
    internal interface IWorldSurfaceData
    {
        WorldPosition Spawn { get; }
        WorldPosition SeaEntry { get; }
        bool TryRead(int x, int z, out float ground, out float water);
        WaterFeatures FeaturesIn(int x, int z, int width);
        bool NearSea(double x, double z);
    }

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
        private readonly struct SurfaceNode
        {
            internal readonly ulong Key;
            internal readonly float Ground, Water;

            internal SurfaceNode(ulong key, float ground, float water)
            {
                Key = key;
                Ground = ground;
                Water = water;
            }
        }

        private readonly SurfaceNode[] nodes = new SurfaceNode[CacheMask + 1];
        private readonly WorldDefinition world;
        private readonly WorldAtlas atlas;
        private readonly IWorldSurfaceData data;
        internal Vector2Int Center { get; }
        internal RegionalLandforms Landforms { get; }
        internal bool HasWaterTiles => Landforms != null || data != null;

        internal WorldSurface(WorldDefinition world, RegionalLandforms landforms = null, IWorldSurfaceData data = null)
        {
            this.world = world;
            this.data = data;
            Landforms = landforms;
            Center = world.ContinentCenter;
            if (landforms == null && data == null)
                atlas = new WorldAtlas(world);
            if (landforms != null)
                Center = new Vector2Int(Center.x & -Step, Center.y & -Step);
        }

        internal WorldPosition Place(System.Numerics.Vector2 point, double height = 0d) =>
            new(Center.x + (double)point.X, height, Center.y + (double)point.Y);

        private System.Numerics.Vector2 RegionPoint(double x, double z) => new((float)(x - Center.x), (float)(z - Center.y));

        internal WaterFeatures WaterFeaturesIn(int x, int z, int width) =>
            Landforms == null ? data.FeaturesIn(x, z, width) : Landforms.WaterFeaturesIn(RegionPoint(x, z), width);

        internal bool RiverIntersects(int x, int z, int width) => Landforms == null
            ? (data.FeaturesIn(x, z, width) & WaterFeatures.River) != 0
            : Landforms.Rivers.IntersectsChannel(RegionPoint(x + width * 0.5, z + width * 0.5), width * 0.707107f);

        internal bool ValleyIntersects(int x, int z, int width) => Landforms == null
            ? RiverIntersects(x, z, width)
            : Landforms.Rivers.IntersectsValley(RegionPoint(x + width * 0.5, z + width * 0.5), width * 0.707107f);

        internal bool TryRiverSpine(int x0, int z0, int x1, int z1, out System.Numerics.Vector2 local,
            out float halfWidth, float margin = 0f)
        {
            local = default;
            halfWidth = 0f;
            return Landforms != null && Landforms.Rivers.TrySpine(RegionPoint(x0, z0), RegionPoint(x1, z1), out local, out halfWidth, margin);
        }

        internal bool NearConnectedSea(double x, double z) => Landforms == null
            ? data?.NearSea(x, z) ?? true : Landforms.NearConnectedSea(RegionPoint(x, z));

        internal float RegionalWaterNode(int x, int z, float ground)
        {
            SurfaceNode node = GetNode(x, z);
            if (node.Ground == ground)
                return node.Water;
            if (data != null && data.TryRead(x, z, out _, out float water))
                return water;
            return Landforms?.WaterAt(RegionPoint(x, z), ground) ?? 0f;
        }

        internal void SampleNode(int x, int z, out float ground, out float water)
        {
            SurfaceNode node = GetNode(x, z);
            ground = node.Ground;
            water = node.Water;
        }

        internal float RiverWaterAt(double x, double z, float ground) =>
            Landforms.Rivers.WaterAt(RegionPoint(x, z), ground, out _);

        internal RegionalClimate? GeneratedClimateAt(WorldPosition point)
        {
            if (Landforms == null)
                return null;
            var local = RegionPoint(point.X, point.Z);
            return Landforms.Contains(local) ? Landforms.ClimateAt(local) : null;
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

        internal float Node(int x, int z) => GetNode(x, z).Ground;

        private SurfaceNode GetNode(int x, int z)
        {
            x = Math.Clamp(x, -WorldDefinition.Extent, WorldDefinition.Extent);
            z = Math.Clamp(z, -WorldDefinition.Extent, WorldDefinition.Extent);
            ulong key = (1UL << 63) | ((ulong)(x + WorldDefinition.Extent) << 28) | (uint)(z + WorldDefinition.Extent);
            int slot = (int)(WorldNoise.Hash(x, z, 0) & CacheMask);
            ref SurfaceNode cached = ref nodes[slot];
            if (cached.Key == key)
                return cached;
            if (data != null && data.TryRead(x, z, out float ground, out float water))
                cached = new SurfaceNode(key, ground, water);
            else if (Landforms != null)
            {
                Landforms.Sample(RegionPoint(x, z), out ground, out water);
                cached = new SurfaceNode(key, ground, water);
            }
            else
                cached = new SurfaceNode(key, Generate(x, z), 0f);
            return cached;
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
            if (HasWaterTiles)
                return RegionalWaterAt(worldX, worldZ);

            double east = worldX - Center.x;
            double north = worldZ - Center.y;
            float lake = LakeRadius((float)east, (float)north) <= 0.66f ? LakeLevel : 0f;
            if (north < RiverEnd || north > RiverHead || east < 16000 || east > 47000)
                return lake;
            Vector3 river = RiverAt((int)Math.Floor(north));
            return Math.Abs(east - river.x) <= river.z ? Math.Max(lake, river.y) : lake;
        }

        private float RegionalWaterAt(double x, double z)
        {
            if (data == null && !Landforms.Contains(RegionPoint(x, z)))
                return 0f;
            int ix = (int)Math.Floor(x / Step) * Step, iz = (int)Math.Floor(z / Step) * Step;
            float u = (float)((x - ix) / Step), v = (float)((z - iz) / Step);
            float b = GetNode(ix + Step, iz).Water;
            float c = GetNode(ix, iz + Step).Water;
            if (u + v <= 1f)
            {
                float a = GetNode(ix, iz).Water;
                return a + (b - a) * u + (c - a) * v;
            }
            float d = GetNode(ix + Step, iz + Step).Water;
            return d + (c - d) * (1f - u) + (b - d) * (1f - v);
        }

        internal WorldPosition FindDeepSea()
        {
            if (Landforms != null)
                return Place(new System.Numerics.Vector2(-Landforms.Size * 0.5f - 2000f, 0f), -1.35);
            if (data != null)
                return data.SeaEntry;

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
