using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Reol2.World
{
    internal sealed class WorldHorizon
    {
        private const int Grid = 16;
        private const int BaseWidth = Grid * ContinentField.ChunkSize;
        private const int RootLevel = 10;
        private const int CoverageSize = 16;
        private const int MaximumTiles = 224;
        private const int SelectionLimit = 176;
        private const int RegionalCellsPerSlice = 32;
        private const int GridVertices = (Grid + 1) * (Grid + 1);
        private const int VertexCount = GridVertices + 4 * (Grid + 1);
        private static readonly int MaskTexture = Shader.PropertyToID("_ResidentTerrain");
        private static readonly int MaskOrigin = Shader.PropertyToID("_ResidentTerrainOrigin");
        private static readonly int CaveCoverageTexture = Shader.PropertyToID("_CaveCoverage");
        private static readonly int CaveCoverageBounds = Shader.PropertyToID("_CaveCoverageBounds");
        private static readonly int WaterOrigin = Shader.PropertyToID("_WaterOrigin");
        private static readonly int[] Indices = CreateIndices();

        private readonly ContinentField field;
        private readonly Transform root;
        private readonly Material material;
        private readonly Material residentMaterial;
        private readonly Material caveMaterial;
        private readonly OceanSurface ocean;
        private readonly RegionalSurfaceTile regionalSurface;
        private readonly Material regionalWater;
        private readonly Texture3D coverage;
        private readonly byte[] coverageData = new byte[CoverageSize * CoverageSize * CoverageSize];
        private readonly Vector3[] vertices = new Vector3[VertexCount];
        private readonly Vector3[] normals = new Vector3[VertexCount];
        private readonly Color32[] colors = new Color32[VertexCount];
        private readonly Vector2[] skirtOffsets = new Vector2[VertexCount];
        private readonly float[] heights = new float[(Grid + 3) * (Grid + 3)];
        private readonly Dictionary<TileKey, Tile> tiles = new(MaximumTiles);
        private readonly Stack<Tile> spare = new(MaximumTiles);
        private readonly HashSet<TileKey> desired = new(SelectionLimit);
        private readonly List<TileKey> roots = new(16);
        private readonly List<Request> queue = new(SelectionLimit);
        private readonly List<Request> refinements = new(SelectionLimit);
        private readonly HashSet<TileKey> visible = new(SelectionLimit);
        private readonly HashSet<TileKey> previouslyVisible = new(SelectionLimit);
        private bool haveSelection;
        private bool haveCoverage;
        private bool sharedWaterDirty;
        private int next;
        private int revision;
        private bool buildingRegional;
        private bool regionalPrepared;
        private int regionalEmitted;
        private int regionalStartedAt;
        private TileKey regionalKey;
        private WorldPosition focusAtSelection;

        internal int ResidentTiles => tiles.Count;
        internal int PendingTiles => queue.Count - next + (buildingRegional ? 1 : 0);
        internal int RegionalTilesBuilt { get; private set; }
        internal int RegionalCellVisits { get; private set; }
        internal int LongestRegionalTileBuild { get; private set; }
        internal int SharedInlandWaterTriangles { get; private set; }
        internal Material ResidentMaterial => residentMaterial;
        internal Material CaveMaterial => caveMaterial;
        internal int VisibleTriangles
        {
            get
            {
                int count = 0;
                foreach (TileKey key in visible)
                    count += tiles[key].Triangles;
                return count;
            }
        }
        internal Vector3Int CoverageCenter { get; private set; }

        internal WorldHorizon(Transform parent, ContinentField field, Material source, OceanSurface ocean, Material water)
        {
            this.field = field;
            this.ocean = ocean;
            root = new GameObject("Streamed land and seabed").transform;
            root.SetParent(parent, false);
            if (field.Surface.HasWaterTiles)
            {
                regionalSurface = new RegionalSurfaceTile(field);
                regionalWater = new Material(water);
            }
            material = new Material(source);
            material.EnableKeyword("REOL_HEIGHT_PROXY");
            material.SetShaderPassEnabled("DepthOnly", false);
            residentMaterial = new Material(source);
            residentMaterial.SetFloat("_TerrainStencilWrite", 1f);
            // The coarse cave wall fills pixels beyond the exact surface's silhouette.
            caveMaterial = new Material(source);
            caveMaterial.EnableKeyword("REOL_CAVE_PROXY");
            caveMaterial.SetShaderPassEnabled("DepthOnly", false);
            caveMaterial.SetFloat("_TerrainStencilCompare", (float)CompareFunction.NotEqual);
            caveMaterial.renderQueue = (int)RenderQueue.Geometry + 1;
            coverage = new Texture3D(CoverageSize, CoverageSize, CoverageSize, TextureFormat.R8, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "Resident terrain coverage"
            };
            material.SetTexture(MaskTexture, coverage);
            caveMaterial.SetTexture(MaskTexture, coverage);
        }

        internal void SetCaveCoverageTexture(Texture3D texture) => material.SetTexture(CaveCoverageTexture, texture);

        internal void SetCaveCoverageBounds(Vector4 bounds) => material.SetVector(CaveCoverageBounds, bounds);

        internal void UpdateCoverage<T>(Dictionary<Vector3Int, T> ready, Vector3Int focus, WorldFrame frame)
        {
            haveCoverage = true;
            CoverageCenter = focus;
            Vector3Int min = focus - Vector3Int.one * (CoverageSize / 2);
            Array.Clear(coverageData, 0, coverageData.Length);
            foreach (Vector3Int page in ready.Keys)
            {
                Vector3Int local = page - min;
                if ((uint)local.x < CoverageSize && (uint)local.y < CoverageSize && (uint)local.z < CoverageSize)
                    coverageData[local.x + CoverageSize * (local.y + CoverageSize * local.z)] = field.HasEdits(page) ? (byte)255 : (byte)127;
            }
            coverage.SetPixelData(coverageData, 0);
            coverage.Apply(false);
            PlaceCoverage(min, frame);
        }

        internal void Stream(WorldPosition focus, WorldFrame frame, bool entry, double budgetMs, float range)
        {
            bool moved = !haveSelection || Math.Abs(focus.X - focusAtSelection.X) >= 48
                || Math.Abs(focus.Z - focusAtSelection.Z) >= 48 || Math.Abs(focus.Y - focusAtSelection.Y) >= 32;
            bool changed = entry || moved;
            if (changed)
            {
                focusAtSelection = focus;
                Select(focus, range);
                haveSelection = true;
                if (entry)
                    UpdateVisible(frame);
            }
            if (buildingRegional && changed && !desired.Contains(regionalKey))
                buildingRegional = false;

            long started = Stopwatch.GetTimestamp();
            int limit = entry ? 12 : budgetMs > 0 ? 2 : 0;
            int completed = 0;
            while (completed < limit && (buildingRegional || next < queue.Count))
            {
                if (buildingRegional)
                {
                    if (!regionalPrepared)
                        regionalPrepared = regionalSurface.Prepare(RegionalCellsPerSlice);
                    else
                    {
                        regionalSurface.Emit(regionalEmitted, RegionalCellsPerSlice);
                        regionalEmitted += RegionalCellsPerSlice;
                    }
                    if (regionalPrepared && regionalEmitted >= regionalSurface.CellCount)
                    {
                        if (tiles.Count == MaximumTiles && !RecycleOldest())
                            break;
                        PublishTile(regionalKey, frame, regional: true);
                        RegionalTilesBuilt++;
                        RegionalCellVisits += regionalSurface.CellVisits;
                        LongestRegionalTileBuild = Math.Max(LongestRegionalTileBuild, Time.frameCount - regionalStartedAt);
                        buildingRegional = false;
                        completed++;
                        changed = true;
                    }
                }
                else
                {
                    TileKey tile = queue[next++].Key;
                    if (tiles.ContainsKey(tile))
                        continue;
                    int step = tile.Width / Grid;
                    if (regionalSurface != null && field.Surface.WaterFeaturesIn(tile.X, tile.Z, tile.Width) != WaterFeatures.None)
                    {
                        regionalKey = tile;
                        regionalSurface.Begin(tile.X, tile.Z, step, Grid);
                        regionalPrepared = false;
                        regionalEmitted = 0;
                        regionalStartedAt = Time.frameCount;
                        buildingRegional = true;
                    }
                    else
                    {
                        if (tiles.Count == MaximumTiles && !RecycleOldest())
                        {
                            next--;
                            break;
                        }
                        PublishTile(tile, frame, regional: false);
                        completed++;
                        changed = true;
                    }
                }
                if ((Stopwatch.GetTimestamp() - started) * (1000d / Stopwatch.Frequency) >= budgetMs
                    && (!entry || regionalSurface != null && (visible.Count > 0 || completed > 0)))
                    break;
            }

            if (changed)
                UpdateVisible(frame);
        }

        private void Select(WorldPosition focus, float range)
        {
            const int rootWidth = BaseWidth << RootLevel;
            float radius = range * 1.08f;
            int x0 = MinimumRoot(focus.X - radius, rootWidth);
            int z0 = MinimumRoot(focus.Z - radius, rootWidth);
            int x1 = MinimumRoot(focus.X + radius, rootWidth);
            int z1 = MinimumRoot(focus.Z + radius, rootWidth);
            desired.Clear();
            roots.Clear();
            queue.Clear();
            refinements.Clear();
            revision++;
            next = 0;
            for (int z = z0; z <= z1; z += rootWidth)
            for (int x = x0; x <= x1; x += rootWidth)
            {
                var rootKey = new TileKey(x, z, RootLevel);
                roots.Add(rootKey);
                SelectTile(rootKey, focus, radius, -1000000);
            }

            while (refinements.Count > 0 && desired.Count + 4 <= SelectionLimit)
            {
                Request choice = TakeRefinement();
                // Build complete child groups near the player while the distant
                // groups still have their parent mesh. Each ancestor comes first.
                float buildPriority = regionalSurface != null
                    ? (float)Math.Sqrt(HorizontalDistanceSquared(choice.Key, focus)) - choice.Key.Level
                    : choice.Priority - choice.Key.Level * 2f;
                for (int i = 0; i < 4; i++)
                    SelectTile(choice.Key.Child(i), focus, radius, buildPriority);
            }
            queue.Sort();
        }

        private void SelectTile(TileKey tile, WorldPosition focus, float radius, float buildPriority)
        {
            desired.Add(tile);
            if (tiles.TryGetValue(tile, out Tile found))
                found.LastUse = revision;
            else
                queue.Add(new Request(tile, buildPriority));
            if (tile.Level == 0)
                return;
            float wide = tile.Width;
            double horizontal = HorizontalDistanceSquared(tile, focus);
            float max = Math.Min(radius, wide * 1.15f);
            if (horizontal > max * (double)max)
                return;

            // Distance to the surface drives overhead LOD as well as lateral LOD.
            int x = Math.Clamp(focus.Cell.x, tile.X, tile.X + tile.Width);
            int z = Math.Clamp(focus.Cell.z, tile.Z, tile.Z + tile.Width);
            float reference = field.HeightAt((double)x, z);
            double dy = Math.Max(0, focus.Y - reference);
            double distance = Math.Sqrt(horizontal + dy * dy);
            if (distance > wide * 1.15f)
                return;

            var candidate = new Request(tile, (float)(distance / wide));
            int index = refinements.Count;
            refinements.Add(candidate);
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (refinements[parent].CompareTo(candidate) <= 0)
                    break;
                refinements[index] = refinements[parent];
                index = parent;
            }
            refinements[index] = candidate;
        }

        private static double HorizontalDistanceSquared(TileKey tile, WorldPosition focus)
        {
            double dx = Math.Max(0, Math.Max(tile.X - focus.X, focus.X - (tile.X + tile.Width)));
            double dz = Math.Max(0, Math.Max(tile.Z - focus.Z, focus.Z - (tile.Z + tile.Width)));
            return dx * dx + dz * dz;
        }

        private Request TakeRefinement()
        {
            Request first = refinements[0];
            int count = refinements.Count - 1;
            Request last = refinements[count];
            refinements.RemoveAt(count);
            if (count == 0)
                return first;
            int index = 0;
            while (index * 2 + 1 < count)
            {
                int child = index * 2 + 1;
                if (child + 1 < count && refinements[child + 1].CompareTo(refinements[child]) < 0)
                    child++;
                if (last.CompareTo(refinements[child]) <= 0)
                    break;
                refinements[index] = refinements[child];
                index = child;
            }
            refinements[index] = last;
            return first;
        }

        private void UpdateVisible(WorldFrame frame)
        {
            previouslyVisible.Clear();
            previouslyVisible.UnionWith(visible);
            visible.Clear();
            foreach (TileKey key in roots)
                AddVisible(key);
            foreach (TileKey key in previouslyVisible)
                if (!visible.Contains(key) && tiles.TryGetValue(key, out Tile old))
                {
                    old.Show(false);
                    sharedWaterDirty |= old.SharedWaterLevel.HasValue;
                }
            foreach (TileKey key in visible)
                if (!previouslyVisible.Contains(key))
                {
                    Tile tile = tiles[key];
                    tile.Show(true);
                    sharedWaterDirty |= tile.SharedWaterLevel.HasValue;
                }
            if (sharedWaterDirty)
                PublishSharedWater(frame);
        }

        private void PublishSharedWater(WorldFrame frame)
        {
            sharedWaterDirty = false;
            SharedInlandWaterTriangles = 0;
            List<Vector3> output = ocean.Vertices;
            output.Clear();
            foreach (TileKey key in visible)
            {
                Tile tile = tiles[key];
                if (tile.SharedWaterLevel is not float level)
                    continue;
                int x = key.X - frame.Origin.x, z = key.Z - frame.Origin.z;
                if (tile.Shore == null || tile.Shore.Count == 0)
                {
                    OceanSurface.AppendQuad(output, x, z, x + key.Width, z + key.Width, level);
                    SharedInlandWaterTriangles += level == 0f ? 0 : 2;
                }
                else
                {
                    var offset = new Vector3(x, 0, z);
                    foreach (Vector3 vertex in tile.Shore)
                        output.Add(offset + vertex);
                }
            }
            ocean.Publish(frame);
        }

        private void AddVisible(TileKey key)
        {
            if (!tiles.ContainsKey(key))
                return;
            if (key.Level != 0 && CanSplit(key))
            {
                for (int i = 0; i < 4; i++)
                    AddVisible(key.Child(i));
            }
            else
                visible.Add(key);
        }

        private bool CanSplit(TileKey key)
        {
            if (!desired.Contains(key.Child(0)))
                return false;
            for (int i = 0; i < 4; i++)
                if (!tiles.ContainsKey(key.Child(i)))
                    return false;
            return true;
        }

        private bool RecycleOldest()
        {
            Tile victim = null;
            TileKey selected = default;
            foreach (var entry in tiles)
            {
                if (!desired.Contains(entry.Key) && (victim == null || entry.Value.LastUse < victim.LastUse))
                {
                    victim = entry.Value;
                    selected = entry.Key;
                }
            }
            if (victim == null)
                return false;
            victim.Show(false);
            if (visible.Remove(selected))
                sharedWaterDirty |= victim.SharedWaterLevel.HasValue;
            spare.Push(victim);
            tiles.Remove(selected);
            return true;
        }

        private void PublishTile(TileKey key, WorldFrame frame, bool regional)
        {
            Tile tile = spare.Count > 0 ? spare.Pop() : new Tile(root, material);
            int step = key.Width / Grid;
            if (regional)
            {
                tile.Elevation = regionalSurface.Publish(tile.Mesh);
                tile.PublishRegionalWater(regionalSurface, regionalWater);
                tile.SharedWaterLevel = regionalSurface.SharedWaterLevel;
                tile.Shore?.Clear();
                tile.HasIndices = false;
            }
            else
            {
                tile.HasRegionalWater = false;
                BuildRegular(tile, key, step);
            }
            tile.Triangles = (int)tile.Mesh.GetIndexCount(0) / 3;
            tile.LastUse = revision;
            tile.Place(key, frame);
            tiles.Add(key, tile);
        }

        private void BuildRegular(Tile tile, TileKey key, int step)
        {
            const int row = Grid + 3;
            for (int z = 0; z < row; z++)
            for (int x = 0; x < row; x++)
                heights[x + z * row] = field.HeightNode(key.X + (x - 1) * step, key.Z + (z - 1) * step);

            float low = float.PositiveInfinity;
            float high = float.NegativeInfinity;
            float baseHeight = Mathf.Floor(heights[row + 1] / 64f) * 64f;
            for (int z = 0; z <= Grid; z++)
            for (int x = 0; x <= Grid; x++)
            {
                int sample = x + 1 + (z + 1) * row;
                int vertex = x + z * (Grid + 1);
                float y = heights[sample];
                vertices[vertex] = new Vector3(x * step, y - baseHeight, z * step);
                normals[vertex] = new Vector3(heights[sample - 1] - heights[sample + 1], 2 * step,
                    heights[sample - row] - heights[sample + row]).normalized;
                var point = new WorldPosition(key.X + x * step, y, key.Z + z * step);
                colors[vertex] = field.MaterialAt(point, normals[vertex], y, step);
                low = Math.Min(low, y - baseHeight);
                high = Math.Max(high, y - baseHeight);
            }

            float skirt = Math.Max(0.20f, step * 1.25f);
            for (int edge = 0; edge < 4; edge++)
            for (int point = 0; point <= Grid; point++)
            {
                int top = EdgeVertex(edge, point);
                int bottom = GridVertices + edge * (Grid + 1) + point;
                vertices[bottom] = vertices[top] - Vector3.up * skirt;
                normals[bottom] = normals[top];
                colors[bottom] = colors[top];
                skirtOffsets[bottom] = new Vector2(skirt, 0);
            }

            if (!tile.HasIndices)
                tile.Mesh.Clear(keepVertexLayout: true);
            tile.Mesh.SetVertices(vertices);
            tile.Mesh.SetNormals(normals);
            tile.Mesh.SetColors(colors);
            tile.Mesh.SetUVs(0, skirtOffsets);
            if (!tile.HasIndices)
            {
                tile.Mesh.SetTriangles(Indices, 0, calculateBounds: false);
                tile.HasIndices = true;
            }
            float half = key.Width * 0.5f;
            tile.Mesh.bounds = new Bounds(new Vector3(half, (high + low - skirt) / 2f, half),
                new Vector3(key.Width, high - low + skirt, key.Width));
            bool hasSea = low + baseHeight < 0f && (regionalSurface == null
                || field.Surface.NearConnectedSea(key.X + half, key.Z + half));
            tile.SharedWaterLevel = hasSea ? 0f : null;
            tile.Shore?.Clear();
            if (hasSea && high + baseHeight >= 0f)
            {
                tile.Shore ??= new List<Vector3>();
                OceanSurface.AppendShore(tile.Shore, heights, row, row + 1, Grid, step);
            }
            tile.Elevation = (int)baseHeight;
        }

        internal void Reposition(WorldFrame frame)
        {
            if (regionalWater != null)
                regionalWater.SetVector(WaterOrigin, new Vector4(frame.Origin.x / 128, frame.Origin.z / 128, 0, 0));
            foreach (var pair in tiles)
                pair.Value.Place(pair.Key, frame);
            if (haveCoverage)
            {
                Vector3Int min = CoverageCenter - Vector3Int.one * (CoverageSize / 2);
                PlaceCoverage(min, frame);
            }
        }

        private void PlaceCoverage(Vector3Int minimum, WorldFrame frame)
        {
            Vector3 origin = minimum * ContinentField.ChunkSize - frame.Origin;
            material.SetVector(MaskOrigin, origin);
            caveMaterial.SetVector(MaskOrigin, origin);
        }

        internal int VisibleLevelAt(WorldPosition point)
        {
            foreach (TileKey key in visible)
                if (point.X >= key.X && point.X < key.X + key.Width && point.Z >= key.Z && point.Z < key.Z + key.Width)
                    return key.Level;
            return -1;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || REOL_TEST_BUILD
        internal WorldRenderReport.TileState[] ReportVisibleTiles(WorldFrame frame)
        {
            var result = new WorldRenderReport.TileState[visible.Count];
            int index = 0;
            foreach (TileKey key in visible)
                result[index++] = tiles[key].Report(key, frame);
            return result;
        }
#endif

        internal void Release()
        {
            foreach (Tile tile in tiles.Values)
                tile.Release();
            foreach (Tile tile in spare)
                tile.Release();
            Object.Destroy(coverage);
            Object.Destroy(material);
            Object.Destroy(residentMaterial);
            Object.Destroy(caveMaterial);
            if (regionalWater != null)
                Object.Destroy(regionalWater);
        }

        private static int MinimumRoot(double coordinate, int width) =>
            (int)Math.Floor(Math.Clamp(coordinate, -WorldDefinition.Extent, WorldDefinition.Extent - 1) / width) * width;

        private static int EdgeVertex(int edge, int point) => edge switch
        {
            0 => Grid - point,
            1 => point * (Grid + 1),
            2 => point + Grid * (Grid + 1),
            _ => Grid + (Grid - point) * (Grid + 1)
        };

        private static int[] CreateIndices()
        {
            var output = new int[6 * (Grid * Grid + Grid * 4)];
            int next = 0;
            for (int z = 0; z < Grid; z++)
            for (int x = 0; x < Grid; x++)
            {
                int a = x + z * (Grid + 1);
                int b = a + 1;
                int c = a + Grid + 1;
                output[next++] = a; output[next++] = c; output[next++] = b;
                output[next++] = b; output[next++] = c; output[next++] = c + 1;
            }
            for (int edge = 0; edge < 4; edge++)
            for (int point = 0; point < Grid; point++)
            {
                int a = EdgeVertex(edge, point);
                int b = EdgeVertex(edge, point + 1);
                int c = GridVertices + edge * (Grid + 1) + point;
                output[next++] = a; output[next++] = c; output[next++] = b;
                output[next++] = b; output[next++] = c; output[next++] = c + 1;
            }
            return output;
        }

        private readonly struct TileKey : IEquatable<TileKey>
        {
            internal readonly int X, Z, Level;
            internal int Width => BaseWidth << Level;

            internal TileKey(int x, int z, int level) { X = x; Z = z; Level = level; }
            internal TileKey Child(int i) => new(X + (i & 1) * (Width / 2), Z + (i >> 1) * (Width / 2), Level - 1);
            public bool Equals(TileKey other) => X == other.X && Z == other.Z && Level == other.Level;
            public override bool Equals(object obj) => obj is TileKey key && Equals(key);
            public override int GetHashCode() => unchecked((int)WorldNoise.Hash(X >> 7, Z >> 7, (ulong)Level));
        }

        private readonly struct Request : IComparable<Request>
        {
            internal readonly TileKey Key;
            internal readonly float Priority;

            internal Request(TileKey key, float priority) { Key = key; Priority = priority; }
            public int CompareTo(Request other)
            {
                int order = Priority.CompareTo(other.Priority);
                return order != 0 ? order : Key.Level.CompareTo(other.Key.Level);
            }
        }

        private sealed class Tile
        {
            private readonly Transform transform;
            internal readonly Mesh Mesh;
            private readonly MeshRenderer renderer;
            private Mesh water;
            private MeshRenderer waterRenderer;
            internal int Elevation;
            internal int Triangles;
            internal int LastUse;
            internal bool HasIndices;
            internal float? SharedWaterLevel;
            internal bool HasRegionalWater;
            internal List<Vector3> Shore;

            internal Tile(Transform root, Material material)
            {
                var obj = new GameObject("Horizon tile", typeof(MeshFilter), typeof(MeshRenderer));
                transform = obj.transform;
                transform.SetParent(root, false);
                renderer = obj.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.enabled = false;
                Mesh = new Mesh { name = "Streamed horizon surface" };
                Mesh.MarkDynamic();
                obj.GetComponent<MeshFilter>().sharedMesh = Mesh;
            }

            internal void Place(TileKey key, WorldFrame frame) =>
                transform.localPosition = (Vector3)(new Vector3Int(key.X, Elevation, key.Z) - frame.Origin);

            internal void Show(bool show)
            {
                renderer.enabled = show;
                if (waterRenderer != null)
                    waterRenderer.enabled = show && HasRegionalWater;
            }

            internal void PublishRegionalWater(RegionalSurfaceTile surface, Material material)
            {
                HasRegionalWater = !surface.SharedWaterLevel.HasValue && surface.WaterVertexCount != 0;
                if (!HasRegionalWater)
                    return;
                if (water == null)
                {
                    var obj = new GameObject("Water surface", typeof(MeshFilter), typeof(MeshRenderer));
                    obj.transform.SetParent(transform, false);
                    water = new Mesh { name = "Regional water surface", indexFormat = IndexFormat.UInt32 };
                    water.MarkDynamic();
                    obj.GetComponent<MeshFilter>().sharedMesh = water;
                    waterRenderer = obj.GetComponent<MeshRenderer>();
                    waterRenderer.sharedMaterial = material;
                    waterRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    waterRenderer.enabled = false;
                }
                surface.PublishWater(water);
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || REOL_TEST_BUILD
            internal WorldRenderReport.TileState Report(TileKey key, WorldFrame frame) => new()
            {
                x = key.X,
                z = key.Z,
                width = key.Width,
                lod = key.Level,
                terrainTriangles = Triangles,
                terrainVisible = renderer.enabled,
                minTerrainY = renderer.bounds.min.y + frame.Origin.y,
                maxTerrainY = renderer.bounds.max.y + frame.Origin.y,
                sharedSea = SharedWaterLevel == 0f,
                sharedWaterLevel = SharedWaterLevel.GetValueOrDefault(),
                clippedSeaTriangles = SharedWaterLevel == 0f ? (Shore?.Count ?? 0) / 3 : 0,
                regionalWaterTriangles = waterRenderer != null && waterRenderer.enabled
                    ? (int)water.GetIndexCount(0) / 3 : 0
            };
#endif

            internal void Release()
            {
                Object.Destroy(Mesh);
                if (water != null)
                    Object.Destroy(water);
            }
        }
    }
}
