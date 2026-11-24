using System;
using System.Collections.Generic;
using Reol2.Voxels;
using UnityEngine;

namespace Reol2.World
{
    internal enum PageContent { Air, Solid, Mixed }

    internal sealed class ContinentField
    {
        internal const int Extent = 1024;
        internal const int Bedrock = -96;
        internal const int Ceiling = 176;
        internal const int CellCount = DensityVolume.ChunkCells;
        internal const int ChunkSize = (int)DensityVolume.ChunkSize;
        internal const int Columns = 2 * Extent / ChunkSize;
        internal const int FirstColumn = -Extent / ChunkSize;
        internal const float Spacing = DensityVolume.Spacing;
        internal const float BrushRadius = 0.65f;
        private const float SpurHalfWidth = 0.10f;
        internal const float GradeHalfWidth = 1f;
        internal const float GradeShoulder = 0.75f;
        internal const float GradeMaxCutFill = 1.25f;

        private readonly float[,] heights;
        private readonly float[] heightSamples = new float[DensityVolume.SampleWidth * DensityVolume.SampleWidth];
        private readonly float[] heightNodes = new float[16];
        private readonly Dictionary<Vector3Int, Dictionary<Vector3Int, SampleEdit>> editedPages = new();
        private readonly HashSet<Vector3Int> editedChunks = new();
        private readonly HashSet<Vector3Int> highEditRegions = new();
        private readonly EditSampleCache[] recentEditSamples = new EditSampleCache[16];
        private int nextEditSample;
        private readonly Bounds cave;
        private readonly Vector3Int landingCell;
        private readonly WorldSurface surface;
        private readonly ulong materialSeed;
        private readonly List<CavePart> caveParts = new(8);
        private readonly List<GradeColumn> gradeColumns = new();
        private readonly HashSet<Vector3Int> gradeChangedChunks = new();
        private readonly List<Vector3Int> openedSamples = new();
        private readonly Dictionary<Vector3Int, float> spurRepairs = new();

        internal WorldPosition Landing { get; }
        internal WorldPosition SeaEntry { get; }
        internal WorldCaves Caves { get; }
        internal int EditedSamples { get; private set; }
        internal long EvaluatedDensitySamples { get; private set; }
        internal bool HasEdits(Vector3Int page) => editedChunks.Contains(page);
        internal IReadOnlyCollection<Vector3Int> GradeChangedChunks => gradeChangedChunks;

        internal ContinentField(WorldDefinition world = null, RegionalLandforms landforms = null, IWorldSurfaceData data = null)
        {
            materialSeed = world?.SeedBits ?? 20261121UL;
            if (world != null)
            {
                surface = new WorldSurface(world, landforms, data);
                Caves = new WorldCaves(world, surface);
                if (landforms != null)
                {
                    WorldPosition point = surface.Place(landforms.Landing);
                    Landing = new WorldPosition(point.X, HeightAt(point.X, point.Z) + 0.18f, point.Z);
                    landingCell = Landing.Cell;
                    SeaEntry = surface.Place(landforms.SeaEntry, -0.8);
                    return;
                }
                if (data != null)
                {
                    WorldPosition spawn = data.Spawn;
                    Landing = new WorldPosition(spawn.X, HeightAt(spawn.X, spawn.Z) + 0.18f, spawn.Z);
                    landingCell = Landing.Cell;
                    SeaEntry = data.SeaEntry;
                    return;
                }

                double east = world.ContinentCenter.x + 0.5;
                double shore = FindShore(world.ContinentCenter, 8f);
                Landing = new WorldPosition(east, HeightAt(east, shore) + 0.18f, shore);
                landingCell = Landing.Cell;
                double offshore = FindShore(world.ContinentCenter, -55f);
                SeaEntry = new WorldPosition(east, -0.8, offshore);
                return;
            }

            heights = new float[Columns + 1, Columns + 1];
            for (int z = 0; z <= Columns; z++)
            for (int x = 0; x <= Columns; x++)
                heights[x, z] = GenerateHeight(x * ChunkSize - Extent, z * ChunkSize - Extent);

            const float shoreX = -36f;
            int shoreZ = -Extent + ChunkSize;
            while (shoreZ < 0 && HeightAt(shoreX, shoreZ) < 4f)
                shoreZ += 2;
            Landing = new WorldPosition(shoreX, HeightAt(shoreX, shoreZ) + 0.15f, shoreZ);
            SeaEntry = new WorldPosition(-0.5, -0.8, -944.5);
            landingCell = Landing.Cell;
            var caveCenter = new Vector3(shoreX + 9f, (float)Landing.Y - 12f, shoreZ + 18f);
            cave = new Bounds(caveCenter, new Vector3(11f, 5f, 15f));
        }

        private double FindShore(Vector2Int center, float level)
        {
            int low = center.y - 542000;
            int high = center.y - 210000;
            while (high - low > 1)
            {
                int mid = low + (high - low) / 2;
                if (HeightAt(center.x + 0.5, mid + 0.5) < level)
                    low = mid;
                else
                    high = mid;
            }
            return high + 0.5;
        }

        internal float HeightAt(double x, double z) => surface != null ? surface.HeightAt(x, z) : HeightAt((float)x, (float)z);
        internal float HeightNode(int x, int z) => surface.Node(x, z);
        internal WorldSurface Surface => surface;

        internal float HeightAt(float x, float z)
        {
            if (surface != null)
                return surface.HeightAt(x, z);
            float gx = Mathf.Clamp((x + Extent) / ChunkSize, 0f, Columns);
            float gz = Mathf.Clamp((z + Extent) / ChunkSize, 0f, Columns);
            int i = Mathf.Min((int)gx, Columns - 1);
            int j = Mathf.Min((int)gz, Columns - 1);
            float u = gx - i;
            float v = gz - j;
            // Use the same diagonal as the coarse terrain quads.
            if (u + v <= 1f)
            {
                float a = heights[i, j];
                return a + (heights[i + 1, j] - a) * u + (heights[i, j + 1] - a) * v;
            }
            float d = heights[i + 1, j + 1];
            return d + (heights[i, j + 1] - d) * (1f - u) + (heights[i + 1, j] - d) * (1f - v);
        }

