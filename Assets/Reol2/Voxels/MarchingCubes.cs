using System;
using System.Collections.Generic;
using UnityEngine;

namespace Reol2.Voxels
{
    internal sealed class MarchingCubes
    {
        private readonly int cells;
        private readonly int width;
        private readonly int slice;
        private readonly float spacing;
        private readonly int[] cornerOffsets;
        private readonly Bounds chunkBounds;
        private readonly float[] values;
        private readonly int[] vertexForEdge;
        private readonly int[] cellVertices = new int[12];
        private readonly List<Vector3> vertices = new(2048);
        private readonly List<Vector3> normals = new(2048);
        private readonly List<Color32> colors = new(2048);
        private readonly List<int> triangles = new(8192);

        internal int TriangleCount => triangles.Count / 3;
        internal float[] SampleBuffer => values;

        internal MarchingCubes(int cells = DensityVolume.ChunkCells, float spacing = DensityVolume.Spacing)
        {
            this.cells = cells;
            this.spacing = spacing;
            width = cells + 3;
            slice = width * width;
            cornerOffsets = new[] { 0, 1, width, width + 1, slice, slice + 1, slice + width, slice + width + 1 };
            values = new float[slice * width];
            vertexForEdge = new int[values.Length * 3];
            Vector3 size = Vector3.one * (cells * spacing);
            chunkBounds = new Bounds(size * 0.5f, size);
        }

        internal void Extract(DensityVolume volume, Vector3Int chunk)
        {
            volume.CopyChunkSamples(chunk, values);
            Extract(chunk, DemoSurfaceColor);
        }

        internal void Extract(Vector3Int chunk, Func<Vector3Int, Vector3, Vector3, Color32> surfaceColor)
        {
            Array.Fill(vertexForEdge, -1);
            vertices.Clear();
            normals.Clear();
            colors.Clear();
            triangles.Clear();

            for (int z = 0; z < cells; z++)
            for (int y = 0; y < cells; y++)
            {
                int sample = 1 + (y + 1) * width + (z + 1) * slice;
                for (int x = 0; x < cells; x++, sample++)
                {
                    int mask = CaseAt(sample);
                    if (mask == 0 || mask == 255)
                        continue;

                    byte[] edges = RegularCellTables.Edges[mask];
                    for (int edge = 0; edge < edges.Length; edge++)
                    {
                        int from = edges[edge] >> 4;
                        int to = edges[edge] & 15;
                        int axis = (from ^ to) >> 1;
                        int a = sample + cornerOffsets[from];
                        int slot = a * 3 + axis;
                        int vertex = vertexForEdge[slot];
                        if (vertex < 0)
                        {
                            int b = sample + cornerOffsets[to];
                            var start = new Vector3(x + (from & 1), y + ((from >> 1) & 1), z + (from >> 2));
                            vertex = AddVertex(a, b, axis, start, chunk, surfaceColor);
                            vertexForEdge[slot] = vertex;
                        }

                        cellVertices[edge] = vertex;
                    }

                    byte[] pattern = RegularCellTables.Triangles[RegularCellTables.Classes[mask]];
                    for (int i = 0; i < pattern.Length; i++)
                        triangles.Add(cellVertices[pattern[i]]);
                }
            }
        }

        internal void Upload(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0, calculateBounds: false);
            mesh.bounds = chunkBounds;
        }

        private int CaseAt(int index)
        {
            return (values[index] < 0f ? 1 : 0)
                | (values[index + 1] < 0f ? 2 : 0)
                | (values[index + width] < 0f ? 4 : 0)
                | (values[index + width + 1] < 0f ? 8 : 0)
                | (values[index + slice] < 0f ? 16 : 0)
                | (values[index + slice + 1] < 0f ? 32 : 0)
                | (values[index + slice + width] < 0f ? 64 : 0)
                | (values[index + slice + width + 1] < 0f ? 128 : 0);
        }

        private Vector3 Gradient(int sample)
        {
            return new Vector3(values[sample + 1] - values[sample - 1],
                               values[sample + width] - values[sample - width],
                               values[sample + slice] - values[sample - slice]);
        }

        private int AddVertex(int a, int b, int axis, Vector3 gridPosition, Vector3Int chunk,
                              Func<Vector3Int, Vector3, Vector3, Color32> surfaceColor)
        {
            float t = values[a] / (values[a] - values[b]);
            gridPosition[axis] += t;
            Vector3 position = gridPosition * spacing;
            Vector3 normal = Vector3.LerpUnclamped(Gradient(a), Gradient(b), t).normalized;
            int result = vertices.Count;
            vertices.Add(position);
            normals.Add(normal);
            colors.Add(surfaceColor(chunk, position, normal));
            return result;
        }

        private static Color32 DemoSurfaceColor(Vector3Int chunk, Vector3 local, Vector3 normal)
        {
            Vector3 world = local + (Vector3)chunk * DensityVolume.ChunkSize;
            float grass = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.43f, 0.86f, normal.y));
            grass *= Mathf.SmoothStep(0f, 1f, (world.y - 0.55f) * 1.5f);
            float rockBand = Mathf.Sin(world.y * 1.9f + world.z * 0.37f) * 0.5f + 0.5f;
            Color rock = Color.Lerp(new Color(0.32f, 0.37f, 0.34f), new Color(0.48f, 0.47f, 0.38f), rockBand);
            float canopy = Mathf.Sin(world.x * 0.49f + world.z * 0.7f) * 0.5f + 0.5f;
            Color cover = Color.Lerp(new Color(0.22f, 0.40f, 0.29f), new Color(0.45f, 0.55f, 0.30f), canopy);

            return Color.Lerp(rock, cover, grass);
        }
    }
}
