#if DEVELOPMENT_BUILD || REOL_TEST_BUILD
using System;
using System.Collections;
using UnityEngine;

namespace Reol2.World
{
    internal sealed partial class ContinentBenchmark
    {
        private IEnumerator CheckTerrainPersistence(string mode)
        {
            bool write = mode == "write";
            bool verify = mode == "verify";
            if (!Check(terrain.TerrainSaveEnabled && (write || verify || mode == "read") && terrain.Seed == 20261121,
                "terrain persistence requires write, read or verify and a fresh -world-save-root"))
                yield break;
            if (!Check(write ? terrain.EditedSamples == 0 : terrain.EditedSamples > 0,
                "terrain persistence starts with the expected saved edit count"))
                yield break;

            WorldPosition home = terrain.WorldLanding;
            var spawnCell = new WorldPosition(home.X, home.Y + 0.25d, home.Z).Cell;
            if (!write && !Check(!terrain.AllowsEntry(home) && terrain.AllowsEntry(player.WorldFeet)
                && Math.Abs(player.WorldFeet.X - home.X) < 10d && Math.Abs(player.WorldFeet.Z - home.Z) < 10d,
                "restored blocked spawn lands the player nearby with room to stand"))
                yield break;
            if (write && !Check(terrain.TryEdit(spawnCell, true) && terrain.TryEdit(spawnCell + Vector3Int.up, true)
                && !terrain.AllowsEntry(home), "fill the original spawn before saving"))
                yield break;

            if (terrain.Landforms == null && !CheckSavedGrade(write, home))
                yield break;

            WorldPosition junction = terrain.CavePassage[3];
            Vector3Int cell = junction.Page * ContinentField.ChunkSize;
            var centre = new WorldPosition(cell.x + 0.5, cell.y + 0.5, cell.z + 0.5);
            var observer = new WorldPosition(centre.X, centre.Y + 2.5, centre.Z);
            if (!Check(player.EnterFlight(observer, 0f, 82f), "saved grid fill arrival"))
                yield break;
            Vector3 eye = terrain.ToLocal(observer);
            var ray = new Ray(eye, Vector3.down);
            if (write && !Check(terrain.TryEdit(cell, fill: true), "fill at the three-axis page corner"))
                yield break;
            bool hit = terrain.Pick(ray, eye, out Vector3Int picked, out _, out _, out bool placed);
            if (verify)
            {
                if (!Check(!Physics.Raycast(ray, 2.5f), "the dig committed by the second process removed the visible collision"))
                    yield break;
            }
            else
            {
                if (!Check(hit && placed && picked == cell && Physics.Raycast(ray, out RaycastHit ground, 2.5f)
                    && ground.collider is MeshCollider collider && collider.TryGetComponent(out MeshFilter filter)
                    && collider.sharedMesh == filter.sharedMesh, "save restores fill source and matching display and collision"))
                    yield break;
                if (!write && !Check(terrain.TryEdit(picked, fill: false) && !Physics.Raycast(ray, 2.5f),
                    "can dig the restored block and update collision"))
                    yield break;
            }
            if (write)
            {
                float deadline = Time.realtimeSinceStartup + 8f;
                while (terrain.TerrainEditsPending && Time.realtimeSinceStartup < deadline)
                    yield return null;
                if (!Check(!terrain.TerrainEditsPending, "terrain autosave committed while the player was still open"))
                    yield break;
            }
            Debug.Log($"Terrain persistence | {mode} | {terrain.EditedSamples} edited samples | {terrain.TerrainSaveStatus}");
            Application.Quit(0);
        }

        private bool CheckSavedGrade(bool write, WorldPosition home)
        {
            const double length = 20d;
            double x = home.X + 32, z = home.Z + 40.25;
            double left = terrain.SurfaceHeight(new WorldPosition(x, 0, z));
            double right = terrain.SurfaceHeight(new WorldPosition(x + length, 0, z));
            int direction = left >= right ? 1 : -1;
            double startX = direction == 1 ? x : x + length, startY = (left + right) * 0.5 - 0.5;
            var start = new WorldPosition(startX, startY, z);
            var end = new WorldPosition(startX + direction * length, startY + 1d, z);
            if (!Check(player.EnterFlight(new WorldPosition(x + length * 0.5, startY + 2.8, z - 4), 0f, 30f),
                "saved grade arrival"))
                return false;
            terrain.PrepareEntry(terrain.ToLocal(start));
            terrain.PrepareEntry(terrain.ToLocal(end));
            if (write && !Check(terrain.TryGrade(start, end), "grade before saving"))
                return false;

            double error = 0d;
            for (int i = 0; i <= 40; i++)
            {
                Vector3 source = terrain.ToLocal(new WorldPosition(startX + direction * i * 0.5, startY + 3d, z));
                if (!Check(Physics.Raycast(source, Vector3.down, out RaycastHit hit, 4f) && hit.collider is MeshCollider,
                    "saved slope collision coverage"))
                    return false;
                error = Math.Max(error, Math.Abs(terrain.ToWorld(hit.point).Y - (startY + i * 0.025)));
            }
            Debug.Log(FormattableString.Invariant($"Terrain persistence | 20 m graded slope | collision error {error:F5} m"));
            return Check(error < 0.03d, "the 20 m graded profile matches after starting the game");
        }
    }
}
#endif