        internal Vector2Int SurfaceBand(Vector2Int column)
        {
            var bounds = HeightRange(column.x, column.y, halo: false);
            return new Vector2Int(Mathf.FloorToInt((bounds.x - 0.002f) / ChunkSize),
                                  Mathf.FloorToInt((bounds.y + 0.002f) / ChunkSize));
        }

        internal bool InOcean(WorldPosition point) => point.Y < 0d && point.Y > HeightAt(point.X, point.Z)
            && (surface == null || surface.NearConnectedSea(point.X, point.Z));
        internal bool InWater(WorldPosition point) => surface == null ? InOcean(point)
            : point.Y < surface.WaterAt(point.X, point.Z) && point.Y > surface.HeightAt(point.X, point.Z);

        internal float WaterSurface(WorldPosition point) => surface == null ? 0f : surface.WaterAt(point.X, point.Z);

        internal bool IsClearSky(WorldPosition first, WorldPosition last, int padding)
        {
            if (Math.Min(first.Y, last.Y) <= WorldSurface.HighestTerrain + 64f)
                return false;
            if (highEditRegions.Count == 0)
                return true;

            Vector3Int growth = Vector3Int.one * padding;
            Vector3Int minimum = SkyRegion(Vector3Int.Min(first.Cell, last.Cell) - growth);
            Vector3Int maximum = SkyRegion(Vector3Int.Max(first.Cell, last.Cell) + growth + Vector3Int.one);
            Vector3Int size = maximum - minimum + Vector3Int.one;
            if ((long)size.x * size.y * size.z > 64)
                return false;
            foreach (Vector3Int candidate in new BoundsInt(minimum, size).allPositionsWithin)
                if (highEditRegions.Contains(candidate))
                    return false;
            return true;
        }

        private static Vector3Int SkyRegion(Vector3Int cell) => new(cell.x >> 7, cell.y >> 7, cell.z >> 7);

        internal PageContent CopySurfaceSamples(Vector3Int chunk, float[] target)
        {
            int width = DensityVolume.SampleWidth;
            int slice = width * width;
            Vector3Int origin = chunk * CellCount - Vector3Int.one;
            if (surface != null)
                return CopyWorldSamples(chunk, origin, target);
            float low = origin.y * Spacing;
            float high = (origin.y + width - 1) * Spacing;
            bool hasEdits = editedChunks.Contains(chunk);
            var halo = new Bounds((Vector3)origin * Spacing + Vector3.one * ((width - 1) * Spacing * 0.5f),
                                  Vector3.one * ((width - 1) * Spacing));
            bool hasCave = cave.Intersects(halo);
            Vector2 heightsAtChunk = HeightRange(chunk.x, chunk.z, halo: true);
            if (!hasEdits && !hasCave)
            {
                if (low > heightsAtChunk.y)
                    return PageContent.Air;
                if (high < heightsAtChunk.x)
                    return PageContent.Solid;
                return CopyHeightSurface(origin, target);
            }

            bool air = false;
            bool solid = false;
            for (int z = 0; z < width; z++)
            for (int x = 0; x < width; x++)
            {
                float wx = (origin.x + x) * Spacing;
                float wz = (origin.z + z) * Spacing;
                float height = HeightAt(wx, wz);
                int index = x + z * slice;
                for (int y = 0; y < width; y++, index += width)
                {
                    var sample = new Vector3Int(origin.x + x, origin.y + y, origin.z + z);
                    if (hasEdits && TryReadEdit(sample, out SampleEdit changed))
                        target[index] = changed.Density;
                    else
                        target[index] = BaseDensity(new Vector3(wx, sample.y * Spacing, wz), height, hasCave);
                    solid |= target[index] < 0f;
                    air |= target[index] >= 0f;
                }
            }
            return Classify(air, solid);
        }

        private PageContent CopyHeightSurface(Vector3Int origin, float[] target)
        {
            const int width = DensityVolume.SampleWidth;
            float lowest = origin.y * Spacing;
            float highest = (origin.y + width - 1) * Spacing;
            bool air = false;
            bool solid = false;
            for (int z = 0; z < width; z++)
            for (int x = 0; x < width; x++)
            {
                float height = HeightAt((origin.x + x) * Spacing, (origin.z + z) * Spacing);
                heightSamples[z * width + x] = height;
                air |= highest >= height;
                solid |= lowest < height;
            }
            if (!air || !solid)
                return Classify(air, solid);

            // These columns have no caves or edits. Evaluate y - height into contiguous sample rows.
            int write = 0;
            for (int z = 0; z < width; z++)
            for (int y = 0; y < width; y++)
            {
                float elevation = (origin.y + y) * Spacing;
                int heightRow = z * width;
                for (int x = 0; x < width; x++)
                    target[write++] = NonzeroDensity(elevation - heightSamples[heightRow + x]);
            }
            return PageContent.Mixed;
        }

