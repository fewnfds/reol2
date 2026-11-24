using System;
using System.Numerics;

namespace Reol2.World
{
    internal sealed class RegionalDrainage
    {
        internal const int Ocean = -1;
        internal const int Basin = -2;
        private const int Unvisited = -3;
        private static readonly int[] OffsetX = { -1, 0, 1, -1, 1, -1, 0, 1 };
        private static readonly int[] OffsetZ = { -1, -1, -1, 0, 0, 1, 1, 1 };
        private static readonly float[] InverseDistance = { 0.70710678f, 1, 0.70710678f, 1, 1, 0.70710678f, 1, 0.70710678f };

        internal readonly float[] Ground, Filled, Runoff;
        internal readonly int[] Downstream, Order, Area;
        internal readonly int Count;

        internal RegionalDrainage(float[] ground, RegionalClimate[] climate, int side, int step, LandformProfile profile)
        {
            Ground = ground;
            int length = ground.Length;
            Filled = new float[length];
            Runoff = new float[length];
            Downstream = new int[length];
            Order = new int[length];
            Area = new int[length];
            var queue = new HeightHeap(Filled);
            int half = (side - 1) * step / 2;
            for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
            {
                int index = x + z * side;
                float height = ground[index];
                if (height <= 0f)
                {
                    Downstream[index] = Ocean;
                    if (TouchesLand(ground, index, x, z, side))
                        queue.Push(index);
                    continue;
                }

                var point = new Vector2(x * step - half, z * step - half);
                Vector2 fromBasin = Vector2.Abs(point - profile.Basin);
                Vector2 lake = profile.LakeExtent;
                if (height <= profile.LakeLevel && fromBasin.X < lake.X && fromBasin.Y < lake.Y)
                {
                    Downstream[index] = Basin;
                    Filled[index] = profile.LakeLevel;
                    queue.Push(index);
                }
                else
                {
                    Downstream[index] = Unvisited;
                }
                Runoff[index] = climate[index].Runoff;
                Area[index] = 1;
            }

            // Priority-Flood. The discovery edge is a valid downhill path on flats as well as slopes.
            while (queue.Count != 0)
            {
                int index = queue.Pop();
                Order[Count++] = index;
                int x = index % side, z = index / side;
                for (int d = 0; d < OffsetX.Length; d++)
                {
                    int nx = x + OffsetX[d], nz = z + OffsetZ[d];
                    if ((uint)nx >= side || (uint)nz >= side)
                        continue;
                    int neighbor = nx + nz * side;
                    if (Downstream[neighbor] != Unvisited)
                        continue;
                    Downstream[neighbor] = index;
                    Filled[neighbor] = MathF.Max(Filled[index], ground[neighbor]);
                    queue.Push(neighbor);
                }
            }

            for (int rank = Count - 1; rank >= 0; rank--)
            {
                int index = Order[rank];
                int downhill = Downstream[index];
                if (downhill < 0)
                    continue;

                // Prefer the steepest D8 descent. Keep the discovery edge for equal-height nodes.
                // A strictly lower receiver is earlier in the flood order, so accumulation is topological.
                int x = index % side, z = index / side;
                float bestSlope = 0f;
                for (int d = 0; d < OffsetX.Length; d++)
                {
                    int nx = x + OffsetX[d], nz = z + OffsetZ[d];
                    if ((uint)nx >= side || (uint)nz >= side)
                        continue;
                    int neighbor = nx + nz * side;
                    float slope = (Filled[index] - Filled[neighbor]) * InverseDistance[d];
                    if (slope > bestSlope)
                    {
                        downhill = neighbor;
                        bestSlope = slope;
                    }
                }
                Downstream[index] = downhill;
                Area[downhill] += Area[index];
                Runoff[downhill] += Runoff[index];
            }
        }

        private static bool TouchesLand(float[] ground, int index, int x, int z, int side)
        {
            for (int d = 0; d < OffsetX.Length; d++)
            {
                int nx = x + OffsetX[d], nz = z + OffsetZ[d];
                if ((uint)nx < side && (uint)nz < side && ground[index + OffsetX[d] + OffsetZ[d] * side] > 0f)
                    return true;
            }
            return false;
        }

        private sealed class HeightHeap
        {
            private readonly int[] indices;
            private readonly float[] height;
            internal int Count { get; private set; }

            internal HeightHeap(float[] height)
            {
                this.height = height;
                indices = new int[height.Length];
            }

            internal void Push(int index)
            {
                int next = Count++;
                while (next > 0)
                {
                    int parent = (next - 1) / 2;
                    if (!Before(index, indices[parent]))
                        break;
                    indices[next] = indices[parent];
                    next = parent;
                }
                indices[next] = index;
            }

            internal int Pop()
            {
                int first = indices[0];
                int last = indices[--Count];
                int next = 0;
                while (next < Count / 2)
                {
                    int child = next * 2 + 1;
                    if (child + 1 < Count && Before(indices[child + 1], indices[child]))
                        child++;
                    if (!Before(indices[child], last))
                        break;
                    indices[next] = indices[child];
                    next = child;
                }
                indices[next] = last;
                return first;
            }

            private bool Before(int a, int b) => height[a] < height[b] || (height[a] == height[b] && a < b);
        }
    }
}
