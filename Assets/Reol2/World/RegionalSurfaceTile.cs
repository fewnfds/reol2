using System;
using System.Collections.Generic;
using UnityEngine;

namespace Reol2.World
{
    internal sealed class RegionalSurfaceTile
    {
        private const int MaximumCells = 2048;
        private const int RiverCellReserve = 1536;
        private const int RiverDetailTileWidth = 1024;
        private const int RiverReserveTileWidth = 4096;
        private readonly ContinentField field;
        private readonly WorldSurface surface;
        private readonly List<Cell> cells = new();
        private readonly List<Cell> pending = new();
        private readonly HashSet<Vector2Int> corners = new();
        private readonly Dictionary<(Vector2Int, Vector2Int), bool> edgeSplits = new();
        private readonly Dictionary<Vector2Int, Sample> samples = new();
        private readonly Dictionary<Vector2Int, int> vertexAt = new();
        private readonly List<int> edge = new();
        private readonly List<Vector3> vertices = new();
        private readonly List<Vector3> normals = new();
        private readonly List<Color32> colors = new();
        private readonly List<Vector2> skirtOffsets = new();
        private readonly List<float> waterLevels = new();
        private readonly List<int> triangles = new();
        private readonly List<int> waterIndices = new();
        private readonly List<Vector3> water = new();
        private readonly List<Vector2> waterDepths = new();
        private int west, south, width, normalStep;
        private int elevation;
        private int baseIndex, baseGrid, baseStride, baseDetail;
        private float? sharedWaterLevel;
        internal int CellVisits { get; private set; }
        internal float? SharedWaterLevel => water.Count > 0 ? sharedWaterLevel : null;

        internal RegionalSurfaceTile(ContinentField field)
        {
            this.field = field;
            surface = field.Surface;
        }

        internal void Begin(int x, int z, int stride, int grid)
        {
            cells.Clear();
            pending.Clear();
            corners.Clear();
            edgeSplits.Clear();
            samples.Clear();
            vertexAt.Clear();
            vertices.Clear();
            normals.Clear();
            colors.Clear();
            skirtOffsets.Clear();
            waterLevels.Clear();
            triangles.Clear();
            water.Clear();
            waterDepths.Clear();

            west = x;
            south = z;
            width = grid * stride;
            normalStep = Math.Min(stride, 32);
            Sample origin = Read(Vector2Int.zero);
            elevation = Mathf.FloorToInt(origin.Ground / 64f) * 64;
            CellVisits = 0;
            sharedWaterLevel = origin.Level;
            baseIndex = 0;
            baseGrid = grid;
            baseStride = stride;
            baseDetail = stride <= 64 ? ContinentField.ChunkSize : Math.Max(16, stride / 16);
        }

        internal bool Prepare(int count)
        {
            while (count > 0 && baseIndex < baseGrid * baseGrid)
            {
                Enqueue(new Cell(baseIndex % baseGrid * baseStride, baseIndex / baseGrid * baseStride, baseStride, baseDetail));
                baseIndex++;
                count--;
            }
            while (count > 0 && pending.Count != 0)
            {
                Cell cell = Dequeue();
                // Keep the normal leaf budget for terrain and shore. Only cells
                // crossed by a river axis can use the additional far-tile budget.
                int limit = cell.HasSpine && width >= RiverReserveTileWidth ? MaximumCells + RiverCellReserve : MaximumCells;
                if (cells.Count + pending.Count + 4 > limit)
                {
                    AddCell(cell);
                    count--;
                    continue;
                }
                int half = cell.Size / 2;
                Enqueue(new Cell(cell.X, cell.Z, half, cell.Detail));
                Enqueue(new Cell(cell.X + half, cell.Z, half, cell.Detail));
                Enqueue(new Cell(cell.X, cell.Z + half, half, cell.Detail));
                Enqueue(new Cell(cell.X + half, cell.Z + half, half, cell.Detail));
                count -= 4;
            }
            return baseIndex == baseGrid * baseGrid && pending.Count == 0;
        }

        private void Enqueue(Cell cell)
        {
            CellVisits++;
            cell.Priority = DetailPriority(ref cell);
            if (cell.Priority == 0)
            {
                AddCell(cell);
                return;
            }
            int index = pending.Count;
            pending.Add(cell);
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (!cell.Before(pending[parent]))
                    break;
                pending[index] = pending[parent];
                index = parent;
            }
            pending[index] = cell;
        }

