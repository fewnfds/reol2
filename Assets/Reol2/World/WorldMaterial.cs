using System;
using UnityEngine;

namespace Reol2.World
{
    internal static class WorldMaterial
    {
        internal static float BandWeight(float spacing, int shift)
        {
            float span = 1 << shift;
            return 1f - WorldNoise.Ramp((spacing * 8f - span) / span);
        }

        internal static Color32 Sample(WorldPosition point, Vector3 normal, float ground, float spacing,
            ulong seed, bool large)
        {
            int x = (int)Math.Floor(point.X);
            int z = (int)Math.Floor(point.Z);
            float variation = Band(x, z, spacing, large ? 12 : 9, seed + 309) * 0.25f
                + Band(x, z, spacing, large ? 8 : 6, seed + 389) * 0.10f;
            return ColorAt((float)point.Y, normal, ground, spacing, 0.5f + variation, large);
        }

        internal static Color32 ColorAt(float height, Vector3 normal, float ground, float spacing, float shade, bool large)
        {
            float depth = ground - height;
            float rock = large ? WorldNoise.Ramp((depth - 0.5f) / 7.5f) : 0f;

            Color stone = Color.Lerp(new Color(0.34f, 0.39f, 0.39f), new Color(0.57f, 0.58f, 0.53f), shade);
            Color bedrock = rock > 0f ? WorldGeology.RockTint(depth, spacing, stone) : stone;
            if (rock >= 1f)
                return bedrock;

            Color earth = Color.Lerp(new Color(0.26f, 0.47f, 0.31f), new Color(0.51f, 0.63f, 0.36f), shade);
            float cover = WorldNoise.Ramp(Mathf.InverseLerp(0.38f, 0.84f, normal.y));
            cover *= Mathf.InverseLerp(-0.5f, 12f, height) *
                     (1f - Mathf.InverseLerp(large ? 2200f : 76f, large ? 3800f : 110f, height));
            float sand = (1f - Mathf.InverseLerp(1.5f, 10f, Mathf.Abs(height))) * Mathf.Clamp01(normal.y * 1.4f);
            Color land = Color.Lerp(Color.Lerp(stone, earth, cover), new Color(0.78f, 0.71f, 0.48f), sand);
            if (large)
            {
                float snow = Mathf.InverseLerp(4200f, 5800f, height) * Mathf.InverseLerp(0.32f, 0.72f, normal.y);
                land = Color.Lerp(land, new Color(0.83f, 0.88f, 0.91f), snow);
            }
            return Color.Lerp(land, bedrock, rock);
        }

        private static float Band(int x, int z, float spacing, int shift, ulong seed)
        {
            float weight = BandWeight(spacing, shift);
            return weight > 0f ? WorldNoise.Value(x, z, shift, seed) * weight : 0f;
        }

        internal readonly struct PageTint
        {
            private readonly TintBand broad;
            private readonly TintBand local;

            internal PageTint(int worldX, int worldZ, ulong seed, bool large)
            {
                broad = new TintBand(worldX, worldZ, large ? 12 : 9, seed + 309);
                local = new TintBand(worldX, worldZ, large ? 8 : 6, seed + 389);
            }

            internal float ShadeAt(Vector3 point)
            {
                int x = Mathf.FloorToInt(point.x);
                int z = Mathf.FloorToInt(point.z);
                return 0.5f + (broad.Sample(x, z) * 0.25f + local.Sample(x, z) * 0.10f);
            }
        }

        // Pages are aligned and smaller than every baked near-field noise cell.
        private readonly struct TintBand
        {
            private readonly int xOffset, zOffset;
            private readonly float inverseSpan;
            private readonly float a, b, c, d;

            internal TintBand(int worldX, int worldZ, int shift, ulong seed)
            {
                int span = 1 << shift;
                int cellX = worldX >> shift;
                int cellZ = worldZ >> shift;
                xOffset = worldX & (span - 1);
                zOffset = worldZ & (span - 1);
                inverseSpan = 1f / span;
                a = WorldNoise.Unit(WorldNoise.Hash(cellX, cellZ, seed));
                b = WorldNoise.Unit(WorldNoise.Hash(cellX + 1, cellZ, seed));
                c = WorldNoise.Unit(WorldNoise.Hash(cellX, cellZ + 1, seed));
                d = WorldNoise.Unit(WorldNoise.Hash(cellX + 1, cellZ + 1, seed));
            }

            internal float Sample(int x, int z)
            {
                float u = WorldNoise.Fade((xOffset + x) * inverseSpan);
                float v = WorldNoise.Fade((zOffset + z) * inverseSpan);
                return Mathf.LerpUnclamped(Mathf.LerpUnclamped(a, b, u), Mathf.LerpUnclamped(c, d, u), v) * 2f - 1f;
            }
        }

        internal static Color32 OreTint(Color32 rock, Mineral mineral) => Color32.Lerp(rock, mineral switch
        {
            Mineral.Salt => new Color32(162, 157, 147, 255),
            Mineral.Coal => new Color32(54, 58, 64, 255),
            Mineral.Iron => new Color32(148, 81, 54, 255),
            Mineral.Copper => new Color32(66, 131, 109, 255),
            Mineral.Sulfur => new Color32(161, 138, 66, 255),
            _ => rock
        }, 0.6f);
    }
}
