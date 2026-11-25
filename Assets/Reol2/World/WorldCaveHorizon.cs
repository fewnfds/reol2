using System;
using System.Collections.Generic;
using System.Diagnostics;
using Reol2.Voxels;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Reol2.World
{
    internal sealed class WorldCaveHorizon
    {
        internal const int SampleScale = 16;
        internal const int PageWidth = ContinentField.ChunkSize * SampleScale;
        private const int EntranceScale = 2;
        private const int EntrancePageWidth = ContinentField.ChunkSize * EntranceScale;
        private const int EntranceBand = 64;
        private const int MaximumEntrances = 4;
        private const int InitialPageCapacity = 232;
        private static readonly Vector3Int[] CoverageFaces =
        {
            Vector3Int.left, Vector3Int.down, Vector3Int.back,
            Vector3Int.right, Vector3Int.up, Vector3Int.forward
        };
        private readonly ContinentField field;
        private readonly Transform root;
        private readonly Material material;
        private readonly Material entranceMaterial;
        private readonly WorldHorizon horizon;
        private Texture3D coverage;
        private byte[] coverageData;
        private Vector3Int coverageFirst;
        private Vector3 coverageScale;
        private readonly HashSet<Vector3Int> surfaceCoverage = new(24);
        private readonly MarchingCubes mesher = new();
        private readonly MarchingCubes entranceMesher = new(spacing: ContinentField.Spacing * EntranceScale);
        private readonly List<CavePart> parts = new(12);
        private readonly HashSet<Vector3Int> desired = new(InitialPageCapacity);
        private readonly List<Vector3Int> queued = new(InitialPageCapacity);
        private readonly List<Vector3Int> retiring = new(InitialPageCapacity);
        private readonly Dictionary<Vector3Int, CaveTile> ready = new(InitialPageCapacity);
        private readonly Stack<CaveTile> pool = new(InitialPageCapacity);
        private readonly HashSet<Vector3Int> entrances = new();
        private readonly HashSet<Vector3Int> nextEntrances = new();
        private readonly HashSet<Vector3Int> visibleEntrances = new();
        private readonly HashSet<Vector3Int> entrancePages = new();
        private readonly List<Vector3Int> entranceQueue = new();
        private readonly Dictionary<Vector3Int, CaveTile> entranceReady = new();
        private readonly Stack<CaveTile> entrancePool = new();
        private readonly Vector4[] entranceBounds = new Vector4[MaximumEntrances];
        private readonly Func<Vector3Int, Vector3, Vector3, Color32> color;
        private readonly Func<Vector3Int, Vector3, Vector3, Color32> entranceColor;
        private readonly Comparison<Vector3Int> compare;
        private Vector3Int center;
        private int next;
        private int nextEntrance;
        private bool preferEntrance;
        private bool initialized;
        private bool coverageDirty;

        internal int VisibleTiles => tileCount;
        internal bool HasPendingPages => next < queued.Count;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || REOL_TEST_BUILD
        internal (int Ready, int Pending) EntranceProgress => (entranceReady.Count, entranceQueue.Count - nextEntrance);
#endif
        private int tileCount;

        internal WorldCaveHorizon(Transform parent, ContinentField field, WorldHorizon horizon)
        {
            this.field = field;
            this.horizon = horizon;
            material = horizon.CaveMaterial;
            entranceMaterial = horizon.CaveEntranceMaterial;
            root = new GameObject("Distant cavern surfaces").transform;
            root.SetParent(parent, false);
            compare = CompareDistance;
            color = CaveColor;
            entranceColor = EntranceColor;
        }

        internal void Stream(WorldPosition point, WorldFrame frame, double budgetMs)
        {
            Vector3Int cell = point.Cell;
            var key = new Vector3Int(cell.x >> 7, cell.y >> 7, cell.z >> 7);
            if (!initialized || key != center)
            {
                initialized = true;
                center = key;
                Select(frame);
            }

            long start = Stopwatch.GetTimestamp();
            for (int work = 0; budgetMs > 0 && work < 8 && (next < queued.Count || nextEntrance < entranceQueue.Count); work++)
            {
                bool detail = nextEntrance < entranceQueue.Count && (next >= queued.Count || preferEntrance);
                preferEntrance = !preferEntrance;
                Vector3Int page = detail ? entranceQueue[nextEntrance++] : queued[next++];
                if (detail && !field.CavePageMayHaveSurface(page, EntrancePageWidth))
                {
                    entranceReady.Add(page, null);
                    if ((Stopwatch.GetTimestamp() - start) * (1000d / Stopwatch.Frequency) >= budgetMs)
                        break;
                    continue;
                }
                MarchingCubes builder = detail ? entranceMesher : mesher;
                int scale = detail ? EntranceScale : SampleScale;
                int width = ContinentField.ChunkSize * scale;
                Stack<CaveTile> spare = detail ? entrancePool : pool;
                CaveTile tile = null;
                PageContent content = field.CopyCaveProxy(page, builder.SampleBuffer, scale);
                if (content == PageContent.Mixed)
                {
                    builder.Extract(page, detail ? entranceColor : color);
                    if (builder.TriangleCount != 0)
                    {
                        tile = spare.Count > 0 ? spare.Pop() : new CaveTile(root, detail ? entranceMaterial : material);
                        tile.Place(page, width, frame);
                        builder.Upload(tile.Mesh);
                        tile.Renderer.enabled = !detail;
                        if (!detail)
                            tileCount++;
                    }
                }
                if (detail)
                    entranceReady.Add(page, tile);
                else
                {
                    if ((tile != null || content == PageContent.Air) && field.CaveProxyIncludesGround(page, content))
                        coverageDirty |= surfaceCoverage.Add(page);
                    ready.Add(page, tile);
                }
                if (tile != null || (Stopwatch.GetTimestamp() - start) * (1000d / Stopwatch.Frequency) >= budgetMs)
                    break;
            }
            if (visibleEntrances.Count != entrances.Count && nextEntrance == entranceQueue.Count)
            {
                visibleEntrances.UnionWith(entrances);
                foreach (CaveTile tile in entranceReady.Values)
                    if (tile != null)
                        tile.Renderer.enabled = true;
                PlaceEntrances(frame);
            }
            if (coverageDirty)
                PublishCoverage(frame);
        }

        private void Select(WorldFrame frame)
        {
            // Keep the entire nearby cavern shell. Cutting at the search bounds exposes the sky inside the mountain.
            var range = new Vector3Int(32, 16, 32);
            Vector3Int min = center - range;
            Vector3Int max = center + range + Vector3Int.one;
            field.Caves.Collect(min * (PageWidth * 2), max * (PageWidth * 2) - Vector3Int.one, parts, wholeSystems: true);
            nextEntrances.Clear();
            foreach (CavePart part in parts)
                if (part.IsEntrance && nextEntrances.Count < MaximumEntrances)
                    nextEntrances.Add(part.EntranceSample);
            if (!entrances.SetEquals(nextEntrances))
                SelectEntrances(frame);
            desired.Clear();
            queued.Clear();
            next = 0;
            foreach (CavePart part in parts)
            {
                BoundsInt extent = part.SampleRange;
                Vector3Int first = Floor(extent.min - Vector3Int.one * SampleScale);
                Vector3Int last = Floor(extent.max + Vector3Int.one * SampleScale) + Vector3Int.one;
                foreach (Vector3Int key in new BoundsInt(first, last - first).allPositionsWithin)
                    if (desired.Add(key) && !ready.ContainsKey(key))
                        queued.Add(key);
            }

            queued.Sort(compare);
            retiring.Clear();
            foreach (var entry in ready)
                if (!desired.Contains(entry.Key))
                    retiring.Add(entry.Key);
            foreach (Vector3Int key in retiring)
            {
                CaveTile tile = ready[key];
                if (tile != null)
                {
                    tile.Renderer.enabled = false;
                    pool.Push(tile);
                    tileCount--;
                }
                ready.Remove(key);
                coverageDirty |= surfaceCoverage.Remove(key);
            }
        }

        private void SelectEntrances(WorldFrame frame)
        {
            entrances.Clear();
            entrances.UnionWith(nextEntrances);
            int wasVisible = visibleEntrances.Count;
            visibleEntrances.IntersectWith(entrances);
            if (visibleEntrances.Count != wasVisible)
                PlaceEntrances(frame);

            entranceQueue.Clear();
            entrancePages.Clear();
            nextEntrance = 0;
            foreach (Vector3Int mouth in entrances)
            {
                const int half = EntranceBand / EntrancePageWidth;
                var tile = new Vector3Int(mouth.x >> 5, mouth.y >> 5, mouth.z >> 5);
                Vector3Int range = new(half, half, half);
                foreach (Vector3Int key in new BoundsInt(tile - range, 2 * range + Vector3Int.one).allPositionsWithin)
                    if (entrancePages.Add(key) && !entranceReady.ContainsKey(key))
                        entranceQueue.Add(key);
            }
            retiring.Clear();
            foreach (Vector3Int key in entranceReady.Keys)
                if (!entrancePages.Contains(key))
                    retiring.Add(key);
            foreach (Vector3Int key in retiring)
            {
                CaveTile tile = entranceReady[key];
                if (tile != null)
                {
                    tile.Renderer.enabled = false;
                    entrancePool.Push(tile);
                }
                entranceReady.Remove(key);
            }
            var focus = center * (PageWidth / EntrancePageWidth);
            entranceQueue.Sort((a, b) => (a - focus).sqrMagnitude.CompareTo((b - focus).sqrMagnitude));
        }

        internal void Reposition(WorldFrame frame)
        {
            foreach (var entry in ready)
                entry.Value?.Place(entry.Key, PageWidth, frame);
            foreach (var entry in entranceReady)
                entry.Value?.Place(entry.Key, EntrancePageWidth, frame);
            PlaceCoverage(frame);
            if (visibleEntrances.Count != 0)
                PlaceEntrances(frame);
        }

        private void PlaceEntrances(WorldFrame frame)
        {
            int count = 0;
            foreach (Vector3Int mouth in visibleEntrances)
            {
                Vector3Int key = new(mouth.x >> 5, mouth.y >> 5, mouth.z >> 5);
                Vector3 local = (Vector3)(key * EntrancePageWidth - frame.Origin);
                // Keep height tiles out of the opening. Stencil coverage gives the
                // detailed shell priority, with the coarse shell beyond its last triangle.
                float radius = EntranceBand - EntrancePageWidth * 0.5f;
                Vector3 middle = local + Vector3.one * (EntrancePageWidth * 0.5f);
                entranceBounds[count++] = new Vector4(middle.x, middle.y, middle.z, radius);
            }
            horizon.SetCaveEntrances(entranceBounds, count);
        }

        private void PublishCoverage(WorldFrame frame)
        {
            coverageDirty = false;
            if (surfaceCoverage.Count > 0)
            {
                Vector3Int first = new(int.MaxValue, int.MaxValue, int.MaxValue);
                Vector3Int last = new(int.MinValue, int.MinValue, int.MinValue);
                foreach (Vector3Int page in surfaceCoverage)
                {
                    first = Vector3Int.Min(first, page);
                    last = Vector3Int.Max(last, page);
                }
                coverageFirst = first;
                EnsureCoverage(last - first + Vector3Int.one);
                Array.Clear(coverageData, 0, coverageData.Length);
                foreach (Vector3Int page in surfaceCoverage)
                {
                    Vector3Int local = page - first;
                    // Keep one sample of height mesh overlap at exposed coverage faces.
                    // The two meshes can intersect a page plane at different positions.
                    byte mask = 1 << 6;
                    for (int face = 0; face < CoverageFaces.Length; face++)
                        if (!surfaceCoverage.Contains(page + CoverageFaces[face]))
                            mask |= (byte)(1 << face);
                    coverageData[local.x + coverage.width * (local.y + coverage.height * local.z)] = mask;
                }
                coverage.SetPixelData(coverageData, 0);
                coverage.Apply(false);
            }
            PlaceCoverage(frame);
        }

        private void EnsureCoverage(Vector3Int size)
        {
            if (coverage != null && size.x <= coverage.width && size.y <= coverage.height && size.z <= coverage.depth)
                return;
            int width = Mathf.NextPowerOfTwo(Math.Max(size.x, coverage?.width ?? 16));
            int height = Mathf.NextPowerOfTwo(Math.Max(size.y, coverage?.height ?? 8));
            int depth = Mathf.NextPowerOfTwo(Math.Max(size.z, coverage?.depth ?? 8));
            Object.Destroy(coverage);
            coverage = new Texture3D(width, height, depth, TextureFormat.R8, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "Streamed cavern coverage"
            };
            coverageData = new byte[width * height * depth];
            coverageScale = new Vector3(1f / (width * PageWidth), 1f / (height * PageWidth), 1f / (depth * PageWidth));
            horizon.SetCaveCoverageTexture(coverage);
        }

        private void PlaceCoverage(WorldFrame frame)
        {
            Vector3Int origin = coverageFirst * PageWidth - frame.Origin;
            horizon.SetCaveCoverageBounds(origin, surfaceCoverage.Count == 0 ? Vector3.zero : coverageScale);
        }

        internal void Release()
        {
            foreach (CaveTile tile in ready.Values)
                if (tile != null)
                    Object.Destroy(tile.Mesh);
            foreach (CaveTile tile in pool)
                Object.Destroy(tile.Mesh);
            foreach (CaveTile tile in entranceReady.Values)
                if (tile != null)
                    Object.Destroy(tile.Mesh);
            foreach (CaveTile tile in entrancePool)
                Object.Destroy(tile.Mesh);
            Object.Destroy(coverage);
        }

        private static Vector3Int Floor(Vector3Int sample) => new(sample.x >> 8, sample.y >> 8, sample.z >> 8);

        private int CompareDistance(Vector3Int a, Vector3Int b) => (a - center).sqrMagnitude.CompareTo((b - center).sqrMagnitude);

        private Color32 CaveColor(Vector3Int key, Vector3 local, Vector3 normal) =>
            field.MaterialAt(new WorldPosition(key.x * (double)PageWidth + local.x * SampleScale,
                key.y * (double)PageWidth + local.y * SampleScale, key.z * (double)PageWidth + local.z * SampleScale),
                normal, field.CaveProxyHeight(local), ContinentField.Spacing * SampleScale);

        private Color32 EntranceColor(Vector3Int key, Vector3 local, Vector3 normal)
        {
            const float spacing = ContinentField.Spacing * EntranceScale;
            return field.MaterialAt(new WorldPosition(key.x * (double)EntrancePageWidth + local.x,
                key.y * (double)EntrancePageWidth + local.y, key.z * (double)EntrancePageWidth + local.z),
                normal, field.CaveProxyHeight(local, spacing), spacing);
        }

        private sealed class CaveTile
        {
            internal readonly Mesh Mesh = new() { name = "Cavern surface at distance" };
            internal readonly MeshRenderer Renderer;

            internal CaveTile(Transform root, Material material)
            {
                var item = new GameObject("Cavern tile", typeof(MeshFilter), typeof(MeshRenderer));
                item.transform.SetParent(root, false);
                Renderer = item.GetComponent<MeshRenderer>();
                Renderer.sharedMaterial = material;
                Renderer.shadowCastingMode = ShadowCastingMode.Off;
                item.GetComponent<MeshFilter>().sharedMesh = Mesh;
            }

            internal void Place(Vector3Int key, int width, WorldFrame frame)
            {
                Renderer.transform.localPosition = (Vector3)(key * width - frame.Origin);
                Renderer.transform.localScale = Vector3.one * (width == PageWidth ? SampleScale : 1f);
            }
        }
    }
}
