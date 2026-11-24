using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Reol2.World
{
    internal sealed class OceanSurface
    {
        // ContinentOcean.shader finishes the shallow-water color transition at this depth.
        internal const float DeepWaterDepth = 2.5f;
        private static readonly int WaterOrigin = Shader.PropertyToID("_WaterOrigin");
        private readonly Mesh mesh = new() { name = "Shared sea and deep inland water", indexFormat = IndexFormat.UInt32 };
        private readonly MeshRenderer renderer;
        private readonly MaterialPropertyBlock ripples = new();
        private readonly List<int> indices = new();
        private readonly List<Vector2> waterDepths = new();
        private Vector3Int meshOrigin;
        private Vector3Int rippleOrigin;

        internal readonly List<Vector3> Vertices = new();
#if UNITY_EDITOR || DEVELOPMENT_BUILD || REOL_TEST_BUILD
        internal int TriangleCount => renderer.enabled ? (int)mesh.GetIndexCount(0) / 3 : 0;
#endif

        internal OceanSurface(Transform parent, Material material)
        {
            var obj = new GameObject("Ocean", typeof(MeshFilter), typeof(MeshRenderer));
            obj.transform.SetParent(parent, false);
            obj.GetComponent<MeshFilter>().sharedMesh = mesh;
            renderer = obj.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.enabled = false;
            mesh.MarkDynamic();
        }

        internal void BuildFixed(ContinentField field, WorldFrame frame)
        {
            const int edge = ContinentField.Extent;
            const int size = ContinentField.Columns + 1;
            const int step = ContinentField.ChunkSize;
            var heights = new float[size * size];
            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
                heights[x + z * size] = field.HeightAt(x * step - edge, z * step - edge);
            AppendShore(Vertices, heights, size, 0, size - 1, step);
            for (int i = 0; i < Vertices.Count; i++)
                Vertices[i] -= new Vector3(edge, 0, edge);
            const int far = edge * 3;
            AppendQuad(Vertices, -far, -far, far, -edge);
            AppendQuad(Vertices, -far, edge, far, far);
            AppendQuad(Vertices, -far, -edge, -edge, edge);
            AppendQuad(Vertices, edge, -edge, far, edge);
            Publish(frame);
        }

        internal void Publish(WorldFrame frame)
        {
            if (Vertices.Count < mesh.vertexCount)
                mesh.Clear(keepVertexLayout: true);
            renderer.enabled = Vertices.Count != 0;
            if (!renderer.enabled)
                return;
            while (indices.Count < Vertices.Count)
                indices.Add(indices.Count);
            waterDepths.Clear();
            foreach (Vector3 vertex in Vertices)
                waterDepths.Add(new Vector2(DeepWaterDepth, vertex.y));
            mesh.SetVertices(Vertices);
            mesh.SetUVs(0, waterDepths);
            mesh.SetIndices(indices, 0, Vertices.Count, MeshTopology.Triangles, 0);
            // X and Z are relative to the frame at publication. Y is the absolute water level.
            meshOrigin = new Vector3Int(frame.Origin.x, 0, frame.Origin.z);
            Reposition(frame);
        }

        internal void Reposition(WorldFrame frame)
        {
            renderer.transform.localPosition = (Vector3)(meshOrigin - frame.Origin);
            if (rippleOrigin == frame.Origin)
                return;
            rippleOrigin = frame.Origin;
            ripples.SetVector(WaterOrigin, new Vector4(frame.Origin.x / 128, frame.Origin.z / 128, 0, 0));
            renderer.SetPropertyBlock(ripples);
        }

        internal void Release() => Object.Destroy(mesh);

        internal static void AppendQuad(List<Vector3> output, float x0, float z0, float x1, float z1, float level = 0f)
        {
            var a = new Vector3(x0, level, z0);
            var b = new Vector3(x1, level, z0);
            var c = new Vector3(x0, level, z1);
            var d = new Vector3(x1, level, z1);
            output.Add(a); output.Add(c); output.Add(b);
            output.Add(b); output.Add(c); output.Add(d);
        }

        internal static void AppendShore(List<Vector3> output, float[] heights, int stride, int start, int cells, int step)
        {
            for (int z = 0; z < cells; z++)
            {
                int run = -1;
                for (int x = 0; x < cells; x++)
                {
                    int index = start + z * stride + x;
                    float a = heights[index], b = heights[index + 1];
                    float c = heights[index + stride], d = heights[index + stride + 1];
                    if (a < 0f && b < 0f && c < 0f && d < 0f)
                    {
                        if (run < 0)
                            run = x;
                        continue;
                    }
                    if (run >= 0)
                    {
                        AppendQuad(output, run * step, z * step, x * step, (z + 1) * step);
                        run = -1;
                    }
                    if (a >= 0f && b >= 0f && c >= 0f && d >= 0f)
                        continue;
                    // The diagonal is shared with WorldSurface.Interpolate and the terrain mesh.
                    var p = new Vector3(x * step, a, z * step);
                    var q = new Vector3((x + 1) * step, b, z * step);
                    var r = new Vector3(x * step, c, (z + 1) * step);
                    var s = new Vector3((x + 1) * step, d, (z + 1) * step);
                    ClipTriangle(output, p, r, q);
                    ClipTriangle(output, q, r, s);
                }
                if (run >= 0)
                    AppendQuad(output, run * step, z * step, cells * step, (z + 1) * step);
            }
        }

        private static void ClipTriangle(List<Vector3> output, Vector3 a, Vector3 b, Vector3 c)
        {
            // A sea-level cut yields a triangle or quad, so there are at most four output points.
            System.Span<Vector3> points = stackalloc Vector3[4];
            int count = 0;
            AddEdge(c, a, points, ref count);
            AddEdge(a, b, points, ref count);
            AddEdge(b, c, points, ref count);
            for (int i = 1; i + 1 < count; i++)
            {
                output.Add(points[0]); output.Add(points[i]); output.Add(points[i + 1]);
            }
        }

        private static void AddEdge(Vector3 a, Vector3 b, System.Span<Vector3> points, ref int count)
        {
            if ((a.y < 0f) != (b.y < 0f))
            {
                Vector3 edge = Vector3.LerpUnclamped(a, b, a.y / (a.y - b.y));
                edge.y = 0f;
                points[count++] = edge;
            }
            if (b.y < 0f)
            {
                b.y = 0f;
                points[count++] = b;
            }
        }
    }
}
