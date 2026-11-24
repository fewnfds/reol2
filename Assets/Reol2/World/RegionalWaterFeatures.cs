using System;
using System.Collections.Generic;
using System.Numerics;

namespace Reol2.World
{
    [Flags]
    internal enum WaterFeatures : byte { None = 0, Coast = 1, River = 2, Lake = 4 }

    internal sealed class RegionalWaterFeatures
    {
        private const int BinWidth = RegionalRivers.BucketWidth;
        private const int Spacing = RegionalLandforms.DrainageSpacing;
        private readonly int half, side, bins;
        private readonly byte[][] levels;
        private readonly bool[] sea;

        internal RegionalWaterFeatures(RegionalLandforms region, float[] ground)
        {
            half = region.Size / 2;
            side = region.Side;
            bins = region.Size / BinWidth;
            sea = FindConnectedSea(ground);
            int levelCount = 1;
            for (int width = bins; width > 1; width >>= 1)
                levelCount++;
            levels = new byte[levelCount][];
            var first = new byte[bins * bins];
            levels[0] = first;
            for (int z = 0; z < bins; z++)
            for (int x = 0; x < bins; x++)
            {
                int index = 2 * (x + z * side);
                float low = float.PositiveInfinity, high = float.NegativeInfinity;
                for (int dz = 0; dz <= 2; dz++)
                for (int dx = 0; dx <= 2; dx++)
                {
                    float height = ground[index + dx + dz * side];
                    low = MathF.Min(low, height);
                    high = MathF.Max(high, height);
                }
                Vector2 start = new(x * BinWidth - half, z * BinWidth - half);
                Vector2 end = start + new Vector2(BinWidth);
                WaterFeatures value = WaterFeatures.None;
                if (low < 12f && high > -12f)
                    value |= WaterFeatures.Coast;
                if (region.Rivers.CandidatesAt((start + end) * 0.5f) != 0)
                    value |= WaterFeatures.River;
                if (region.HasLake && low < region.LakeLevel + 15f && start.X <= region.LakeMax.X
                    && end.X >= region.LakeMin.X && start.Y <= region.LakeMax.Y && end.Y >= region.LakeMin.Y)
                    value |= WaterFeatures.Lake;
                first[x + z * bins] = (byte)value;
            }

            for (int level = 1, width = bins / 2; level < levels.Length; level++, width /= 2)
            {
                byte[] source = levels[level - 1], reduced = new byte[width * width];
                for (int z = 0; z < width; z++)
                for (int x = 0; x < width; x++)
                {
                    int a = x * 2 + z * width * 4, b = a + width * 2;
                    reduced[x + z * width] = (byte)(source[a] | source[a + 1] | source[b] | source[b + 1]);
                }
                levels[level] = reduced;
            }
        }

        internal WaterFeatures Query(Vector2 start, int width)
        {
            Vector2 end = start + new Vector2(width);
            if (end.X < -half || start.X > half || end.Y < -half || start.Y > half)
                return WaterFeatures.None;
            int level = 0;
            for (int spacing = BinWidth; spacing < width && level + 1 < levels.Length; spacing *= 2)
                level++;
            int step = BinWidth << level, count = bins >> level;
            int x0 = Math.Clamp((int)((start.X + half) / step), 0, count - 1);
            int x1 = Math.Clamp((int)((end.X + half) / step), 0, count - 1);
            int z0 = Math.Clamp((int)((start.Y + half) / step), 0, count - 1) * count;
            int z1 = Math.Clamp((int)((end.Y + half) / step), 0, count - 1) * count;
            byte[] cells = levels[level];
            return (WaterFeatures)(cells[x0 + z0] | cells[x1 + z0] | cells[x0 + z1] | cells[x1 + z1]);
        }

        internal bool NearConnectedSea(Vector2 point)
        {
            if (MathF.Max(MathF.Abs(point.X), MathF.Abs(point.Y)) >= half)
                return true;
            int x = Math.Min((int)((point.X + half) / Spacing), side - 2);
            int z = Math.Min((int)((point.Y + half) / Spacing), side - 2);
            int i = x + z * side;
            return sea[i] || sea[i + 1] || sea[i + side] || sea[i + side + 1];
        }

        private bool[] FindConnectedSea(float[] ground)
        {
            var connected = new bool[ground.Length];
            var queue = new Queue<int>();
            int edge = side - 1;
            for (int i = 0; i < side; i++)
            {
                Visit(i);
                Visit(i + edge * side);
                Visit(i * side);
                Visit(i * side + edge);
            }
            while (queue.Count != 0)
            {
                int i = queue.Dequeue();
                int x = i % side, z = i / side;
                if (x > 0) Visit(i - 1);
                if (x < edge) Visit(i + 1);
                if (z > 0) Visit(i - side);
                if (z < edge) Visit(i + side);
            }
            return connected;

            void Visit(int index)
            {
                if (ground[index] > 0f || connected[index])
                    return;
                connected[index] = true;
                queue.Enqueue(index);
            }
        }
    }
}