        private Cell Dequeue()
        {
            Cell first = pending[0];
            int count = pending.Count - 1;
            Cell last = pending[count];
            pending.RemoveAt(count);
            if (count == 0)
                return first;
            int index = 0;
            while (index * 2 + 1 < count)
            {
                int child = index * 2 + 1;
                if (child + 1 < count && pending[child + 1].Before(pending[child]))
                    child++;
                if (!pending[child].Before(last))
                    break;
                pending[index] = pending[child];
                index = child;
            }
            pending[index] = last;
            return first;
        }

        private void AddCell(Cell cell)
        {
            cells.Add(cell);
            corners.Add(cell.Southwest);
            corners.Add(cell.Northwest);
            corners.Add(cell.Northeast);
            corners.Add(cell.Southeast);
        }

        internal int CellCount => cells.Count;

        internal void Emit(int first, int count)
        {
            int end = Math.Min(first + count, cells.Count);
            for (int i = first; i < end; i++)
                Emit(cells[i]);
        }

        internal int Publish(Mesh mesh)
        {
            mesh.Clear(keepVertexLayout: true);
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, skirtOffsets);
            mesh.SetTriangles(triangles, 0, calculateBounds: true);
            return elevation;
        }

        internal int WaterVertexCount => water.Count;

        internal void PublishWater(Mesh mesh)
        {
            if (water.Count < mesh.vertexCount)
                mesh.Clear(keepVertexLayout: true);
            while (waterIndices.Count < water.Count)
                waterIndices.Add(waterIndices.Count);
            mesh.SetVertices(water);
            mesh.SetUVs(0, waterDepths);
            mesh.SetIndices(waterIndices, 0, water.Count, MeshTopology.Triangles, 0);
        }

        private int DetailPriority(ref Cell cell)
        {
            if (cell.Size <= ContinentField.ChunkSize)
                return 0;
            int x = west + cell.X, z = south + cell.Z;
            WaterFeatures features = surface.WaterFeaturesIn(x, z, cell.Size);
            if (features == WaterFeatures.None)
                return 0;
            if (cell.Size > RegionalRivers.BucketWidth)
                return cell.Size * 8;
            bool river = (features & WaterFeatures.River) != 0;
            if (river && width >= RiverDetailTileWidth)
            {
                cell.HasSpine = surface.TryRiverSpine(x, z, x + cell.Size, z + cell.Size, out cell.Spine, out float halfWidth);
                float spacing = Math.Clamp(halfWidth * 0.75f, ContinentField.ChunkSize, cell.Detail * 2f);
                if (cell.HasSpine && cell.Size > spacing)
                    return cell.Size * 8;
            }
            if ((features & (WaterFeatures.Coast | WaterFeatures.Lake)) == 0)
                return river && cell.Size > cell.Detail && surface.RiverIntersects(x, z, cell.Size) ? cell.Size : 0;
            if (cell.Size <= cell.Detail && (features & WaterFeatures.Coast) == 0)
                return 0;

            float a = Read(cell.Southwest).Depth, b = Read(cell.Northwest).Depth;
            float c = Read(cell.Northeast).Depth, d = Read(cell.Southeast).Depth;
            float center = Read(cell.Center).Depth;
            float low = MathF.Min(center, MathF.Min(MathF.Min(a, b), MathF.Min(c, d)));
            float high = MathF.Max(center, MathF.Max(MathF.Max(a, b), MathF.Max(c, d)));
            if ((features & WaterFeatures.Coast) != 0 && low < 1.5f && high > -1.5f
                && river && surface.ValleyIntersects(x, z, cell.Size))
                return cell.Size * 4;
            float slack = MathF.Min(2f, cell.Size * 0.013f);
            if (low < slack && high > -slack)
                return cell.Size * 4;
            return river && cell.Size > cell.Detail && surface.RiverIntersects(x, z, cell.Size) ? cell.Size : 0;
        }

        private Sample Read(Vector2Int point)
        {
            if (samples.TryGetValue(point, out Sample known))
                return known;
            int x = west + point.x, z = south + point.y;
            surface.SampleNode(x, z, out float height, out float level);
            var sample = new Sample(height, level);
            samples.Add(point, sample);
            return sample;
        }

        private int Vertex(Vector2Int point)
        {
            if (vertexAt.TryGetValue(point, out int index))
                return index;
            int x = west + point.x, z = south + point.y;
            Sample sample = Read(point);
            if (sharedWaterLevel is float level
                && (sample.Level != level || sample.Depth <= 0f || (level != 0f && sample.Depth < OceanSurface.DeepWaterDepth)))
                sharedWaterLevel = null;
            int step = normalStep;
            var normal = new Vector3(field.HeightNode(x - step, z) - field.HeightNode(x + step, z), 2 * step,
                field.HeightNode(x, z - step) - field.HeightNode(x, z + step)).normalized;
            index = vertices.Count;
            vertexAt.Add(point, index);
            vertices.Add(new Vector3(point.x, sample.Ground - elevation, point.y));
            waterLevels.Add(sample.Level - elevation);
            normals.Add(normal);
            colors.Add(field.MaterialAt(new WorldPosition(x, sample.Ground, z), normal, sample.Ground, step));
            skirtOffsets.Add(Vector2.zero);
            return index;
        }

        private void Emit(Cell cell)
        {
            int a = Vertex(cell.Southwest), b = Vertex(cell.Northwest);
            int c = Vertex(cell.Northeast), d = Vertex(cell.Southeast);
            edge.Clear();
            AddEdge(cell.Southwest, cell.Northwest, a);
            AddEdge(cell.Northwest, cell.Northeast, b);
            AddEdge(cell.Northeast, cell.Southeast, c);
            AddEdge(cell.Southeast, cell.Southwest, d);
            Vector2Int? wetKnot = InteriorWaterKnot(cell);
            if (edge.Count == 4 && !wetKnot.HasValue)
            {
                EmitTriangle(a, b, d);
                EmitTriangle(d, b, c);
            }
            else
            {
                // Every coarse cell consumes the same edge vertices as its smaller
                // neighbours. The terrain and the water cut use these same triangles.
                int center = Vertex(wetKnot ?? cell.Center);
                for (int i = 0; i < edge.Count; i++)
                    EmitTriangle(center, edge[i], edge[(i + 1) % edge.Count]);
            }

            float skirt = cell.Size * 1.25f;
            for (int i = 0; i < edge.Count; i++)
            {
                int first = edge[i], last = edge[(i + 1) % edge.Count];
                Vector3 u = vertices[first], v = vertices[last];
                if ((u.x == v.x && (u.x == 0f || u.x == width))
                    || (u.z == v.z && (u.z == 0f || u.z == width)))
                    AddSkirt(first, last, skirt);
            }
        }

        private Vector2Int? InteriorWaterKnot(Cell cell)
        {
            int spacing = ContinentField.ChunkSize;
            if (cell.Size <= spacing)
                return null;
            var spine = cell.Spine;
            if (!cell.HasSpine)
            {
                if (cell.Size <= RegionalRivers.BucketWidth || surface.Landforms == null)
                    return null;
                int x = west + cell.X, z = south + cell.Z;
                if ((surface.WaterFeaturesIn(x, z, cell.Size) & WaterFeatures.River) == 0
                    || !surface.TryRiverSpine(x, z, x + cell.Size, z + cell.Size, out spine, out _))
                    return null;
            }
            int east = Mathf.FloorToInt((spine.X + surface.Center.x - west) / spacing + 0.5f) * spacing;
            int north = Mathf.FloorToInt((spine.Y + surface.Center.y - south) / spacing + 0.5f) * spacing;
            var knot = new Vector2Int(Math.Clamp(east, cell.X + spacing, cell.X + cell.Size - spacing),
                Math.Clamp(north, cell.Z + spacing, cell.Z + cell.Size - spacing));
            float spineDepth = Read(knot).Depth;
            float centerDepth = Read(cell.Center).Depth;
            if (spineDepth > 0f && spineDepth >= centerDepth)
                return knot;
            return centerDepth > 0f ? cell.Center : null;
        }

        private void AddEdge(Vector2Int start, Vector2Int end, int first)
        {
            edge.Add(first);
            AppendEdgeKnots(start, end);
        }

        private void AddSkirt(int a, int b, float depth)
        {
            int c = AddSkirtVertex(a, depth), d = AddSkirtVertex(b, depth);
            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(b);
            triangles.Add(b);
            triangles.Add(c);
            triangles.Add(d);
        }

        private int AddSkirtVertex(int index, float depth)
        {
            int skirt = vertices.Count;
            vertices.Add(vertices[index] - Vector3.up * depth);
            normals.Add(normals[index]);
            colors.Add(colors[index]);
            skirtOffsets.Add(new Vector2(depth, 0f));
            waterLevels.Add(waterLevels[index]);
            return skirt;
        }

        private void AppendEdgeKnots(Vector2Int start, Vector2Int end)
        {
            Vector2Int mid = (start + end) / 2;
            if (mid == start || mid == end || !corners.Contains(mid) && !RiverCrossesEdge(start, end))
                return;
            AppendEdgeKnots(start, mid);
            edge.Add(Vertex(mid));
            AppendEdgeKnots(mid, end);
        }

        private bool RiverCrossesEdge(Vector2Int start, Vector2Int end)
        {
            int length = Math.Abs(start.x - end.x) + Math.Abs(start.y - end.y);
            if (length <= ContinentField.ChunkSize || surface.Landforms == null)
                return false;
            if (width < RiverDetailTileWidth && !(start.x == end.x && (start.x == 0 || start.x == width))
                && !(start.y == end.y && (start.y == 0 || start.y == width)))
                return false;
            var first = new Vector2Int(Math.Min(start.x, end.x), Math.Min(start.y, end.y));
            var last = new Vector2Int(Math.Max(start.x, end.x), Math.Max(start.y, end.y));
            var key = (first, last);
            if (edgeSplits.TryGetValue(key, out bool split))
                return split;
            split = (surface.WaterFeaturesIn(west + first.x, south + first.y, length) & WaterFeatures.River) != 0
                && surface.TryRiverSpine(west + first.x, south + first.y, west + last.x, south + last.y, out _, out _,
                    ContinentField.ChunkSize);
            edgeSplits.Add(key, split);
            return split;
        }

        private void EmitTriangle(int a, int b, int c)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
            AppendWater(vertices[a], vertices[b], vertices[c], waterLevels[a], waterLevels[b], waterLevels[c]);
        }

        private void AppendWater(Vector3 a, Vector3 b, Vector3 c, float levelA, float levelB, float levelC)
        {
            float da = levelA - a.y, db = levelB - b.y, dc = levelC - c.y;
            if (da <= 0f && db <= 0f && dc <= 0f)
                return;
            a.y = levelA;
            b.y = levelB;
            c.y = levelC;
            if (da > 0f && db > 0f && dc > 0f)
            {
                AddWaterVertex(a, da);
                AddWaterVertex(b, db);
                AddWaterVertex(c, dc);
                return;
            }

            Span<Vector4> polygon = stackalloc Vector4[4];
            int count = 0;
            AddWaterEdge(c, a, dc, da, polygon, ref count);
            AddWaterEdge(a, b, da, db, polygon, ref count);
            AddWaterEdge(b, c, db, dc, polygon, ref count);
            for (int i = 1; i + 1 < count; i++)
            {
                AddWaterVertex(polygon[0]);
                AddWaterVertex(polygon[i]);
                AddWaterVertex(polygon[i + 1]);
            }
        }

        private void AddWaterVertex(Vector4 point) => AddWaterVertex(new Vector3(point.x, point.y, point.z), point.w);

        private void AddWaterVertex(Vector3 point, float depth)
        {
            water.Add(point);
            // The shader uses absolute water level to blend out the inland shallows at an estuary.
            waterDepths.Add(new Vector2(depth, point.y + elevation));
        }

        private static void AddWaterEdge(Vector3 a, Vector3 b, float depthA, float depthB, Span<Vector4> polygon, ref int count)
        {
            if ((depthA > 0f) != (depthB > 0f))
            {
                Vector3 edge = Vector3.LerpUnclamped(a, b, depthA / (depthA - depthB));
                polygon[count++] = new Vector4(edge.x, edge.y, edge.z, 0f);
            }
            if (depthB > 0f)
                polygon[count++] = new Vector4(b.x, b.y, b.z, depthB);
        }

        private struct Cell
        {
            internal readonly int X, Z, Size, Detail;
            internal int Priority;
            internal bool HasSpine;
            internal System.Numerics.Vector2 Spine;

            internal Cell(int x, int z, int size, int detail)
            {
                X = x;
                Z = z;
                Size = size;
                Detail = detail;
                Priority = 0;
                HasSpine = false;
                Spine = default;
            }

            internal bool Before(Cell other) => Priority > other.Priority
                || Priority == other.Priority && (Z < other.Z || Z == other.Z && X < other.X);

            internal Vector2Int Southwest => new(X, Z);
            internal Vector2Int Northwest => new(X, Z + Size);
            internal Vector2Int Northeast => new(X + Size, Z + Size);
            internal Vector2Int Southeast => new(X + Size, Z);
            internal Vector2Int Center => new(X + Size / 2, Z + Size / 2);
        }

        private readonly struct Sample
        {
            internal readonly float Ground, Level;
            internal float Depth => Level - Ground;

            internal Sample(float ground, float level)
            {
                Ground = ground;
                Level = level;
            }
        }
    }
}
