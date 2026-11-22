using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Reol2.World
{
    internal sealed class ContinentHorizon
    {
        private const int TileSide = 32;
        private const int TileCount = ContinentField.Columns / TileSide;
        private readonly Mesh[,] tiles = new Mesh[TileCount, TileCount];
        private readonly bool[,] covered = new bool[ContinentField.Columns, ContinentField.Columns];
        private readonly HashSet<Vector2Int> dirty = new();
        private readonly List<int> indices = new(TileSide * TileSide * 6);

        internal ContinentHorizon(Transform parent, ContinentField field, Material material)
        {
            var root = new GameObject("Distant land and seabed");
            root.transform.SetParent(parent, false);
            for (int z = 0; z < TileCount; z++)
            for (int x = 0; x < TileCount; x++)
            {
                var tile = new GameObject($"Horizon {x}, {z}", typeof(MeshFilter), typeof(MeshRenderer));
                tile.transform.SetParent(root.transform, false);
                Mesh mesh = CreateTile(field, x, z);
                tiles[x, z] = mesh;
                tile.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = tile.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                RefreshTile(x, z);
            }
        }

        internal bool SetCovered(Vector2Int column, bool value)
        {
            int x = column.x - ContinentField.FirstColumn;
            int z = column.y - ContinentField.FirstColumn;
            if (x < 0 || z < 0 || x >= ContinentField.Columns || z >= ContinentField.Columns || covered[x, z] == value)
                return false;
            covered[x, z] = value;
            dirty.Add(new Vector2Int(x / TileSide, z / TileSide));
            return true;
        }

        internal void Flush()
        {
            foreach (Vector2Int tile in dirty)
                RefreshTile(tile.x, tile.y);
            dirty.Clear();
        }

        internal void Release()
        {
            foreach (Mesh mesh in tiles)
                Object.Destroy(mesh);
        }

        private void RefreshTile(int tileX, int tileZ)
        {
            int baseX = tileX * TileSide;
            int baseZ = tileZ * TileSide;
            int stride = TileSide + 1;
            indices.Clear();
            for (int z = 0; z < TileSide; z++)
            for (int x = 0; x < TileSide; x++)
            {
                if (covered[baseX + x, baseZ + z])
                    continue;
                int a = x + z * stride;
                indices.Add(a);
                indices.Add(a + stride);
                indices.Add(a + 1);
                indices.Add(a + 1);
                indices.Add(a + stride);
                indices.Add(a + stride + 1);
            }
            tiles[tileX, tileZ].SetTriangles(indices, 0, calculateBounds: false);
        }

        private static Mesh CreateTile(ContinentField field, int tileX, int tileZ)
        {
            int stride = TileSide + 1;
            var vertices = new Vector3[stride * stride];
            var normals = new Vector3[vertices.Length];
            var colors = new Color32[vertices.Length];
            for (int z = 0; z < stride; z++)
            for (int x = 0; x < stride; x++)
            {
                float step = ContinentField.ChunkSize;
                float worldX = (tileX * TileSide + x) * step - ContinentField.Extent;
                float worldZ = (tileZ * TileSide + z) * step - ContinentField.Extent;
                int i = x + z * stride;
                var p = new Vector3(worldX, field.HeightAt(worldX, worldZ), worldZ);
                var normal = new Vector3(field.HeightAt(worldX - step, worldZ) - field.HeightAt(worldX + step, worldZ),
                                         step * 2f,
                                         field.HeightAt(worldX, worldZ - step) - field.HeightAt(worldX, worldZ + step)).normalized;
                vertices[i] = p;
                normals[i] = normal;
                colors[i] = field.MaterialAt(new WorldPosition(p.x, p.y, p.z), normal, p.y, step);
            }
            var mesh = new Mesh { name = $"Continent horizon {tileX}, {tileZ}" };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.colors32 = colors;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