        private PageContent CopyWorldSamples(Vector3Int chunk, Vector3Int origin, float[] target, bool naturalSnapshot = false)
        {
            const int width = DensityVolume.SampleWidth;
            Vector3Int last = origin + Vector3Int.one * (width - 1);
            float low = Math.Max(origin.y, 2 * WorldDefinition.MinY) * Spacing;
            float high = Math.Min(last.y, 2 * WorldDefinition.MaxY) * Spacing;
            bool hasEdits = !naturalSnapshot && editedChunks.Contains(chunk);
            Caves.Collect(origin, last, caveParts);
            Vector3Int minimumSample = WorldDefinition.ClampSample(origin);
            Vector3Int maximumSample = WorldDefinition.ClampSample(last);
            bool canCarveAir = false;
            for (int i = 0; i < caveParts.Count; i++)
            {
                Vector2 distance = caveParts[i].DistanceRange(minimumSample, maximumSample);
                if (!naturalSnapshot && !hasEdits && distance.y < -0.001f)
                    return PageContent.Air;
                canCarveAir |= distance.x < 0.001f;
            }

            bool heightOnly = !hasEdits && caveParts.Count == 0;
            if (!naturalSnapshot && heightOnly && low > WorldSurface.HighestTerrain)
                return PageContent.Air;
            if (!naturalSnapshot && heightOnly && high < WorldSurface.DeepestSeabed)
                return PageContent.Solid;

            int wx = chunk.x * ChunkSize;
            int wz = chunk.z * ChunkSize;
            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;
            for (int z = 0; z < 4; z++)
            for (int x = 0; x < 4; x++)
            {
                float node = surface.Node(wx + (x - 1) * ChunkSize, wz + (z - 1) * ChunkSize);
                heightNodes[x + z * 4] = node;
                minimum = Math.Min(minimum, node);
                maximum = Math.Max(maximum, node);
            }
            if (!naturalSnapshot && !hasEdits && low > maximum)
                return PageContent.Air;
            if (!naturalSnapshot && !hasEdits && !canCarveAir && high < minimum)
                return PageContent.Solid;

            float actualMin = float.PositiveInfinity;
            float actualMax = float.NegativeInfinity;
            for (int z = 0; z < width; z++)
            for (int x = 0; x < width; x++)
            {
                int sx = Math.Clamp(origin.x + x, -2 * WorldDefinition.Extent, 2 * WorldDefinition.Extent);
                int sz = Math.Clamp(origin.z + z, -2 * WorldDefinition.Extent, 2 * WorldDefinition.Extent);
                int ix = sx - (chunk.x - 1) * CellCount;
                int iz = sz - (chunk.z - 1) * CellCount;
                // At the positive world face evaluate the inside triangle at its endpoint.
                int nx = Math.Min(ix / CellCount, sx == 2 * WorldDefinition.Extent ? 1 : 2);
                int nz = Math.Min(iz / CellCount, sz == 2 * WorldDefinition.Extent ? 1 : 2);
                float u = (ix - nx * CellCount) * (1f / CellCount);
                float v = (iz - nz * CellCount) * (1f / CellCount);
                int a = nx + nz * 4;
                float h = WorldSurface.Interpolate(heightNodes[a], heightNodes[a + 1], heightNodes[a + 4], heightNodes[a + 5], u, v);
                heightSamples[x + z * width] = h;
                actualMin = Math.Min(actualMin, h);
                actualMax = Math.Max(actualMax, h);
            }
            if (!naturalSnapshot && heightOnly && low >= actualMax)
                return PageContent.Air;
            if (!naturalSnapshot && heightOnly && high < actualMin)
                return PageContent.Solid;

            FillWorldDensities(origin, 1, target);
            if (naturalSnapshot)
                return ClassifyBuffer(target);
            if (heightOnly)
                return PageContent.Mixed;
            if (hasEdits)
                OverlayEdits(origin, last, target);
            return ClassifyBuffer(target);
        }

        internal PageContent CopyEditedSurfaceSamples(Vector3Int key, float[] target)
        {
            if (surface == null)
                return CopySurfaceSamples(key, target);

            Vector3Int first = key * CellCount - Vector3Int.one;
            Vector3Int last = first + Vector3Int.one * (DensityVolume.SampleWidth - 1);
            float[] baseSamples = null;
            for (int i = 0; i < recentEditSamples.Length; i++)
            {
                EditSampleCache entry = recentEditSamples[i];
                if (entry.Natural != null && entry.Page == key)
                {
                    baseSamples = entry.Natural;
                    break;
                }
            }
            if (baseSamples == null)
            {
                int slot = nextEditSample++ % recentEditSamples.Length;
                baseSamples = recentEditSamples[slot].Natural ?? new float[target.Length];
                CopyWorldSamples(key, first, baseSamples, naturalSnapshot: true);
                recentEditSamples[slot] = new EditSampleCache(key, baseSamples);
            }
            Array.Copy(baseSamples, target, target.Length);
            OverlayEdits(first, last, target);
            return ClassifyBuffer(target);
        }

        internal PageContent CopyCaveProxy(Vector3Int key, float[] target)
        {
            const int factor = WorldCaveHorizon.SampleScale;
            const int width = DensityVolume.SampleWidth;
            Vector3Int first = key * (CellCount * factor) - Vector3Int.one * factor;
            Vector3Int last = first + Vector3Int.one * ((width - 1) * factor);
            Caves.Collect(first, last, caveParts);
            bool hasWall = false;
            foreach (CavePart cavePart in caveParts)
            {
                Vector2 distance = cavePart.DistanceRange(first, last);
                if (distance.y < -0.001f)
                    return PageContent.Air;
                hasWall |= distance.x < 0.001f;
            }
            if (!hasWall)
                return PageContent.Solid;

            for (int z = 0; z < width; z++)
            for (int x = 0; x < width; x++)
                heightSamples[x + z * width] = surface.Node((first.x + x * factor) / 2, (first.z + z * factor) / 2);

            FillWorldDensities(first, factor, target);
            return ClassifyBuffer(target);
        }

        internal float CaveProxyHeight(Vector3 local)
        {
            const int row = DensityVolume.SampleWidth;
            float x = local.x / Spacing;
            float z = local.z / Spacing;
            int ix = Math.Min((int)x, CellCount - 1);
            int iz = Math.Min((int)z, CellCount - 1);
            int corner = ix + 1 + (iz + 1) * row;
            return WorldSurface.Interpolate(heightSamples[corner], heightSamples[corner + 1],
                heightSamples[corner + row], heightSamples[corner + row + 1], x - ix, z - iz);
        }

