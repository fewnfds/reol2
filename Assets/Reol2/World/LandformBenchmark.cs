#if DEVELOPMENT_BUILD || REOL_TEST_BUILD
using System;
using System.Collections;
using UnityEngine;

namespace Reol2.World
{
    internal sealed partial class ContinentBenchmark
    {
        private IEnumerator CheckLandforms(RenderTexture target)
        {
            WorldPosition landing = terrain.WorldLanding;
            RegionalLandforms region = terrain.Landforms;
            if (!Check(Math.Abs(player.WorldFeet.X - landing.X) < 0.025
                && Math.Abs(player.WorldFeet.Z - landing.Z) < 0.025, "landform scene arrival"))
                yield break;

            for (int i = 0; i < 30; i++)
                player.MoveForFrame(Vector2.zero, 0f, false, false, Step);
            yield return SettleView();
            CaptureIfRequested(target, "spawn");
            float walkHeading = 0f, minimumGrade = float.PositiveInfinity;
            float originHeight = terrain.SurfaceHeight(landing);
            foreach (Vector3 direction in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left })
            {
                float grade = 0f, previous = originHeight;
                for (int distance = 4; distance <= 24; distance += 4)
                {
                    var probe = new WorldPosition(landing.X + distance * direction.x, 0d, landing.Z + distance * direction.z);
                    float height = terrain.SurfaceHeight(probe);
                    grade = Math.Max(grade, Math.Abs(height - previous));
                    previous = height;
                }
                if (grade >= minimumGrade)
                    continue;
                minimumGrade = grade;
                walkHeading = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            }

            player.Look(walkHeading, 12f);
            yield return Measure("landform walk", 300, _ => player.MoveForFrame(Vector2.up, 0f, false, false, Step));
            double distanceWalked = Math.Sqrt(Math.Pow(player.WorldFeet.X - landing.X, 2) + Math.Pow(player.WorldFeet.Z - landing.Z, 2));
            if (!Check(distanceWalked > 12d && player.Grounded, "landform walking and grounding"))
                yield break;

            double digX = landing.Cell.x + 3.5d, digZ = landing.Cell.z + 0.5d;
            var at = new WorldPosition(digX, 0d, digZ);
            double baseHeight = terrain.SurfaceHeight(at);
            if (!Check(player.EnterAt(new WorldPosition(digX - 3d, baseHeight + 2d, digZ)), "landform dig approach"))
                yield break;

            Vector3 eye = terrain.ToLocal(new WorldPosition(digX, baseHeight + 3d, digZ));
            var ray = new Ray(eye, Vector3.down);
            bool hitBefore = Physics.Raycast(ray, out RaycastHit before, 7f);
            if (!Check(hitBefore && terrain.Pick(ray, eye, out var cell, out _) && terrain.TryEdit(cell, fill: false),
                "landform grid dig"))
                yield break;
            if (!Check(Physics.Raycast(ray, out RaycastHit after, 7f), "landform cut collision"))
                yield break;

            float drop = before.point.y - after.point.y;
            double cutY = terrain.ToWorld(after.point).Y;
            if (!Check(drop > 0.01f, "landform cut lowered ground"))
                yield break;

            var flyStart = new WorldPosition(landing.X, Math.Max(8d, originHeight + 15d), landing.Z);
            if (!Check(player.EnterAt(flyStart), "landform flight entry"))
                yield break;
            player.Look(90f, 15f);
            yield return Measure("landform terrain-following flight", 600, _ => FollowTerrain(Vector3.right, 0.03f));

            if (!Check(player.EnterAt(new WorldPosition(digX - 3d, baseHeight + 2d, digZ)), "landform return to edit"))
                yield break;
            eye = terrain.ToLocal(new WorldPosition(digX, baseHeight + 3d, digZ));
            if (!Check(Physics.Raycast(new Ray(eye, Vector3.down), out RaycastHit revisited, 7f)
                && Math.Abs(terrain.ToWorld(revisited.point).Y - cutY) < 0.003d, "landform edit after terrain streamed out"))
                yield break;
            Debug.Log(FormattableString.Invariant($"Landform check | walk {distanceWalked:F2} m | grid cut {drop:F3} m | retained after return"));

            if (region != null)
            {
                foreach (RiverReach reach in region.Rivers.Reaches)
                {
                    var position = (reach.Upstream.Center + reach.Downstream.Center) * 0.5f;
                    float level = region.WaterAt(position);
                    if (level < 12f || reach.Downstream.HalfWidth < 14f)
                        continue;
                    WorldPosition river = terrain.PlaceInRegion(position, level - 1.15f);
                    if (!terrain.TrySampleWaterColumn(river, out float surface, out float bottom) || surface - bottom < 1.5f)
                        continue;
                    if (!Check(player.EnterAt(river) && player.MoveMode == ExplorerMoveMode.Swim, "landform river swimming"))
                        yield break;
                    Debug.Log(FormattableString.Invariant($"Landform check | river depth {surface - bottom:F2} m | swim"));
                    var overlook = new WorldPosition(river.X - 138d, level + 130d, river.Z - 125d);
                    if (player.EnterAt(overlook))
                    {
                        player.Look(48f, 27f);
                        yield return SettleView();
                        CaptureIfRequested(target, "river");
                    }
                    break;
                }

                if (region.HasLake)
                {
                    WorldPosition lake = terrain.PlaceInRegion(region.Basin, region.LakeLevel - 1.15f);
                    if (!Check(player.EnterAt(lake) && player.MoveMode == ExplorerMoveMode.Swim, "landform basin lake swimming"))
                        yield break;
                    Debug.Log(FormattableString.Invariant($"Landform check | lake depth {region.LakeLevel - terrain.SurfaceHeight(lake):F2} m | swim"));
                    player.EnterAt(terrain.BasinEntry);
                    player.Look(38f, 29f);
                    yield return SettleView();
                    CaptureIfRequested(target, "lake");
                }

                if (!Check(player.EnterAt(terrain.MountainOverview), "landform overview"))
                    yield break;
                player.Look(45f, 32f);
                yield return SettleView();
                CaptureIfRequested(target, "overview");
            }

            Debug.Log("Landform check | passed");
            Application.Quit(0);
        }

        private IEnumerator SettleView()
        {
            for (int i = 0; i < 96; i++)
            {
                terrain.Stream(player.transform.position, Vector3.zero);
                player.UpdateView();
                Render();
                yield return null;
            }
        }
    }
}
#endif
