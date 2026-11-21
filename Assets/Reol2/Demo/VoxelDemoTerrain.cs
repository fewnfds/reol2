using System.Diagnostics;
using Reol2.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace Reol2.Demo
{
    [DisallowMultipleComponent]
    public sealed class VoxelDemoTerrain : MonoBehaviour
    {
        private static readonly Vector3Int FirstChunk = new(-1, 0, 0);
        private static readonly Vector3Int ChunkCount = new(2, 1, 1);
        // Reserve both the floor and the standing space around the return point.
        private static readonly BoundsInt TrailheadCells = new(
            Vector3Int.FloorToInt(Trailhead) - Vector3Int.one, new Vector3Int(3, 4, 3));
        private const float Reach = 4.6f;
        private const float SurfaceNudge = 0.025f;

        [SerializeField] private Material surfaceMaterial = null;

        private DensityVolume density;
        private MarchingCubes mesher;
        private Chunk[] chunks;

        public static Vector3 Trailhead => new(-6.5f, GroundHeight(-6.5f, 4.5f) + 0.08f, 4.5f);
        public static Vector3Int GateLowerCell => new(0, 3, 4);
        public static Vector3Int GateUpperCell => new(0, 4, 4);
        public EditTiming LastEdit { get; private set; }

        private void Awake()
        {
            density = new DensityVolume(FirstChunk, ChunkCount, InitialDensity);
            mesher = new MarchingCubes();
            chunks = new[]
            {
                new Chunk(transform, FirstChunk, surfaceMaterial),
                new Chunk(transform, Vector3Int.zero, surfaceMaterial)
            };
            Rebuild(density.SampleBounds);
        }

        public void ResetTerrain()
        {
            density.Reset();
            Rebuild(density.SampleBounds);
        }

        public bool TryEdit(Vector3Int cell, bool fill, CharacterController occupant = null)
        {
            LastEdit = default;
            if (!CanEditCell(cell) || (fill && FillIntersectsPlayer(cell, occupant)))
                return false;
            if (!density.Edit(cell, fill, out BoundsInt changed))
                return false;

            Rebuild(changed);
            return true;
        }

        public bool TryPick(Ray aim, Vector3 playerEye, out Vector3Int dig, out Vector3Int fill)
        {
            dig = fill = default;
            float rayDistance = Reach + Vector3.Distance(playerEye, aim.origin);
            if (!Physics.Raycast(aim, out RaycastHit hit, rayDistance, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore)
                || hit.collider.transform.parent != transform
                || (hit.point - playerEye).sqrMagnitude > Reach * Reach)
                return false;

            // A camera peeking around terrain should not be able to edit through the intervening wall.
            Vector3 direction = hit.point - playerEye;
            if (Physics.Raycast(playerEye, direction, direction.magnitude - 0.08f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;

            dig = density.DigCellAt(hit.point + aim.direction * SurfaceNudge);
            fill = DensityVolume.CellAt(hit.point + hit.normal * 0.35f);
            return CanEditCell(dig);
        }

        public bool CanFill(Vector3Int cell, CharacterController occupant)
        {
            return CanEditCell(cell) && !FillIntersectsPlayer(cell, occupant);
        }

        private bool CanEditCell(Vector3Int cell)
        {
            return density.ContainsEditCell(cell) && !TrailheadCells.Contains(cell);
        }

        private static bool FillIntersectsPlayer(Vector3Int cell, CharacterController occupant)
        {
            if (occupant == null)
                return false;

            // Mesh triangles can extend from a changed sample to its next neighbor.
            // Test the player's bounds against this conservative extent before touching the field.
            float size = 1f + DensityVolume.Spacing * 2f;
            return occupant.bounds.Intersects(new Bounds(DensityVolume.CellCenter(cell), Vector3.one * size));
        }

        private void Rebuild(BoundsInt changed)
        {
            var timing = new EditTiming();
            for (int i = 0; i < chunks.Length; i++)
            {
                Chunk chunk = chunks[i];
                if (!Overlaps(chunk.Samples, changed))
                    continue;

                long start = Stopwatch.GetTimestamp();
                mesher.Extract(density, chunk.Coordinate);
                long extracted = Stopwatch.GetTimestamp();
                mesher.Upload(chunk.PendingMesh);
                long uploaded = Stopwatch.GetTimestamp();
                chunk.PendingHasSurface = mesher.TriangleCount != 0;
                if (chunk.PendingHasSurface)
                    Physics.BakeMesh(chunk.PendingMesh.GetEntityId(), convex: false, chunk.Collider.cookingOptions);

                timing.ExtractMs += Milliseconds(extracted - start);
                timing.UploadMs += Milliseconds(uploaded - extracted);
                timing.BakeMs += Milliseconds(Stopwatch.GetTimestamp() - uploaded);
                timing.ChunkCount++;
                chunk.Pending = true;
            }

            // Publish the whole edit in one main-thread turn. Both users of the mesh see the same version.
            long publish = Stopwatch.GetTimestamp();
            for (int i = 0; i < chunks.Length; i++)
            {
                if (chunks[i].Pending)
                    chunks[i].SwapMeshes();
            }

            timing.PublishMs = Milliseconds(Stopwatch.GetTimestamp() - publish);
            LastEdit = timing;
        }

        private static bool Overlaps(BoundsInt a, BoundsInt b)
        {
            return a.xMin < b.xMax && a.xMax > b.xMin && a.yMin < b.yMax && a.yMax > b.yMin
                && a.zMin < b.zMax && a.zMax > b.zMin;
        }

        private static double Milliseconds(long ticks) => ticks * (1000d / Stopwatch.Frequency);

        public static float GroundHeight(float x, float z)
        {
            float rise = Mathf.Clamp((x + 6.1f) * 0.26f, 0f, 1.68f);
            float offTrail = Mathf.Clamp01(Mathf.Abs(z - 4.5f) * 0.35f);
            return 1.48f + rise + offTrail * (0.16f + Mathf.Sin(x * 0.7f + z) * 0.20f);
        }

        private static float InitialDensity(Vector3 p)
        {
            float island = DensityVolume.RoundedBox(p - new Vector3(0f, 2.1f, 4f), new Vector3(7.74f, 3f, 3.78f), 0.65f);
            float ground = Mathf.Max(island, Mathf.Max(p.y - GroundHeight(p.x, p.z), 0.33f - p.y));
            float ridge = DensityVolume.RoundedBox(p - new Vector3(3.44f, 4.40f, 4.48f), new Vector3(3.16f, 1.98f, 2.69f), 0.74f);
            float crown = DensityVolume.RoundedBox(p - new Vector3(4.53f, 5.74f, 3.24f), new Vector3(2.03f, 0.94f, 1.37f), 0.72f);
            float tunnel = DensityVolume.RoundedBox(p - new Vector3(3.59f, 4.14f, 4.5f), new Vector3(2.73f, 1.21f, 1.12f), 0.22f);
            return Mathf.Min(ground, Mathf.Max(Mathf.Min(ridge, crown), -tunnel));
        }

        private void OnDestroy()
        {
            if (chunks == null)
                return;
            foreach (Chunk chunk in chunks)
                chunk.Release();
        }

        public struct EditTiming
        {
            public int ChunkCount;
            public double ExtractMs;
            public double UploadMs;
            public double BakeMs;
            public double PublishMs;
        }

        private sealed class Chunk
        {
            private readonly MeshFilter filter;
            private Mesh displayedMesh;

            internal readonly Vector3Int Coordinate;
            internal readonly BoundsInt Samples;
            internal readonly MeshCollider Collider;
            internal Mesh PendingMesh { get; private set; }
            internal bool Pending { get; set; }
            internal bool PendingHasSurface { get; set; }

            internal Chunk(Transform parent, Vector3Int coordinate, Material material)
            {
                Coordinate = coordinate;
                Samples = DensityVolume.ChunkSampleBounds(coordinate);
                var root = new GameObject($"Terrain chunk {coordinate.x}", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                root.transform.SetParent(parent, false);
                root.transform.localPosition = (Vector3)coordinate * DensityVolume.ChunkSize;
                filter = root.GetComponent<MeshFilter>();
                root.GetComponent<MeshRenderer>().sharedMaterial = material;
                Collider = root.GetComponent<MeshCollider>();
                PendingMesh = CreateMesh(root.name);
                displayedMesh = CreateMesh(root.name);
            }

            internal void SwapMeshes()
            {
                Collider.sharedMesh = PendingHasSurface ? PendingMesh : null;
                filter.sharedMesh = PendingMesh;
                (displayedMesh, PendingMesh) = (PendingMesh, displayedMesh);
                Pending = false;
            }

            internal void Release()
            {
                Destroy(displayedMesh);
                Destroy(PendingMesh);
            }

            private static Mesh CreateMesh(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt16 };
                mesh.MarkDynamic();
                return mesh;
            }
        }
    }
}