        internal bool CaveProxyIncludesGround(Vector3Int page, PageContent content)
        {
            const int pageWidth = WorldCaveHorizon.PageWidth;
            int baseX = page.x * pageWidth, baseZ = page.z * pageWidth;
            int bottom = page.y * pageWidth;
            float low = WorldSurface.HighestTerrain, high = WorldSurface.DeepestSeabed;
            for (int z = 0; z <= CellCount; z++)
            for (int x = 0; x <= CellCount; x++)
            {
                float y = content == PageContent.Mixed
                    ? heightSamples[x + 1 + (z + 1) * DensityVolume.SampleWidth]
                    : surface.Node(baseX + x * ChunkSize, baseZ + z * ChunkSize);
                // Retain a coarse sample of margin at the upper and lower page faces.
                if (y >= bottom - ChunkSize && y <= bottom + pageWidth + ChunkSize)
                    return true;
                low = Math.Min(low, y);
                high = Math.Max(high, y);
            }
            return low < bottom && high > bottom + pageWidth;
        }

        private void FillWorldDensities(Vector3Int origin, int stride, float[] target)
        {
            const int width = DensityVolume.SampleWidth;
            int index = 0;
            for (int z = 0; z < width; z++)
            for (int y = 0; y < width; y++)
            {
                int sampleY = Math.Clamp(origin.y + y * stride, 2 * WorldDefinition.MinY, 2 * WorldDefinition.MaxY);
                float elevation = sampleY * Spacing;
                int row = z * width;
                for (int x = 0; x < width; x++)
                    target[index++] = NonzeroDensity(elevation - heightSamples[row + x]);
            }

            if (caveParts.Count != 0)
            {
                Vector3Int first = WorldDefinition.ClampSample(origin);
                Vector3Int last = WorldDefinition.ClampSample(origin + Vector3Int.one * ((width - 1) * stride));
                Span<float> nearest = stackalloc float[caveParts.Count];
                float airThroughout = 0f;
                for (int i = 0; i < caveParts.Count; i++)
                {
                    Vector2 distance = caveParts[i].DistanceRange(first, last);
                    nearest[i] = distance.x;
                    airThroughout = Math.Max(airThroughout, -distance.y);
                }

                // A cave enclosing the whole page contributes at least airThroughout everywhere.
                // A second part cannot raise a sample beyond the greater of its original height density and -distance.
                const float distanceSlack = 0.02f;
                float influenceLimit = (airThroughout > distanceSlack ? -airThroughout : CavePart.BlendDistance) + distanceSlack;
                for (int i = 0; i < caveParts.Count; i++)
                    if (nearest[i] <= influenceLimit)
                        caveParts[i].Apply(origin, stride, width, heightSamples, target);
            }
            EvaluatedDensitySamples += target.Length;
        }

        private static PageContent ClassifyBuffer(float[] target)
        {
            bool solid = target[0] < 0f;
            for (int i = 1; i < target.Length; i++)
                if ((target[i] < 0f) != solid)
                    return PageContent.Mixed;
            return solid ? PageContent.Solid : PageContent.Air;
        }

        private void OverlayEdits(Vector3Int first, Vector3Int last, float[] target)
        {
            const int width = DensityVolume.SampleWidth;
            Vector3Int min = FloorChunk(WorldDefinition.ClampSample(first));
            Vector3Int max = FloorChunk(WorldDefinition.ClampSample(last));
            foreach (Vector3Int owner in new BoundsInt(min, max - min + Vector3Int.one).allPositionsWithin)
            {
                if (!editedPages.TryGetValue(owner, out var page))
                    continue;
                foreach (var entry in page)
                {
                    Vector3Int sample = entry.Key;
                    Vector3Int lo = new(
                        sample.x == -2 * WorldDefinition.Extent ? first.x : Math.Max(first.x, sample.x),
                        sample.y == 2 * WorldDefinition.MinY ? first.y : Math.Max(first.y, sample.y),
                        sample.z == -2 * WorldDefinition.Extent ? first.z : Math.Max(first.z, sample.z));
                    Vector3Int hi = new(
                        sample.x == 2 * WorldDefinition.Extent ? last.x : Math.Min(last.x, sample.x),
                        sample.y == 2 * WorldDefinition.MaxY ? last.y : Math.Min(last.y, sample.y),
                        sample.z == 2 * WorldDefinition.Extent ? last.z : Math.Min(last.z, sample.z));
                    for (int z = lo.z; z <= hi.z; z++)
                    for (int y = lo.y; y <= hi.y; y++)
                    for (int x = lo.x; x <= hi.x; x++)
                        target[x - first.x + width * (y - first.y + width * (z - first.z))] = NonzeroDensity(entry.Value.Density);
                }
            }
        }

        private static PageContent Classify(bool air, bool solid) => air && solid ? PageContent.Mixed
            : air ? PageContent.Air : PageContent.Solid;

        private float Carve(Vector3Int sample, float density)
        {
            float carved = density;
            for (int i = 0; i < caveParts.Count; i++)
                carved = Math.Max(carved, caveParts[i].Carve(sample, density));
            return carved;
        }

