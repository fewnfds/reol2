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
        private const float RefineDistance = 1.35f;
        private const float RetainDistance = 1.55f;
        private const float RefinePixels = 1.5f;
        private const float RetainPixels = 1f;
        private const float NearDetailDistance = 96f;
        private const int GridVertices = (Grid + 1) * (Grid + 1);
        private const int VertexCount = GridVertices + 4 * (Grid + 1);
        private static readonly int MaskTexture = Shader.PropertyToID("_ResidentTerrain");
        private static readonly int MaskOrigin = Shader.PropertyToID("_ResidentTerrainOrigin");
        private static readonly int CaveCoverageTexture = Shader.PropertyToID("_CaveCoverage");
        private static readonly int CaveCoverageOrigin = Shader.PropertyToID("_CaveCoverageOrigin");
        private static readonly int CaveCoverageScale = Shader.PropertyToID("_CaveCoverageScale");
        private static readonly int CaveEntranceCount = Shader.PropertyToID("_CaveEntranceCount");
        private static readonly int CaveEntrances = Shader.PropertyToID("_CaveEntrances");
        private static readonly int WaterOrigin = Shader.PropertyToID("_WaterOrigin");
        private static readonly int[] Indices = CreateIndices();

        private readonly ContinentField field;
        private readonly Transform root;
        private readonly Material material;
        private readonly Material residentMaterial;
        private readonly Material caveMaterial;
        private readonly Material caveEntranceMaterial;
        private readonly OceanSurface ocean;
        private readonly RegionalSurfaceTile regionalSurface;
        private readonly Material regionalWater;
        private readonly Texture3D coverage;
        private readonly byte[] coverageData = new byte[CoverageSize * CoverageSize * CoverageSize];
        private readonly Vector3[] vertices = new Vector3[VertexCount];
        private readonly List<Vector3> stitchedVertices = new(VertexCount);
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
        private readonly HashSet<TileKey> refined = new(SelectionLimit);
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
        private WorldPosition eyeAtSelection;
        private float pixelScaleAtSelection;
        private float horizontalSelectionStep = 48f;
        private float verticalSelectionStep = 32f;
        private bool measuredNewError;

        internal int ResidentTiles => tiles.Count;
        internal int PendingTiles => queue.Count - next + (buildingRegional ? 1 : 0);
        internal int RegionalTilesBuilt { get; private set; }
        internal int RegularTilesBuilt { get; private set; }
        internal int RegionalCellVisits { get; private set; }
        internal int LongestRegionalTileBuild { get; private set; }
        internal int SharedInlandWaterTriangles { get; private set; }
        internal Material ResidentMaterial => residentMaterial;
        internal Material CaveMaterial => caveMaterial;
        internal Material CaveEntranceMaterial => caveEntranceMaterial;
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
            caveMaterial.SetFloat("_TerrainStencilRef", 0f);
            caveMaterial.SetFloat("_TerrainStencilRead", 3f);
            caveMaterial.SetFloat("_TerrainStencilCompare", (float)CompareFunction.Equal);
            caveMaterial.renderQueue = (int)RenderQueue.Geometry + 2;
            caveEntranceMaterial = new Material(caveMaterial);
            // Test the resident bit and write a separate entrance bit so entrance
            // triangles at the same LOD still compete normally in the depth buffer.
            caveEntranceMaterial.SetFloat("_TerrainStencilRef", 3f);
            caveEntranceMaterial.SetFloat("_TerrainStencilRead", 1f);
            caveEntranceMaterial.SetFloat("_TerrainStencilCompare", (float)CompareFunction.NotEqual);
            caveEntranceMaterial.SetFloat("_TerrainStencilWrite", 2f);
            caveEntranceMaterial.renderQueue = (int)RenderQueue.Geometry + 1;
            coverage = new Texture3D(CoverageSize, CoverageSize, CoverageSize, TextureFormat.R8, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "Resident terrain coverage"
            };
            material.SetTexture(MaskTexture, coverage);
            caveMaterial.SetTexture(MaskTexture, coverage);
            caveEntranceMaterial.SetTexture(MaskTexture, coverage);
        }

        internal void SetCaveCoverageTexture(Texture3D texture) => material.SetTexture(CaveCoverageTexture, texture);

        internal void SetCaveCoverageBounds(Vector3 origin, Vector3 scale)
        {
            material.SetVector(CaveCoverageOrigin, origin);
            material.SetVector(CaveCoverageScale, scale);
        }

        internal void SetCaveEntrances(Vector4[] bounds, int count)
        {
            material.SetFloat(CaveEntranceCount, count);
            if (count != 0)
                material.SetVectorArray(CaveEntrances, bounds);
        }

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

        internal void Stream(WorldPosition focus, WorldPosition eye, WorldFrame frame, bool entry, double budgetMs,
            float range, float pixelScale)
        {
            long started = Stopwatch.GetTimestamp();
            bool moved = !haveSelection || Math.Abs(focus.X - focusAtSelection.X) >= horizontalSelectionStep
                || Math.Abs(focus.Z - focusAtSelection.Z) >= horizontalSelectionStep
                || Math.Abs(focus.Y - focusAtSelection.Y) >= verticalSelectionStep;
            bool projectionChanged = pixelScale != pixelScaleAtSelection;
            bool canReselect = budgetMs > 0 && measuredNewError && !buildingRegional && next >= queue.Count;
            bool changed = entry || moved || projectionChanged || canReselect;
            if (changed)
            {
                focusAtSelection = focus;
                eyeAtSelection = eye;
                pixelScaleAtSelection = pixelScale;
                measuredNewError = false;
                Select(focus, eye, range, pixelScale);
                haveSelection = true;
                // At altitude, short steps barely change the ground projection. Avoid rebuilding the
                // queue on each of those steps while keeping the original cadence near the ground.
                float height = (float)(eye.Y - field.HeightAt(eye.X, eye.Z));
                horizontalSelectionStep = Mathf.Clamp(height * 0.16f, 48f, 512f);
                verticalSelectionStep = Mathf.Clamp(height * 0.08f, 32f, 256f);
                if (entry)
                    UpdateVisible(frame);
            }
            if (buildingRegional && changed && !desired.Contains(regionalKey))
                buildingRegional = false;

            int limit = entry ? 12 : ElapsedMs(started) < budgetMs ? 2 : 0;
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
                if (ElapsedMs(started) >= budgetMs
                    && (!entry || regionalSurface != null && (visible.Count > 0 || completed > 0)))
                    break;
            }

            if (changed)
                UpdateVisible(frame);
        }

        private static double ElapsedMs(long started) => (Stopwatch.GetTimestamp() - started) * (1000d / Stopwatch.Frequency);

        private void Select(WorldPosition focus, WorldPosition eye, float range, float pixelScale)
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
                SelectTile(rootKey, eye, radius, pixelScale, -1000000);
            }

            while (refinements.Count > 0 && desired.Count + 4 <= SelectionLimit)
            {
                Request choice = TakeRefinement();
                // Build complete child groups near the player while the distant
                // groups still have their parent mesh. Each ancestor comes first.
                float buildPriority = regionalSurface != null
                    ? (float)Math.Sqrt(HorizontalDistanceSquared(choice.Key, focus)) - choice.Key.Level
                    : (float)(Math.Sqrt(HorizontalDistanceSquared(choice.Key, focus)) / choice.Key.Width)
                        - choice.Key.Level * 2f;
                for (int i = 0; i < 4; i++)
                    SelectTile(choice.Key.Child(i), eye, radius, pixelScale, buildPriority);
            }
            queue.Sort();
        }

        private void SelectTile(TileKey tile, WorldPosition eye, float radius, float pixelScale, float buildPriority)
        {
            desired.Add(tile);
            if (tiles.TryGetValue(tile, out Tile found))
                found.LastUse = revision;
            else
                queue.Add(new Request(tile, buildPriority));
            if (tile.Level == 0)
                return;
            float wide = tile.Width;
            bool wasRefined = refined.Contains(tile);
            float fallbackRange = wide * (wasRefined ? RetainDistance : RefineDistance);
            bool adaptive = found != null && found.AdaptiveLod;
            float lodRange = adaptive ? radius : fallbackRange;
            double horizontal = HorizontalDistanceSquared(tile, eye);
            float max = Math.Min(radius, lodRange);
            if (horizontal > max * (double)max)
                return;

            double dy;
            if (adaptive)
                dy = Math.Max(0d, Math.Max(found.MinimumHeight - eye.Y, eye.Y - found.MaximumHeight));
            else
            {
                int x = Math.Clamp(eye.Cell.x, tile.X, tile.X + tile.Width);
                int z = Math.Clamp(eye.Cell.z, tile.Z, tile.Z + tile.Width);
                dy = Math.Max(0, eye.Y - field.HeightAt((double)x, z));
            }
            double distance = Math.Sqrt(horizontal + dy * dy);
            if (distance > lodRange)
                return;

            float ratio = (float)(distance / wide);
            // Keep the height tiles immediately around the playable surface ahead of remote detail.
            float near = wasRefined ? 0.9f : 0.75f;
            float priority = ratio - (wasRefined ? 0.15f : 0f);
            if (distance <= NearDetailDistance && ratio <= near)
                priority -= 1000f;
            else if (adaptive)
            {
                float error = found.ChildError ?? found.CoarseningError;
                float threshold = wasRefined ? RetainPixels : RefinePixels;
                float projected = ProjectError(error, distance, pixelScale);
                if (projected <= threshold)
                    return;
                priority = threshold / projected - (wasRefined ? 0.15f : 0f);
            }

            var candidate = new Request(tile, priority);
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

        internal static float PixelScale(int height, float verticalFov) =>
            Math.Max(1, height) * 0.5f / Mathf.Tan(verticalFov * (Mathf.Deg2Rad * 0.5f));

        internal static float ProjectError(float metres, double distance, float pixelScale) =>
            (float)(metres * pixelScale / Math.Max(1d, distance));

        internal static float MeasureCoarseningError(float[] samples, int origin, int row, int cells)
        {
            float maximum = 0f;
            for (int z = 0; z <= cells; z++)
            for (int x = 0; x <= cells; x++)
            {
                if (((x | z) & 1) == 0)
                    continue;
                int x0 = Math.Min(x & ~1, cells - 2);
                int z0 = Math.Min(z & ~1, cells - 2);
                int a = origin + x0 + z0 * row;
                float coarse = WorldSurface.Interpolate(samples[a], samples[a + 2], samples[a + 2 * row],
                    samples[a + 2 * row + 2], (x - x0) * 0.5f, (z - z0) * 0.5f);
                maximum = Math.Max(maximum, Mathf.Abs(samples[origin + x + z * row] - coarse));
            }
            return maximum;
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
            refined.Clear();
            foreach (TileKey key in roots)
                AddVisible(key);
            if (!sharedWaterDirty && visible.SetEquals(previouslyVisible))
                return;
            foreach (TileKey key in previouslyVisible)
                if (!visible.Contains(key) && tiles.TryGetValue(key, out Tile old))
                {
                    old.Show(false);
                    sharedWaterDirty |= old.SharedWaterLevel.HasValue;
                }
            foreach (TileKey key in visible)
            {
                Tile tile = tiles[key];
                if (!previouslyVisible.Contains(key))
                {
                    tile.Show(true);
                    sharedWaterDirty |= tile.SharedWaterLevel.HasValue;
                }
                StitchEdges(key, tile);
            }
            if (sharedWaterDirty)
                PublishSharedWater(frame);
        }

        private void StitchEdges(TileKey key, Tile tile)
        {
            var constraints = new Vector4(
                CoarserEdge(key, key.X, key.Z - 1, 0),
                CoarserEdge(key, key.X - 1, key.Z, 1),
                CoarserEdge(key, key.X, key.Z + key.Width, 2),
                CoarserEdge(key, key.X + key.Width, key.Z, 3));
            if (constraints == tile.EdgeConstraints)
                return;
            tile.Mesh.GetVertices(stitchedVertices);
            int spacing = key.Width / Grid;
            // With at most four levels of difference, both tile corners are already shared samples.
            for (int edge = 0; edge < 4; edge++)
            {
                if (constraints[edge] == tile.EdgeConstraints[edge])
                    continue;
                int constraint = (int)constraints[edge];
                if (!tile.HasIndices)
                {
                    foreach (BorderVertex border in tile.RegionalBorders[edge])
                    {
                        Vector3 vertex = stitchedVertices[border.Index];
                        int step = StitchStep(key, tile, vertex, edge, spacing, constraint);
                        vertex.y = step == 0 ? border.OriginalY
                            : EdgeHeight(key, vertex, step, edge) - tile.Elevation - border.Skirt;
                        stitchedVertices[border.Index] = vertex;
                    }
                    continue;
                }
                for (int i = 1; i < Grid; i++)
                {
                    int top = EdgeVertex(edge, i);
                    int bottom = GridVertices + edge * (Grid + 1) + i;
                    Vector3 vertex = stitchedVertices[top];
                    int step = StitchStep(key, tile, vertex, edge, spacing, constraint);
                    if (step == 0)
                        continue;
                    vertex.y = EdgeHeight(key, vertex, step, edge) - tile.Elevation;
                    stitchedVertices[top] = vertex;
                    stitchedVertices[bottom] = vertex - Vector3.up * (spacing * 1.25f);
                }
            }
            tile.EdgeConstraints = constraints;
            tile.Mesh.SetVertices(stitchedVertices);
            tile.Mesh.RecalculateBounds();
        }

        private static int StitchStep(TileKey key, Tile tile, Vector3 vertex, int edge, int spacing, int constraint)
        {
            bool alongX = edge is 0 or 2;
            int local = (int)(alongX ? vertex.x : vertex.z);
            if (local == 0 || local == key.Width || IsWetSpan(local, spacing, tile.WetBorderSpans[edge]))
                return 0;
            int coarseStep = spacing << (constraint >> Grid);
            int coarseLocal = ((alongX ? key.X : key.Z) + local) & (coarseStep * Grid - 1);
            return IsWetSpan(coarseLocal, coarseStep, constraint & ushort.MaxValue) ? spacing : coarseStep;
        }

        private static bool IsWetSpan(int along, int step, int mask)
        {
            int span = along / step;
            int touching = 1 << span;
            if (span > 0 && (along & (step - 1)) == 0)
                touching |= 1 << (span - 1);
            return (mask & touching) != 0;
        }

        private float EdgeHeight(TileKey key, Vector3 vertex, int step, int edge)
        {
            int x = key.X + (int)vertex.x, z = key.Z + (int)vertex.z;
            bool alongX = edge is 0 or 2;
            int offset = (alongX ? x : z) & (step - 1);
            int dx = alongX ? offset : 0, dz = alongX ? 0 : offset;
            float height = field.HeightNode(x - dx, z - dz);
            if (offset == 0)
                return height;
            float end = field.HeightNode(x - dx + (alongX ? step : 0), z - dz + (alongX ? 0 : step));
            return Mathf.LerpUnclamped(height, end, offset / (float)step);
        }

        private int CoarserEdge(TileKey key, int x, int z, int edge)
        {
            int last = Math.Min(RootLevel, key.Level + 4);
            for (int level = key.Level + 1; level <= last; level++)
            {
                int width = BaseWidth << level;
                var neighbor = new TileKey(x & -width, z & -width, level);
                if (visible.Contains(neighbor))
                    return ((level - key.Level) << Grid) | tiles[neighbor].WetBorderSpans[(edge + 2) & 3];
            }
            return 0;
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
                refined.Add(key);
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
            tile.ChildError = null;
            if (regional)
            {
                tile.Elevation = regionalSurface.Publish(tile.Mesh);
                tile.PublishRegionalWater(regionalSurface, regionalWater);
                tile.SharedWaterLevel = regionalSurface.SharedWaterLevel;
                tile.Shore?.Clear();
                tile.HasIndices = false;
                tile.ReadRegionalBorders(regionalSurface, key.Width);
            }
            else
            {
                tile.HasRegionalWater = false;
                BuildRegular(tile, key, step);
                RegularTilesBuilt++;
            }
            // Shorelines, water and the submerged seabed keep their existing streaming policy.
            tile.AdaptiveLod = !regional && !tile.SharedWaterLevel.HasValue;
            tile.Triangles = (int)tile.Mesh.GetIndexCount(0) / 3;
            tile.LastUse = revision;
            tile.Place(key, frame);
            tiles.Add(key, tile);
            measuredNewError |= tile.AdaptiveLod && key.Level > 0;
            if (key.Level < RootLevel)
                CaptureChildError(key.Parent);
        }

        private void CaptureChildError(TileKey key)
        {
            if (!tiles.TryGetValue(key, out Tile parent) || !parent.AdaptiveLod)
                return;
            float error = 0f;
            for (int i = 0; i < 4; i++)
            {
                if (!tiles.TryGetValue(key.Child(i), out Tile child))
                    return;
                if (!child.AdaptiveLod)
                {
                    parent.AdaptiveLod = false;
                    measuredNewError = true;
                    return;
                }
                error = Math.Max(error, child.CoarseningError);
            }
            parent.ChildError = error;
            measuredNewError = true;
        }

        private void BuildRegular(Tile tile, TileKey key, int step)
        {
            tile.EdgeConstraints = default;
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
            Array.Fill(tile.WetBorderSpans, hasSea ? ushort.MaxValue : (ushort)0);
            tile.SharedWaterLevel = hasSea ? 0f : null;
            tile.Shore?.Clear();
            if (hasSea && high + baseHeight >= 0f)
            {
                tile.Shore ??= new List<Vector3>();
                OceanSurface.AppendShore(tile.Shore, heights, row, row + 1, Grid, step);
            }
            tile.Elevation = (int)baseHeight;
            tile.MinimumHeight = low + baseHeight;
            tile.MaximumHeight = high + baseHeight;
            tile.CoarseningError = MeasureCoarseningError(heights, row + 1, row, Grid);
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
            caveEntranceMaterial.SetVector(MaskOrigin, origin);
        }

        internal int VisibleLevelAt(WorldPosition point)
        {
            foreach (TileKey key in visible)
                if (point.X >= key.X && point.X < key.X + key.Width && point.Z >= key.Z && point.Z < key.Z + key.Width)
                    return key.Level;
            return -1;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || REOL_TEST_BUILD
        internal (int Leaves, int WithError, int AboveThreshold, float MaximumPixels) MeasureProjectedErrors()
        {
            int measured = 0, above = 0;
            float maximum = 0f;
            foreach (TileKey key in visible)
            {
                Tile tile = tiles[key];
                if (!tile.AdaptiveLod || key.Level == 0)
                    continue;
                double y = eyeAtSelection.Y;
                double dy = Math.Max(0d, Math.Max(tile.MinimumHeight - y, y - tile.MaximumHeight));
                double distance = Math.Sqrt(HorizontalDistanceSquared(key, eyeAtSelection) + dy * dy);
                float pixels = ProjectError(tile.ChildError ?? tile.CoarseningError, distance, pixelScaleAtSelection);
                measured++;
                if (pixels > RefinePixels)
                    above++;
                maximum = Math.Max(maximum, pixels);
            }
            return (visible.Count, measured, above, maximum);
        }
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD || REOL_TEST_BUILD
        internal (int Edges, float OriginalError, float Error) MeasureDryEdges()
        {
            int count = 0;
            float original = 0f, error = 0f;
            foreach (TileKey key in visible)
            {
                Tile tile = tiles[key];
                if (!tile.HasIndices || tile.EdgeConstraints == Vector4.zero)
                    continue;
                tile.Mesh.GetVertices(stitchedVertices);
                for (int edge = 0; edge < 4; edge++)
                {
                    int constraint = (int)tile.EdgeConstraints[edge];
                    if (constraint == 0)
                        continue;
                    count++;
                    int spacing = key.Width / Grid;
                    for (int i = 1; i < Grid; i++)
                    {
                        Vector3 vertex = stitchedVertices[EdgeVertex(edge, i)];
                        int step = StitchStep(key, tile, vertex, edge, spacing, constraint);
                        if (step <= spacing)
                            continue;
                        float neighbor = EdgeHeight(key, vertex, step, edge);
                        error = Math.Max(error, Math.Abs(vertex.y + tile.Elevation - neighbor));
                        original = Math.Max(original, Math.Abs(EdgeHeight(key, vertex, spacing, edge) - neighbor));
                    }
                }
            }
            return (count, original, error);
        }

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
            Object.Destroy(caveEntranceMaterial);
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
            internal TileKey Parent => new(X & -(Width * 2), Z & -(Width * 2), Level + 1);
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

        private readonly struct BorderVertex
        {
            internal readonly int Index;
            internal readonly float OriginalY, Skirt;

            internal BorderVertex(int index, float originalY, float skirt)
            {
                Index = index;
                OriginalY = originalY;
                Skirt = skirt;
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
            internal bool AdaptiveLod;
            internal float MinimumHeight, MaximumHeight, CoarseningError;
            internal float? ChildError;
            internal Vector4 EdgeConstraints;
            internal readonly ushort[] WetBorderSpans = new ushort[4];
            internal List<BorderVertex>[] RegionalBorders;
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

            internal void ReadRegionalBorders(RegionalSurfaceTile surface, int width)
            {
                RegionalBorders ??= new[] { new List<BorderVertex>(), new List<BorderVertex>(),
                    new List<BorderVertex>(), new List<BorderVertex>() };
                for (int edge = 0; edge < 4; edge++)
                {
                    RegionalBorders[edge].Clear();
                    WetBorderSpans[edge] = surface.WetBorderSpans(edge);
                }
                IReadOnlyList<Vector3> points = surface.Vertices;
                IReadOnlyList<Vector2> skirts = surface.SkirtOffsets;
                for (int i = 0; i < points.Count; i++)
                {
                    Vector3 p = points[i];
                    if (p.x > 0 && p.x < width && (p.z == 0 || p.z == width))
                        RegionalBorders[p.z == 0 ? 0 : 2].Add(new BorderVertex(i, p.y, skirts[i].x));
                    else if (p.z > 0 && p.z < width && (p.x == 0 || p.x == width))
                        RegionalBorders[p.x == 0 ? 1 : 3].Add(new BorderVertex(i, p.y, skirts[i].x));
                }
                // A freshly built adaptive edge may have extra samples within its native grid intervals.
                EdgeConstraints = -Vector4.one;
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
