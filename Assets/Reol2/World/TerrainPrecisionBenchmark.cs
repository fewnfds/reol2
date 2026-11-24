#if DEVELOPMENT_BUILD || REOL_TEST_BUILD
using System;
using Reol2.Voxels;
using UnityEngine;
using UnityEngine.Rendering;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Reol2.World
{
    internal static class TerrainPrecisionBenchmark
    {
        private const int Passes = 80;
        private const int Warmup = 12;
        private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

        internal static void Run(ContinentTerrain terrain)
        {
            var surface = new WorldSurface(new WorldDefinition(terrain.Seed));
            CompareHeightNodes(surface, "landing", terrain.WorldLanding);
            CompareHeightNodes(surface, "mountain", terrain.MountainOverview);
            CompareHeightNodes(surface, "basin", terrain.BasinEntry);
            Vector3 river = surface.RiverAt(18000);
            CompareHeightNodes(surface, "river-bank",
                new WorldPosition(surface.Center.x + river.x + river.z, 0, surface.Center.y + 18000));
            ComparePages(terrain.Seed);
        }

        private static void CompareHeightNodes(WorldSurface surface, string site, WorldPosition center)
        {
            const int side = 64;
            int startX = (int)Math.Floor((center.X - side / 2) / 8) * 8;
            int startZ = (int)Math.Floor((center.Z - side / 2) / 8) * 8;
            foreach (int step in new[] { 8, 4, 2 })
            {
                int nodeCount = side / step + 1;
                var nodes = new float[nodeCount, nodeCount];
                for (int z = 0; z < nodeCount; z++)
                for (int x = 0; x < nodeCount; x++)
                    nodes[x, z] = surface.Node(startX + x * step, startZ + z * step);

                var error = new double[side * side];
                for (int z = 0; z < side; z++)
                for (int x = 0; x < side; x++)
                {
                    int i = x / step, j = z / step;
                    float fitted = WorldSurface.Interpolate(nodes[i, j], nodes[i + 1, j],
                        nodes[i, j + 1], nodes[i + 1, j + 1], x % step / (float)step, z % step / (float)step);
                    error[x + z * side] = Math.Abs((double)fitted - surface.Node(startX + x, startZ + z)) * 1000;
                }
                Array.Sort(error);
                Debug.Log(FormattableString.Invariant($"Terrain precision | {site} origin {startX},{startZ} | node {step}m | samples {error.Length} | control nodes {nodes.Length} | height error mm p95 {Percentile(error, 0.95):F4}, max {error[^1]:F4}"));
            }
        }

        private static void ComparePages(long seed)
        {
            var coarse = new PageTrial(16);
            var fine = new PageTrial(32);
            var random = new System.Random(unchecked((int)seed));
            try
            {
                for (int pass = -Warmup; pass < Passes; pass++)
                {
                    float x = 3.5f + (float)random.NextDouble() * 0.5f;
                    float z = 3.5f + (float)random.NextDouble() * 0.5f;
                    var center = new Vector3(x, Hill(x, z) - 0.20f, z);
                    // Both trials use the same center; alternate execution order at every sample.
                    bool coarseFirst = (pass & 1) == 0;
                    (coarseFirst ? coarse : fine).Measure(pass, center);
                    (coarseFirst ? fine : coarse).Measure(pass, center);
                }
                coarse.Report();
                fine.Report();
            }
            finally
            {
                coarse.Dispose();
                fine.Dispose();
            }
        }

        private static float Hill(float x, float z)
        {
            float dx = x - 3.9f, dz = z - 4.1f;
            return 2.8f + 1.4f * Mathf.Exp(-(dx * dx + dz * dz) / 5f) +
                   0.35f * Mathf.Sin(1.2f * x + 0.4f) * Mathf.Cos(0.95f * z + 0.7f);
        }

        // Attribute geometry cost separately from gameplay material lookup.
        private static Color32 SurfaceColor(Vector3Int chunk, Vector3 point, Vector3 normal) =>
            new(127, 127, 127, 255);

        private sealed class PageTrial : IDisposable
        {
            private readonly int cells;
            private readonly float spacing;
            private readonly MarchingCubes mesher;
            private readonly GameObject root;
            private readonly MeshCollider collider;
            private readonly Mesh mesh;
            private readonly double[] sample = new double[Passes], extract = new double[Passes],
                upload = new double[Passes], bake = new double[Passes], publish = new double[Passes],
                total = new double[Passes], depthError = new double[Passes];
            private int minTriangles = int.MaxValue;
            private int maxTriangles;

            internal PageTrial(int cells)
            {
                this.cells = cells;
                spacing = 8f / cells;
                mesher = new MarchingCubes(cells, spacing);
                mesh = new Mesh { name = "Terrain precision mesh", indexFormat = IndexFormat.UInt16 };
                mesh.MarkDynamic();
                root = new GameObject("Terrain precision collider");
                collider = root.AddComponent<MeshCollider>();
            }

            internal void Measure(int pass, Vector3 center)
            {
                collider.sharedMesh = null;
                long start = Stopwatch.GetTimestamp();
                int index = 0;
                float[] buffer = mesher.SampleBuffer;
                for (int z = -1; z <= cells + 1; z++)
                for (int y = -1; y <= cells + 1; y++)
                for (int x = -1; x <= cells + 1; x++)
                {
                    Vector3 point = new Vector3(x, y, z) * spacing;
                    float cut = 0.65f - Vector3.Distance(point, center);
                    float ground = point.y - Hill(point.x, point.z);
                    buffer[index++] = ContinentField.NonzeroDensity(Mathf.Max(ground, cut));
                }
                long sampled = Stopwatch.GetTimestamp();
                mesher.Extract(Vector3Int.zero, SurfaceColor);
                long extracted = Stopwatch.GetTimestamp();
                mesher.Upload(mesh);
                long uploaded = Stopwatch.GetTimestamp();
                Physics.BakeMesh(mesh.GetEntityId(), convex: false, collider.cookingOptions);
                long baked = Stopwatch.GetTimestamp();
                collider.sharedMesh = mesh;
                long published = Stopwatch.GetTimestamp();
                if (!collider.Raycast(new Ray(new Vector3(center.x, 8f, center.z), Vector3.down), out var hit, 8f) ||
                    Mathf.Abs(hit.point.y - (center.y - 0.65f)) > 0.35f)
                    throw new InvalidOperationException($"Precision fixture missed the excavated floor at {center} ({spacing}m).");
                if (pass < 0)
                    return;
                sample[pass] = (sampled - start) * MsPerTick;
                extract[pass] = (extracted - sampled) * MsPerTick;
                upload[pass] = (uploaded - extracted) * MsPerTick;
                bake[pass] = (baked - uploaded) * MsPerTick;
                publish[pass] = (published - baked) * MsPerTick;
                total[pass] = (published - start) * MsPerTick;
                depthError[pass] = Math.Abs(hit.point.y - (center.y - 0.65f)) * 1000;
                minTriangles = Math.Min(minTriangles, mesher.TriangleCount);
                maxTriangles = Math.Max(maxTriangles, mesher.TriangleCount);
            }

            internal void Report()
            {
                string prefix = FormattableString.Invariant($"Terrain precision | 8m hill + sphere cut | spacing {spacing}m");
                Debug.Log($"{prefix} | passes {Passes}, warmup {Warmup} | samples {mesher.SampleBuffer.Length} | cells {cells*cells*cells} | triangles {minTriangles}..{maxTriangles}");
                LogRange(prefix, "sample ms", sample);
                LogRange(prefix, "extract ms", extract);
                LogRange(prefix, "upload ms", upload);
                LogRange(prefix, "bake ms", bake);
                LogRange(prefix, "publish ms", publish);
                LogRange(prefix, "total CPU ms", total);
                LogRange(prefix, "center collision error mm", depthError);
            }

            public void Dispose()
            {
                collider.sharedMesh = null;
                UnityEngine.Object.Destroy(mesh);
                UnityEngine.Object.Destroy(root);
            }
        }

        private static void LogRange(string prefix, string stage, double[] values)
        {
            Array.Sort(values);
            Debug.Log(FormattableString.Invariant($"{prefix} | {stage} | p50 {Percentile(values, 0.50):F4}, p95 {Percentile(values, 0.95):F4}, p99 {Percentile(values, 0.99):F4}, max {values[^1]:F4}"));
        }

        private static double Percentile(double[] sorted, double p) => sorted[(int)Math.Ceiling(sorted.Length * p) - 1];
    }
}
#endif