        internal bool Edit(Vector3Int cell, bool fill, out BoundsInt changed)
        {
            changed = default;
            if (!Editable(cell))
                return false;

            const int core = DensityVolume.SamplesPerMeter / 2;
            const int rim = core + 1;
            Vector3Int center = cell * DensityVolume.SamplesPerMeter + Vector3Int.one * core;
            Vector3Int low = center + Vector3Int.one * (rim + 1);
            Vector3Int high = center - Vector3Int.one * (rim + 1);
            // Derive the same shape on every edit and after an origin shift.
            ulong shape = WorldNoise.Hash(cell.x, cell.z, materialSeed ^ unchecked((uint)cell.y));
            float shiftX = (WorldNoise.Unit(shape) - 0.5f) * 0.04f;
            float shiftZ = (WorldNoise.Unit(shape << 32) - 0.5f) * 0.04f;
            float radius = Mathf.Lerp(0.16f, 0.22f, (shape & 255) / 255f);
            for (int z = -rim; z <= rim; z++)
            for (int y = -rim; y <= rim; y++)
            for (int x = -rim; x <= rim; x++)
            {
                // The outer endpoint of a crossing edge is one lattice step outside the cell.
                int exteriorAxes = (Math.Abs(x) > core ? 1 : 0) + (Math.Abs(y) > core ? 1 : 0)
                    + (Math.Abs(z) > core ? 1 : 0);
                if (exteriorAxes > 1)
                    continue;
                Vector3Int sample = center + new Vector3Int(x, y, z);
                if (exteriorAxes != 0 && !SculptRangeAllowed(sample, sample))
                    continue;

                float stamp = GridCellDensity(x * Spacing - shiftX, y * Spacing, z * Spacing - shiftZ, radius);
                SampleEdit current = Read(sample);
                float next = fill ? Mathf.Min(current.Density, stamp) : Mathf.Max(current.Density, -stamp);
                StoreChange(sample, current, next, cell, ref low, ref high);
            }
            return CompleteBrush(low, high, out changed);
        }

        private static float GridCellDensity(float x, float y, float z, float radius)
        {
            const float halfWidth = 0.625f;
            const float halfHeight = 0.555f;
            var corner = new Vector2(Mathf.Abs(x), Mathf.Abs(z)) - Vector2.one * (halfWidth - radius);
            float side = Mathf.Max(corner.x, corner.y);
            float rounded = side <= 0f ? side : Vector2.Max(corner, Vector2.zero).magnitude;
            // At a top or bottom sample within the cell the cap dominates even at a rounded
            // corner. Neighbouring cuts at the same elevation then share a plane across the join.
            return Mathf.Max(rounded - radius, Mathf.Abs(y) - halfHeight);
        }

        internal bool EditSphere(WorldPosition center, bool fill, out BoundsInt changed)
        {
            changed = default;
            if (!WorldDefinition.Contains(center) || (surface == null && !Editable(center.Cell)))
                return false;

            openedSamples.Clear();
            // The band supplies the outer endpoint of every lattice edge that the sphere crosses.
            const float reach = BrushRadius + Spacing;
            Vector3Int start = new((int)Math.Ceiling((center.X - reach) / Spacing),
                                   (int)Math.Ceiling((center.Y - reach) / Spacing),
                                   (int)Math.Ceiling((center.Z - reach) / Spacing));
            Vector3Int end = new((int)Math.Floor((center.X + reach) / Spacing),
                                 (int)Math.Floor((center.Y + reach) / Spacing),
                                 (int)Math.Floor((center.Z + reach) / Spacing));
            if (surface != null)
            {
                start = WorldDefinition.ClampSample(start);
                end = WorldDefinition.ClampSample(end);
            }
            else if (!SculptRangeAllowed(start, end))
                return false;

            Vector3Int low = end + Vector3Int.one, high = start - Vector3Int.one;
            for (int z = start.z; z <= end.z; z++)
            for (int y = start.y; y <= end.y; y++)
            for (int x = start.x; x <= end.x; x++)
            {
                // Subtract in world double precision before converting to brush-local floats.
                var offset = new Vector3((float)(x * (double)Spacing - center.X),
                                         (float)(y * (double)Spacing - center.Y),
                                         (float)(z * (double)Spacing - center.Z));
                if (offset.sqrMagnitude > reach * reach)
                    continue;
                Vector3Int sample = new(x, y, z);
                SampleEdit current = Read(sample);
                float stamp = offset.magnitude - BrushRadius;
                float next = fill ? Mathf.Min(current.Density, stamp) : Mathf.Max(current.Density, -stamp);
                if (surface != null && current.Density < 0f && next >= 0f)
                    openedSamples.Add(sample);
                StoreChange(sample, current, next, null, ref low, ref high);
            }
            if (openedSamples.Count > 0)
                RemoveCutSpurs(ref low, ref high);
            return CompleteBrush(low, high, out changed);
        }

        private void RemoveCutSpurs(ref Vector3Int low, ref Vector3Int high)
        {
            spurRepairs.Clear();
            foreach (Vector3Int opened in openedSamples)
            for (int axis = 0; axis < 3; axis++)
            {
                var step = Vector3Int.zero;
                step[axis] = 1;
                FindCutSpur(opened - step);
                FindCutSpur(opened + step);
            }

            // Inspect the same post-cut field for every candidate before removing any tips.
            foreach (var repair in spurRepairs)
                StoreChange(repair.Key, Read(repair.Key), repair.Value, null, ref low, ref high);
        }

        private void FindCutSpur(Vector3Int sample)
        {
            if (sample != WorldDefinition.ClampSample(sample) || spurRepairs.ContainsKey(sample))
                return;
            float density = SampleAt(sample);
            if (density >= 0f)
                return;

            int narrowAxes = 0;
            float air = float.PositiveInfinity;
            // Both zero crossings must lie close to the sample on two axes. A sheet, ridge,
            // or thin column with solid neighbours at both ends of an axis keeps its support.
            float minNeighbour = -density * (Spacing / SpurHalfWidth - 1f);
            for (int axis = 0; axis < 3; axis++)
            {
                var step = Vector3Int.zero;
                step[axis] = 1;
                float a = SampleAt(sample - step), b = SampleAt(sample + step);
                if (a < 0f && b < 0f)
                    return;
                float near = Mathf.Min(a, b);
                if (near < minNeighbour)
                    continue;
                narrowAxes++;
                air = Mathf.Min(air, near);
            }
            if (narrowAxes >= 2)
                spurRepairs.Add(sample, air);
        }

