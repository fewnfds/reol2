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
        private const int CoverageWidth = 16;
        private const int MaximumPages = 232;
        internal const int VisibleDistance = 700;
        private readonly ContinentField field;
        private readonly Transform root;
        private readonly Material material;
        private readonly WorldHorizon horizon;
        private readonly Texture3D coverage;
        private readonly byte[] coverageData = new byte[CoverageWidth * CoverageWidth * CoverageWidth];
        private readonly HashSet<Vector3Int> surfaceCoverage = new(24);
        private readonly MarchingCubes mesher = new();
        private readonly List<CavePart> parts = new(12);
        private readonly HashSet<Vector3Int> desired = new(MaximumPages);
        private readonly List<Vector3Int> queued = new(MaximumPages);
        private readonly List<Vector3Int> retiring = new(MaximumPages);
        private readonly Dictionary<Vector3Int, CaveTile> ready = new(MaximumPages);
        private readonly Stack<CaveTile> pool = new(MaximumPages);
        private readonly Func<Vector3Int, Vector3, Vector3, Color32> color;
        private readonly Comparison<Vector3Int> compare;
        private Vector3Int center;
        private int next;
        private bool initialized;
        private bool coverageDirty;

        internal int VisibleTiles => root.gameObject.activeSelf ? tileCount : 0;
        private int tileCount;

        internal WorldCaveHorizon(Transform parent, ContinentField field, WorldHorizon horizon)
        {
            this.field = field;
            this.horizon = horizon;
            material = horizon.CaveMaterial;
            root = new GameObject("Distant cavern surfaces").transform;
            root.SetParent(parent, false);
            root.gameObject.SetActive(false);
            coverage = new Texture3D(CoverageWidth, CoverageWidth, CoverageWidth, TextureFormat.R8, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "Streamed cavern coverage"
            };
            horizon.SetCaveCoverageTexture(coverage);
            compare = CompareDistance;
            color = CaveColor;
        }

        internal void Stream(WorldPosition point, WorldFrame frame, double budgetMs)
        {
            if (!root.gameObject.activeSelf)
            {
                root.gameObject.SetActive(true);
                coverageDirty = true;
            }
            Vector3Int cell = point.Cell;
            var key = new Vector3Int(cell.x >> 7, cell.y >> 7, cell.z >> 7);
            if (!initialized || key != center)
            {
                initialized = true;
                center = key;
                Select();
                coverageDirty |= surfaceCoverage.Count != 0;
            }

            long start = Stopwatch.GetTimestamp();
            for (int work = 0; budgetMs > 0 && work < 8 && next < queued.Count; work++)
            {
                Vector3Int page = queued[next++];
                CaveTile tile = null;
                PageContent content = field.CopyCaveProxy(page, mesher.SampleBuffer);
                if (content == PageContent.Mixed)
                {
                    mesher.Extract(page, color);
                    if (mesher.TriangleCount != 0)
                    {
                        tile = pool.Count > 0 ? pool.Pop() : new CaveTile(root, material);
                        mesher.Upload(tile.Mesh);
                        tile.Place(page, frame);
                        tile.Renderer.enabled = true;
                        tileCount++;
                    }
                }
                if ((tile != null || content == PageContent.Air) && field.CaveProxyIncludesGround(page, content))
                    coverageDirty |= surfaceCoverage.Add(page);
                ready.Add(page, tile);
                if (tile != null || (Stopwatch.GetTimestamp() - start) * (1000d / Stopwatch.Frequency) >= budgetMs)
                    break;
            }
            if (coverageDirty)
                PublishCoverage(frame);
        }

        internal void Deactivate()
        {
            if (root.gameObject.activeSelf)
            {
                root.gameObject.SetActive(false);
                horizon.SetCaveCoverageBounds(Vector4.zero);
            }
        }

        private void Select()
        {
            var range = new Vector3Int(6, 4, 6);
            Vector3Int min = center - range;
            Vector3Int max = center + range + Vector3Int.one;
            field.Caves.Collect(min * (PageWidth * 2), max * (PageWidth * 2) - Vector3Int.one, parts);
            desired.Clear();
            queued.Clear();
            next = 0;
            foreach (CavePart part in parts)
            {
                BoundsInt extent = part.SampleRange;
                Vector3Int first = Vector3Int.Max(min, Floor(extent.min - Vector3Int.one * SampleScale));
                Vector3Int last = Vector3Int.Min(max, Floor(extent.max + Vector3Int.one * SampleScale) + Vector3Int.one);
                if (first.x >= last.x || first.y >= last.y || first.z >= last.z)
                    continue;
                foreach (Vector3Int key in new BoundsInt(first, last - first).allPositionsWithin)
                    if (desired.Add(key))
                        queued.Add(key);
            }

            queued.Sort(compare);
            int selected = Math.Min(queued.Count, MaximumPages);
            int pending = 0;
            desired.Clear();
            for (int i = 0; i < selected; i++)
            {
                Vector3Int key = queued[i];
                desired.Add(key);
                if (!ready.ContainsKey(key))
                    queued[pending++] = key;
            }
            queued.RemoveRange(pending, queued.Count - pending);

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

        internal void Reposition(WorldFrame frame)
        {
            foreach (var entry in ready)
                entry.Value?.Place(entry.Key, frame);
            if (root.gameObject.activeSelf)
                PlaceCoverage(frame);
        }

        private void PublishCoverage(WorldFrame frame)
        {
            coverageDirty = false;
            if (surfaceCoverage.Count > 0)
            {
                Array.Clear(coverageData, 0, coverageData.Length);
                Vector3Int first = center - Vector3Int.one * (CoverageWidth / 2);
                foreach (Vector3Int page in surfaceCoverage)
                {
                    Vector3Int local = page - first;
                    coverageData[local.x + CoverageWidth * (local.y + CoverageWidth * local.z)] = 255;
                }
                coverage.SetPixelData(coverageData, 0);
                coverage.Apply(false);
            }
            PlaceCoverage(frame);
        }

        private void PlaceCoverage(WorldFrame frame)
        {
            Vector3Int first = center - Vector3Int.one * (CoverageWidth / 2);
            Vector3Int origin = first * PageWidth - frame.Origin;
            float scale = surfaceCoverage.Count == 0 ? 0f : 1f / (CoverageWidth * PageWidth);
            horizon.SetCaveCoverageBounds(new Vector4(origin.x, origin.y, origin.z, scale));
        }

        internal void Release()
        {
            foreach (CaveTile tile in ready.Values)
                if (tile != null)
                    Object.Destroy(tile.Mesh);
            foreach (CaveTile tile in pool)
                Object.Destroy(tile.Mesh);
            Object.Destroy(coverage);
        }

        private static Vector3Int Floor(Vector3Int sample) => new(sample.x >> 8, sample.y >> 8, sample.z >> 8);

        private int CompareDistance(Vector3Int a, Vector3Int b) => (a - center).sqrMagnitude.CompareTo((b - center).sqrMagnitude);

        private Color32 CaveColor(Vector3Int key, Vector3 local, Vector3 normal) =>
            field.MaterialAt(new WorldPosition(key.x * (double)PageWidth + local.x * SampleScale,
                key.y * (double)PageWidth + local.y * SampleScale, key.z * (double)PageWidth + local.z * SampleScale),
                normal, field.CaveProxyHeight(local), ContinentField.Spacing * SampleScale);

        private sealed class CaveTile
        {
            internal readonly Mesh Mesh = new() { name = "Cavern surface at distance" };
            internal readonly MeshRenderer Renderer;

            internal CaveTile(Transform root, Material material)
            {
                var item = new GameObject("Cavern tile", typeof(MeshFilter), typeof(MeshRenderer));
                item.transform.SetParent(root, false);
                item.transform.localScale = Vector3.one * SampleScale;
                Renderer = item.GetComponent<MeshRenderer>();
                Renderer.sharedMaterial = material;
                Renderer.shadowCastingMode = ShadowCastingMode.Off;
                item.GetComponent<MeshFilter>().sharedMesh = Mesh;
            }

            internal void Place(Vector3Int key, WorldFrame frame) => Renderer.transform.localPosition = (Vector3)(key * PageWidth - frame.Origin);
        }
    }
}
