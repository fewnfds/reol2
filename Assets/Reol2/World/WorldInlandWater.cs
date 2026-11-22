using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Reol2.World
{
    internal sealed class WorldInlandWater
    {
        private readonly Mesh mesh;
        private readonly MeshRenderer renderer;
        private readonly Vector2Int center;

        internal WorldInlandWater(Transform parent, WorldSurface surface, Material material)
        {
            center = surface.Center;
            const int lakeSides = 96;
            const int riverStep = 64;
            int rows = (WorldSurface.RiverHead - WorldSurface.RiverEnd) / riverStep + 1;
            int riverVertices = rows * 2;
            var vertices = new Vector3[riverVertices + lakeSides + 1];
            var indices = new int[(rows - 1) * 6 + lakeSides * 3];
            int write = 0;
            for (int row = 0; row < rows; row++)
            {
                int north = WorldSurface.RiverHead - row * riverStep;
                Vector3 river = surface.RiverAt(north);
                int a = row * 2;
                vertices[a] = new Vector3(river.x - river.z, river.y, north);
                vertices[a + 1] = new Vector3(river.x + river.z, river.y, north);
                if (row == rows - 1)
                    continue;
                indices[write++] = a; indices[write++] = a + 1; indices[write++] = a + 2;
                indices[write++] = a + 1; indices[write++] = a + 3; indices[write++] = a + 2;
            }
            vertices[riverVertices] = new Vector3(WorldSurface.BasinEast, WorldSurface.LakeLevel, WorldSurface.BasinNorth);
            for (int i = 0; i < lakeSides; i++)
            {
                float angle = i * (2f * Mathf.PI / lakeSides);
                vertices[riverVertices + i + 1] = new Vector3(WorldSurface.BasinEast + Mathf.Cos(angle) * WorldSurface.BasinWidth * 0.66f,
                    WorldSurface.LakeLevel, WorldSurface.BasinNorth + Mathf.Sin(angle) * WorldSurface.BasinLength * 0.66f);
                indices[write++] = riverVertices;
                indices[write++] = riverVertices + (i + 1) % lakeSides + 1;
                indices[write++] = riverVertices + i + 1;
            }

            var obj = new GameObject("River and basin water", typeof(MeshFilter), typeof(MeshRenderer));
            obj.transform.SetParent(parent, false);
            mesh = new Mesh { name = "Main river and lake" };
            mesh.vertices = vertices;
            mesh.triangles = indices;
            obj.GetComponent<MeshFilter>().sharedMesh = mesh;
            renderer = obj.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        internal void Place(WorldFrame frame) => renderer.transform.localPosition =
            (Vector3)(new Vector3Int(center.x, 0, center.y) - frame.Origin);

        internal void SetRipples(MaterialPropertyBlock props) => renderer.SetPropertyBlock(props);
        internal void Release() => Object.Destroy(mesh);
    }
}