        internal bool EditGrade(WorldPosition start, WorldPosition end, out BoundsInt changed)
        {
            changed = default;
            gradeColumns.Clear();
            gradeChangedChunks.Clear();
            if (!WorldDefinition.Contains(start) || !WorldDefinition.Contains(end))
                return false;
            double dx = end.X - start.X, dz = end.Z - start.Z;
            double length = Math.Sqrt(dx * dx + dz * dz);
            if (length < 2d || length > 32d || Math.Abs(end.Y - start.Y) > 0.5d * length)
                return false;

            double ux = dx / length, uz = dz / length;
            const double width = GradeHalfWidth + GradeShoulder;
            int x0 = (int)Math.Ceiling((Math.Min(start.X, end.X) - width) / Spacing);
            int x1 = (int)Math.Floor((Math.Max(start.X, end.X) + width) / Spacing);
            int z0 = (int)Math.Ceiling((Math.Min(start.Z, end.Z) - width) / Spacing);
            int z1 = (int)Math.Floor((Math.Max(start.Z, end.Z) + width) / Spacing);
            // Select the complete layer first. Reject ambiguous columns before touching any page.
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                double px = x * (double)Spacing - start.X, pz = z * (double)Spacing - start.Z;
                double along = Math.Clamp(px * ux + pz * uz, 0d, length);
                double acrossX = px - along * ux, acrossZ = pz - along * uz;
                double distance = Math.Sqrt(acrossX * acrossX + acrossZ * acrossZ);
                if (distance >= width)
                    continue;
                double target = start.Y + (end.Y - start.Y) * (along / length);
                float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((float)((width - distance) / GradeShoulder)));
                if (!SelectGradeColumn(x, z, target, blend))
                    return false;
            }

