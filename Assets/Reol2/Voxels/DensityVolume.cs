using System;
using UnityEngine;

namespace Reol2.Voxels
{
    internal sealed class DensityVolume
    {
        internal const int SamplesPerMeter = 2;
        internal const float Spacing = 1f / SamplesPerMeter;
        internal const int ChunkCells = 16;
        internal const int SampleWidth = ChunkCells + 3; // Corners plus one sample on both sides for normals.
        internal const float ChunkSize = ChunkCells * Spacing;

        private readonly Vector3Int minimum;
        private readonly Vector3Int maximum;
        private readonly Vector3Int origin;
        private readonly int width;
        private readonly int slice;
        private readonly float[] initialSamples;
        private readonly float[] samples;
        // Zero denotes natural terrain. Other values identify the sample at the center of a fill cell plus one.
        private readonly int[] fillSource;
        internal BoundsInt SampleBounds => new(minimum, maximum - minimum + Vector3Int.one);

        internal DensityVolume(Vector3Int firstChunk, Vector3Int chunkCount, Func<Vector3, float> density)
        {
            minimum = firstChunk * ChunkCells;
            maximum = minimum + chunkCount * ChunkCells;
            origin = minimum - Vector3Int.one;
            Vector3Int size = maximum - minimum + Vector3Int.one * 3;
            width = size.x;
            slice = size.x * size.y;
            samples = new float[slice * size.z];
            initialSamples = new float[samples.Length];
            fillSource = new int[samples.Length];

            for (int z = origin.z; z <= maximum.z + 1; z++)
            for (int y = origin.y; y <= maximum.y + 1; y++)
            for (int x = origin.x; x <= maximum.x + 1; x++)
            {
                float sample = density(new Vector3(x, y, z) * Spacing);
                // Keep vertices away from exactly equal-to-iso corners.
                initialSamples[Index(x, y, z)] = sample == 0f ? 0.0001f : sample;
            }

            Reset();
        }

        internal void Reset()
        {
            Array.Copy(initialSamples, samples, samples.Length);
            Array.Clear(fillSource, 0, fillSource.Length);
        }

        internal bool ContainsEditCell(Vector3Int cell)
        {
            Vector3Int first = minimum / SamplesPerMeter;
            Vector3Int end = maximum / SamplesPerMeter;
            return cell.x >= first.x && cell.x < end.x
                && cell.y >= first.y && cell.y < end.y
                && cell.z >= first.z && cell.z < end.z;
        }

        internal static Vector3Int CellAt(Vector3 position)
        {
            return Vector3Int.FloorToInt(position);
        }

        internal static Vector3 CellCenter(Vector3Int cell) => (Vector3)cell + Vector3.one * 0.5f;

        internal Vector3Int DigCellAt(Vector3 insideSurface)
        {
            Vector3Int cell = CellAt(insideSurface);
            if (!ContainsEditCell(cell))
                return cell;

            // An edited surface can extend into the next cell. Use the source of its nearest solid corner.
            Vector3 position = insideSurface * SamplesPerMeter;
            Vector3Int corner = Vector3Int.FloorToInt(position);
            float closest = float.MaxValue;
            int source = 0;
            for (int z = corner.z; z <= corner.z + 1; z++)
            for (int y = corner.y; y <= corner.y + 1; y++)
            for (int x = corner.x; x <= corner.x + 1; x++)
            {
                int index = Index(x, y, z);
                if (samples[index] >= 0f)
                    continue;

                float distance = (new Vector3(x, y, z) - position).sqrMagnitude;
                if (distance >= closest)
                    continue;

                closest = distance;
                source = fillSource[index];
            }

            if (source == 0)
                return cell;

            int center = source - 1;
            Vector3Int sample = origin + new Vector3Int(center % width, center % slice / width, center / slice);
            return CellAt((Vector3)sample * Spacing);
        }

        internal bool Edit(Vector3Int cell, bool fill, out BoundsInt changed)
        {
            changed = default;
            if (!ContainsEditCell(cell))
                return false;

            Vector3Int cellMinimum = cell * SamplesPerMeter;
            // Preserve the empty outer shell of the bounded demo volume.
            Vector3Int start = Vector3Int.Max(cellMinimum, minimum + Vector3Int.one);
            Vector3Int end = Vector3Int.Min(cellMinimum + Vector3Int.one * SamplesPerMeter, maximum - Vector3Int.one);
            Vector3 center = CellCenter(cell);
            int source = fill ? Index(cellMinimum.x + 1, cellMinimum.y + 1, cellMinimum.z + 1) + 1 : 0;
            Vector3Int low = maximum;
            Vector3Int high = minimum;

            for (int z = start.z; z <= end.z; z++)
            for (int y = start.y; y <= end.y; y++)
            for (int x = start.x; x <= end.x; x++)
            {
                int index = Index(x, y, z);
                float stamp = RoundedBox(new Vector3(x, y, z) * Spacing - center, Vector3.one * 0.55f, 0.08f);
                float current = samples[index];
                float next = fill ? Mathf.Min(current, stamp) : Mathf.Max(current, -stamp);
                if (current == next)
                    continue;

                samples[index] = next;
                if (current >= 0f || next >= 0f)
                    fillSource[index] = source;
                var sample = new Vector3Int(x, y, z);
                low = Vector3Int.Min(low, sample);
                high = Vector3Int.Max(high, sample);
            }

            if (low.x > high.x)
                return false;

            changed = new BoundsInt(low, high - low + Vector3Int.one);
            return true;
        }

        internal void CopyChunkSamples(Vector3Int chunk, float[] destination)
        {
            Vector3Int start = chunk * ChunkCells - Vector3Int.one;
            int offset = 0;
            for (int z = start.z; z < start.z + SampleWidth; z++)
            for (int y = start.y; y < start.y + SampleWidth; y++)
            {
                Array.Copy(samples, Index(start.x, y, z), destination, offset, SampleWidth);
                offset += SampleWidth;
            }
        }

        internal static BoundsInt ChunkSampleBounds(Vector3Int chunk)
        {
            return new BoundsInt(chunk * ChunkCells - Vector3Int.one, Vector3Int.one * SampleWidth);
        }

        internal static float RoundedBox(Vector3 point, Vector3 halfExtents, float radius)
        {
            var delta = new Vector3(Mathf.Abs(point.x), Mathf.Abs(point.y), Mathf.Abs(point.z))
                - halfExtents + Vector3.one * radius;
            float side = Mathf.Max(delta.x, Mathf.Max(delta.y, delta.z));
            return side <= 0f ? side - radius : Vector3.Max(delta, Vector3.zero).magnitude - radius;
        }

        private int Index(int x, int y, int z)
        {
            return x - origin.x + (y - origin.y) * width + (z - origin.z) * slice;
        }
    }
}