            Vector3Int low = new(int.MaxValue, int.MaxValue, int.MaxValue);
            Vector3Int high = new(int.MinValue, int.MinValue, int.MinValue);
            foreach (GradeColumn column in gradeColumns)
            {
                Vector3Int columnLow = new(column.X + 1, column.Top + 1, column.Z + 1);
                Vector3Int columnHigh = new(column.X - 1, column.Bottom - 1, column.Z - 1);
                for (int y = column.Bottom; y <= column.Top; y++)
                {
                    Vector3Int sample = new(column.X, y, column.Z);
                    SampleEdit current = Read(sample);
                    float plane = (float)(y * (double)Spacing - column.Height);
                    // Both ends of a crossing edge need the same plane, including on old non-planar edits.
                    StoreChange(sample, current, plane, null, ref columnLow, ref columnHigh);
                }
                if (columnLow.x > columnHigh.x)
                    continue;
                low = Vector3Int.Min(low, columnLow);
                high = Vector3Int.Max(high, columnHigh);
                var columnBounds = new BoundsInt(columnLow, columnHigh - columnLow + Vector3Int.one);
                foreach (Vector3Int page in AffectedChunks(columnBounds).allPositionsWithin)
                    gradeChangedChunks.Add(page);
            }
            if (gradeChangedChunks.Count == 0)
                return false;
            changed = new BoundsInt(low, high - low + Vector3Int.one);
            editedChunks.UnionWith(gradeChangedChunks);
            return true;
        }

        private bool SelectGradeColumn(int x, int z, double designHeight, float blend)
        {
            const double checkDepth = GradeMaxCutFill + Spacing;
            int bottom = (int)Math.Floor((designHeight - checkDepth) / Spacing);
            int top = (int)Math.Ceiling((designHeight + checkDepth) / Spacing);
            if (!SculptRangeAllowed(new Vector3Int(x, bottom, z), new Vector3Int(x, top, z)))
                return false;
            float previous = SampleAt(new Vector3Int(x, bottom, z));
            if (previous >= 0f)
                return false;
            double currentHeight = double.NaN;
            for (int y = bottom + 1; y <= top; y++)
            {
                float density = SampleAt(new Vector3Int(x, y, z));
                if (previous >= 0f && density < 0f)
                    return false;
                if (previous < 0f && density >= 0f)
                    currentHeight = (y - 1d + previous / (previous - density)) * Spacing;
                previous = density;
            }
            double delta = designHeight - currentHeight;
            if (double.IsNaN(currentHeight) || Math.Abs(delta) > GradeMaxCutFill)
                return false;
            // An envelope makes shoulders join the terrain and keeps a repeated stroke idempotent.
            double shoulderAllowance = GradeMaxCutFill * (1d - blend);
            double target = Math.Clamp(currentHeight, designHeight - shoulderAllowance, designHeight + shoulderAllowance);
            if (Math.Abs(target - currentHeight) < 0.0001d)
                return true;
            // Touch just the old and new surfaces and one interpolation edge on either side.
            int from = (int)Math.Floor(Math.Min(currentHeight, target) / Spacing) - 1;
            int to = (int)Math.Ceiling(Math.Max(currentHeight, target) / Spacing) + 1;
            if (!SculptRangeAllowed(new Vector3Int(x, from - 1, z), new Vector3Int(x, to + 1, z)))
                return false;
            if (TouchesOtherSurface(x, from, z, target, solid: true) ||
                TouchesOtherSurface(x, to, z, target, solid: false))
                return false;
            gradeColumns.Add(new GradeColumn(x, z, from, to, target));
            return true;
        }

        private bool TouchesOtherSurface(int x, int y, int z, double target, bool solid)
        {
            float original = SampleAt(new Vector3Int(x, y, z));
            if (original == (float)(y * (double)Spacing - target))
                return false;
            // A signed sample positions zero crossings on all six incident lattice edges. At the
            // outer end of this write, keep a cave ceiling, upper floor or wall edge stationary.
            int outsideY = y + (solid ? -1 : 1);
            return (SampleAt(new Vector3Int(x, outsideY, z)) < 0f) != solid ||
                   (SampleAt(new Vector3Int(x - 1, y, z)) < 0f) != solid ||
                   (SampleAt(new Vector3Int(x + 1, y, z)) < 0f) != solid ||
                   (SampleAt(new Vector3Int(x, y, z - 1)) < 0f) != solid ||
                   (SampleAt(new Vector3Int(x, y, z + 1)) < 0f) != solid;
        }

        private bool SculptRangeAllowed(Vector3Int start, Vector3Int end)
        {
            if (surface != null)
                return start == WorldDefinition.ClampSample(start) && end == WorldDefinition.ClampSample(end);
            const int steps = DensityVolume.SamplesPerMeter;
            if (start.x < (-Extent + ChunkSize) * steps || end.x > (Extent - ChunkSize) * steps ||
                start.z < (-Extent + ChunkSize) * steps || end.z > (Extent - ChunkSize) * steps ||
                start.y < (Bedrock + 1) * steps || end.y > (Ceiling - 2) * steps)
                return false;
            Vector3Int safeMin = (landingCell - new Vector3Int(1, 2, 1)) * steps;
            Vector3Int safeMax = (landingCell + new Vector3Int(2, 3, 2)) * steps;
            return end.x < safeMin.x || start.x > safeMax.x || end.y < safeMin.y || start.y > safeMax.y ||
                   end.z < safeMin.z || start.z > safeMax.z;
        }

        private void StoreChange(Vector3Int sample, SampleEdit current, float next, Vector3Int? placementCell,
                                 ref Vector3Int low, ref Vector3Int high)
        {
            if (next == current.Density)
                return;
            bool added = next < 0f && current.Density >= 0f;
            Vector3Int? source = next >= 0f ? null : added ? placementCell : current.Source;
            bool sculptedFill = next < 0f && (added ? !placementCell.HasValue : current.SculptedFill);
            WriteEdit(sample, new SampleEdit(next, source, sculptedFill));
            if (sample.y * Spacing >= WorldSurface.HighestTerrain)
                highEditRegions.Add(new Vector3Int(sample.x >> 8, sample.y >> 8, sample.z >> 8));
            low = Vector3Int.Min(low, sample);
            high = Vector3Int.Max(high, sample);
        }

        private bool CompleteBrush(Vector3Int low, Vector3Int high, out BoundsInt changed)
        {
            changed = default;
            if (low.x > high.x)
                return false;

            changed = new BoundsInt(low, high - low + Vector3Int.one);
            foreach (Vector3Int chunk in AffectedChunks(changed).allPositionsWithin)
                editedChunks.Add(chunk);
            return true;
        }

        internal bool Editable(Vector3Int cell)
        {
            if (surface != null)
                return cell.x >= -WorldDefinition.Extent && cell.x < WorldDefinition.Extent
                    && cell.z >= -WorldDefinition.Extent && cell.z < WorldDefinition.Extent
                    && cell.y >= WorldDefinition.MinY && cell.y < WorldDefinition.MaxY;
            Vector3Int offset = cell - landingCell;
            bool nearLanding = Mathf.Abs(offset.x) <= 1 && Mathf.Abs(offset.z) <= 1 && Mathf.Abs(offset.y) <= 2;
            return !nearLanding && cell.y > Bedrock && cell.y < Ceiling - 2
                && cell.x >= -Extent + ChunkSize && cell.x < Extent - ChunkSize
                && cell.z >= -Extent + ChunkSize && cell.z < Extent - ChunkSize;
        }

        internal Vector3Int DigCellAt(WorldPosition point, out bool placed)
        {
            var samplePoint = new WorldPosition(point.X * 2, point.Y * 2, point.Z * 2);
            Vector3Int corner = samplePoint.Cell;
            placed = false;
            // This page's edit invalidation includes the halo where the eight pick corners lie.
            if (!HasEdits(FloorChunk(corner)))
                return point.Cell;
            var fraction = new Vector3((float)(samplePoint.X - corner.x), (float)(samplePoint.Y - corner.y),
                (float)(samplePoint.Z - corner.z));
            float closest = float.PositiveInfinity;
            Vector3Int? source = null;
            for (int z = 0; z <= 1; z++)
            for (int y = 0; y <= 1; y++)
            for (int x = 0; x <= 1; x++)
            {
                var offset = new Vector3Int(x, y, z);
                Vector3Int sample = corner + offset;
                float distance = ((Vector3)offset - fraction).sqrMagnitude;
                if (distance >= closest)
                    continue;
                SampleEdit value = Read(sample);
                if (value.Density >= 0f)
                    continue;
                closest = distance;
                source = value.Source;
                placed = source.HasValue || value.SculptedFill;
            }
            return source ?? point.Cell;
        }

        internal bool IsSolidAt(WorldPosition point)
        {
            var closest = new Vector3Int((int)Math.Round(point.X * 2), (int)Math.Round(point.Y * 2),
                (int)Math.Round(point.Z * 2));
            return SampleAt(closest) < 0f;
        }

        internal float SampleAt(Vector3Int sample) => NonzeroDensity(Read(sample).Density);

        internal static Vector3Int ChunkAt(Vector3 point) => Vector3Int.FloorToInt(point / ChunkSize);

        internal static BoundsInt AffectedChunks(BoundsInt changed)
        {
            // Chunk c reads samples [c * 16 - 1, c * 16 + 17], including the normal halo.
            Vector3Int min = FloorChunk(changed.min - Vector3Int.one * 2);
            Vector3Int max = FloorChunk(changed.max);
            return new BoundsInt(min, max - min + Vector3Int.one);
        }

        internal Color32 MaterialAt(WorldPosition point, Vector3 normal, float ground, float spacing)
        {
            Color32 color = WorldMaterial.Sample(point, normal, ground, spacing, materialSeed, surface != null);
            return TintForRegion(point, normal, ground, color, spacing);
        }

        internal Color32 TintForRegion(WorldPosition point, Vector3 normal, float ground, Color32 color, float spacing)
        {
            if (point.Y > 2d && point.Y > ground - 1d
                && surface?.GeneratedClimateAt(point) is RegionalClimate climate)
            {
                color = WorldMaterial.TintForClimate(color, climate, (float)point.Y, normal);
                if (spacing < 32f)
                {
                    float river = surface.RiverWaterAt(point.X, point.Z, ground);
                    float detail = 1f - WorldNoise.Ramp((spacing - 8f) / 24f);
                    if (river > 0f && ground - river < 3.5f)
                        color = WorldMaterial.TintRiverSediment(color, point, ground - river, detail, materialSeed);
                }
            }
            return color;
        }

        internal WorldMaterial.PageTint PrepareTint(int x, int z) => new(x, z, materialSeed, surface != null);

        private Vector2 HeightRange(int x, int z, bool halo)
        {
            int ix = x - FirstColumn;
            int iz = z - FirstColumn;
            int side = halo ? 1 : 0;
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            for (int j = iz - side; j <= iz + 1 + side; j++)
            for (int i = ix - side; i <= ix + 1 + side; i++)
            {
                float h = heights[Mathf.Clamp(i, 0, Columns), Mathf.Clamp(j, 0, Columns)];
                min = Mathf.Min(min, h);
                max = Mathf.Max(max, h);
            }
            return new Vector2(min, max);
        }

        private SampleEdit Read(Vector3Int sample)
        {
            if (surface != null)
            {
                sample = WorldDefinition.ClampSample(sample);
                if (TryReadEdit(sample, out SampleEdit changed))
                    return changed;
                Caves.Collect(sample, sample, caveParts);
                float height = HeightAt(sample.x * 0.5, sample.z * 0.5);
                return new SampleEdit(NonzeroDensity(Carve(sample, sample.y * Spacing - height)), null);
            }
            if (TryReadEdit(sample, out SampleEdit edit))
                return edit;
            Vector3 p = (Vector3)sample * Spacing;
            return new SampleEdit(BaseDensity(p, HeightAt(p.x, p.z), cave.Contains(p)), null);
        }

        private bool TryReadEdit(Vector3Int sample, out SampleEdit edit)
        {
            if (editedPages.TryGetValue(FloorChunk(sample), out var page))
                return page.TryGetValue(sample, out edit);
            edit = default;
            return false;
        }

        private void WriteEdit(Vector3Int sample, SampleEdit edit)
        {
            Vector3Int key = FloorChunk(sample);
            if (!editedPages.TryGetValue(key, out var page))
            {
                page = new Dictionary<Vector3Int, SampleEdit>(32);
                editedPages.Add(key, page);
            }
            if (page.TryAdd(sample, edit))
                EditedSamples++;
            else
                page[sample] = edit;
        }

        private float BaseDensity(Vector3 point, float surface, bool hasCave)
        {
            float density = point.y - surface;
            if (hasCave && cave.Contains(point))
                density = Mathf.Max(density, -DensityVolume.RoundedBox(point - cave.center, cave.extents, 0.85f));
            return NonzeroDensity(density);
        }

        internal static float NonzeroDensity(float density) => density == 0f ? 0.0001f : density;

        private static Vector3Int FloorChunk(Vector3Int sample)
        {
            return new Vector3Int(FloorDiv(sample.x), FloorDiv(sample.y), FloorDiv(sample.z));
        }

        private static int FloorDiv(int sample) => sample >= 0 ? sample / CellCount : (sample + 1) / CellCount - 1;

        private static float GenerateHeight(float x, float z)
        {
            float a = Mathf.PerlinNoise(17.3f + x / 510f, 47.1f + z / 510f);
            float b = Mathf.PerlinNoise(61.9f + x / 175f, 29.7f + z / 175f);
            float c = Mathf.PerlinNoise(116.1f + x / 290f, 6.3f + z / 290f);
            float island = 1f - new Vector2((x + 33f) / 755f, (z - 28f) / 728f).magnitude
                           + (a - 0.5f) * 0.31f + (b - 0.5f) * 0.09f;
            if (island <= 0f)
                return -Mathf.Lerp(0f, 56f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.34f, -island)));

            float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.55f, island));
            float ridges = Mathf.Pow(0.52f * b + 0.48f * c, 2f);
            return Mathf.Min(140f, island * 31f + rise * (12f + 150f * ridges));
        }

        private readonly struct SampleEdit
        {
            internal readonly float Density;
            // A rounded box can protrude into the next logical cell; clicking it targets its source.
            // Sculpted fill can span many cells, so it is marked as placed but picked where it is hit.
            internal readonly Vector3Int? Source;
            internal readonly bool SculptedFill;

            internal SampleEdit(float density, Vector3Int? source, bool sculptedFill = false)
            {
                Density = density;
                Source = source;
                SculptedFill = sculptedFill;
            }
        }

        private readonly struct GradeColumn
        {
            internal readonly int X, Z, Bottom, Top;
            internal readonly double Height;
            internal GradeColumn(int x, int z, int bottom, int top, double height)
            {
                X = x;
                Z = z;
                Bottom = bottom;
                Top = top;
                Height = height;
            }
        }

        private readonly struct EditSampleCache
        {
            internal readonly Vector3Int Page;
            internal readonly float[] Natural;

            internal EditSampleCache(Vector3Int page, float[] natural)
            {
                Page = page;
                Natural = natural;
            }
        }
    }
}
