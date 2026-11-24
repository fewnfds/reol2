#if DEVELOPMENT_BUILD || REOL_TEST_BUILD
using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Reol2.World
{
    internal sealed partial class ContinentBenchmark
    {
        [Serializable]
        private sealed class BakeRoute
        {
            public int seamX, crossingZ, editZ, riverX, riverZ;

            public BakeRoute() { }
        }

        private void CaptureSurfaceBake(string folder)
        {
            if (terrain.Landforms?.Size != 64000 || terrain.Seed != 20261121)
                throw new ArgumentException("The reference bake uses the default 64 km region.");
            const int tile = 4096;
            int step = TiledSurfaceData.Step, side = tile / step + 1;
            WorldPosition spawn = terrain.WorldLanding;
            WorldPosition sea = terrain.OceanEntry;
            var manifest = new TiledSurfaceData.Manifest
            {
                seed = terrain.Seed,
                regionalFallback = true,
                west = -8192,
                south = -38912,
                tileSize = tile,
                eastCount = 2,
                northCount = 1,
                spawnX = spawn.X,
                spawnZ = spawn.Z,
                seaX = sea.X,
                seaZ = sea.Z
            };
            var source = new WorldSurface(new WorldDefinition(terrain.Seed), terrain.Landforms);
            Directory.CreateDirectory(folder);
            var clock = Stopwatch.StartNew();
            var ground = new float[side * side];
            var water = new float[ground.Length];
            var bytes = new byte[ground.Length * sizeof(float)];
            var tags = new byte[ground.Length];
            for (int column = 0; column < 2; column++)
            {
                for (int z = 0; z < side; z++)
                for (int x = 0; x < side; x++)
                {
                    int i = z * side + x;
                    int wx = manifest.west + column * tile + x * step;
                    int wz = manifest.south + z * step;
                    ground[i] = source.Node(wx, wz);
                    water[i] = source.RegionalWaterNode(wx, wz, ground[i]);
                    tags[i] = water[i] > ground[i] ? (byte)(water[i] == 0f ? 1 : 2) : (byte)0;
                }
                using var output = new GZipStream(File.Create(TiledSurfaceData.TilePath(folder, column, 0)), System.IO.Compression.CompressionLevel.Fastest);
                Buffer.BlockCopy(ground, 0, bytes, 0, bytes.Length);
                output.Write(bytes, 0, bytes.Length);
                Buffer.BlockCopy(water, 0, bytes, 0, bytes.Length);
                output.Write(bytes, 0, bytes.Length);
                output.Write(tags, 0, tags.Length);
            }
            File.WriteAllText(Path.Combine(folder, TiledSurfaceData.ManifestName), JsonUtility.ToJson(manifest, true));
            var route = new BakeRoute { seamX = -4096, crossingZ = -36072, editZ = -36008, riverX = -4096, riverZ = -36072 };
            File.WriteAllText(Path.Combine(folder, "route.json"), JsonUtility.ToJson(route, true));
            Debug.Log(FormattableString.Invariant($"Surface bake export | 2 tiles | {2 * ground.Length} stored nodes | {clock.Elapsed.TotalMilliseconds:F2} ms"));
        }

        private IEnumerator CheckSurfaceBake(RenderTexture target, string routeFile)
        {
            var route = JsonUtility.FromJson<BakeRoute>(File.ReadAllText(routeFile));
            if (!Check(route != null, "read surface route"))
                yield break;
            player.Environment.Clock.Paused = true;
            var riverPoint = new WorldPosition(route.riverX, 0d, route.riverZ);
            if (!Check(terrain.TryGetWaterColumn(riverPoint, out float water, out float bed) && water - bed >= 0.95f,
                "sample swimmable river across the seam"))
                yield break;
            float dryApproach = terrain.SurfaceHeight(new WorldPosition(route.riverX, 0d, route.riverZ - 64));
            var overlook = new WorldPosition(route.riverX, Math.Max(water, dryApproach) + 55d, route.riverZ - 64);
            if (!Check(player.EnterFlight(overlook, 0f, 46f), "crossing overlook"))
                yield break;
            yield return SettleSurfaceBake(target, "01-river-seam");

            var swim = new WorldPosition(riverPoint.X, water - 0.77f, riverPoint.Z);
            var clock = Stopwatch.StartNew();
            if (!Check(player.EnterAt(swim) && player.MoveMode == ExplorerMoveMode.Swim, "river swim"))
                yield break;
            Debug.Log(FormattableString.Invariant($"Surface bake | river entry {clock.Elapsed.TotalMilliseconds:F2} ms | water {water:F3} | bed {bed:F3} | depth {water - bed:F3}"));
            for (int i = 0; i < 45; i++)
            {
                player.MoveForFrame(Vector2.zero, 0f, false, false, Step);
                Render();
                yield return null;
            }

            const int halfRoute = 1152;
            float altitude = water + 80f;
            for (int x = route.seamX - halfRoute; x <= route.seamX + halfRoute; x += 8)
                altitude = Math.Max(altitude, terrain.SurfaceHeight(new WorldPosition(x, 0d, route.crossingZ)) + 80f);
            var flight = new WorldPosition(route.seamX - halfRoute, altitude, route.crossingZ);
            if (!Check(player.EnterFlight(flight, 90f, 55f), "flight start"))
                yield break;
            // Warm player, renderer, and current location before sampling the route.
            for (int i = 0; i < 90; i++)
            {
                terrain.Stream(player.transform.position, Vector3.zero);
                Render();
                yield return null;
            }
            yield return Measure("surface bake flight", 480, _ => player.MoveWithVelocity(Vector3.right * (18f * 16f), Step));
            if (!Check(player.WorldFeet.X > route.seamX + 1000, "flight crossed the seam"))
                yield break;

            if (!Check(TryFindRoad(route, out WorldPosition start, out WorldPosition end), "dry road across the seam"))
                yield break;
            const float lowFlightSpeed = 36f;
            double lowStart = route.seamX - 108d;
            float lowAltitude = LowFlightHeight(lowStart, start.Z);
            if (!Check(player.EnterFlight(new WorldPosition(lowStart, lowAltitude, start.Z), 90f, 18f), "near-ground flight start"))
                yield break;
            for (int i = 0; i < 60; i++)
            {
                terrain.Stream(player.transform.position, Vector3.zero);
                Render();
                yield return null;
            }
            long evaluated = terrain.EvaluatedDensitySamples;
            yield return Measure("surface bake low flight", 360, _ =>
            {
                var point = player.WorldFeet;
                float lift = (float)((LowFlightHeight(point.X + lowFlightSpeed * Step, point.Z) - point.Y) / Step);
                player.MoveWithVelocity(new Vector3(lowFlightSpeed, lift, 0f), Step);
            });
            if (!Check(player.WorldFeet.X > route.seamX + 70d && terrain.EvaluatedDensitySamples > evaluated,
                "near-ground flight across built collision pages"))
                yield break;

            var landing = new WorldPosition(start.X + 1d, start.Y + 0.22, start.Z);
            var awayPoint = new WorldPosition(landing.X + 20000, 3000, landing.Z);
            if (!Check(player.EnterFlight(awayPoint, 90f, 40f), "leave before a cold collision entry"))
                yield break;
            clock.Restart();
            if (!Check(player.EnterAt(landing), "distant dry landing"))
                yield break;
            Debug.Log(FormattableString.Invariant($"Surface bake | dry entry {clock.Elapsed.TotalMilliseconds:F2} ms | ground ready | {landing.X:F2}, {landing.Y:F2}, {landing.Z:F2}"));
            if (!Check(terrain.TryGrade(start, end), "smooth approach across two tiles"))
                yield break;
            Debug.Log($"Surface bake | grade published {terrain.LastEditPublishedPages} resident pages");
            if (!Check(PlaceTestGate(route.seamX + 2, start, end), "place the test gate"))
                yield break;
            player.Look(90f, 5f);
            for (int i = 0; i < 150; i++)
            {
                if (player.WorldFeet.X > route.seamX + 3.25)
                    break;
                player.MoveForFrame(Vector2.up, 0f, false, false, Step);
                Render();
                yield return null;
            }
            if (!Check(player.WorldFeet.X > route.seamX + 3.25 && player.MoveMode == ExplorerMoveMode.Land,
                "walk across seam and through the gate"))
                yield break;
            CaptureIfRequested(target, "02-gate-approach");

            var edit = new WorldPosition(route.seamX + 0.07, (start.Y + end.Y) * 0.5, start.Z);
            if (!Check(ReadGround(edit, out double before), "original ground collider"))
                yield break;
            edit = new WorldPosition(edit.X, before - 0.08d, edit.Z);
            if (!Check(terrain.TryEditSphere(edit, false) && ReadGround(edit, out double after)
                && before - after > 0.22d, "excavate the seam through the terrain collider"))
                yield break;
            ReadGround(edit, out double cutHeight);
            int editPages = terrain.LastEditPublishedPages;
            if (!Check(editPages >= 2 && player.EnterFlight(awayPoint, 90f, 40f), "edit two pages and leave the active region"))
                yield break;
            var blocked = new WorldPosition(landing.X, start.Y - 4d, start.Z);
            WorldPosition away = player.WorldFeet;
            if (!Check(!player.EnterAt(blocked) && player.WorldFeet.X == away.X, "reject an obstructed arrival"))
                yield break;
            clock.Restart();
            if (!Check(player.EnterAt(landing) && ReadGround(edit, out double restored) && Math.Abs(restored - cutHeight) < 0.035d,
                "edited collider after unloading and returning"))
                yield break;
            Debug.Log(FormattableString.Invariant($"Surface bake | return {clock.Elapsed.TotalMilliseconds:F2} ms | excavation {before - cutHeight:F3} m | edited pages republished {editPages}"));
            if (!Check(terrain.TryEditSphere(edit, true) && ReadGround(edit, out double filled) && filled - cutHeight > 0.22d,
                "fill at the imported seam"))
                yield break;

            Debug.Log("Surface bake | passed");
            Application.Quit(0);
        }

        private IEnumerator SettleSurfaceBake(RenderTexture target, string suffix)
        {
            for (int i = 0; i < 180 || (terrain.PendingHorizonTiles != 0 && i < 510); i++)
            {
                terrain.Stream(player.transform.position, Vector3.zero);
                Render();
                yield return null;
            }
            CaptureIfRequested(target, suffix);
            Debug.Log($"Surface bake | {suffix} | pending horizon tiles {terrain.PendingHorizonTiles} | visible triangles {terrain.HorizonTriangles}");
        }

        private bool TryFindRoad(BakeRoute route, out WorldPosition start, out WorldPosition end)
        {
            for (int i = 0; i < 120; i++)
            {
                double z = route.editZ + 0.5 + ((i % 2 == 0 ? i : -i) * 2);
                double x = route.seamX;
                var left = new WorldPosition(x - 5, 0, z);
                var right = new WorldPosition(x + 5, 0, z);
                float a = terrain.SurfaceHeight(left), b = terrain.SurfaceHeight(right);
                if (Math.Abs(a - b) > 1.2f || a < 2f || b < 2f)
                    continue;
                bool dry = true;
                for (int metres = -5; metres <= 5; metres++)
                for (int dz = -2; dz <= 2; dz++)
                {
                    var at = new WorldPosition(x + metres, 0, z + dz);
                    float h = terrain.SurfaceHeight(at);
                    dry &= !terrain.TryGetWaterColumn(at, out _, out _)
                        && Math.Abs(h - Mathf.Lerp(a, b, (metres + 5f) / 10f)) < 0.38f;
                }
                if (dry)
                {
                    start = new WorldPosition(left.X, a + 0.14d, z);
                    end = new WorldPosition(right.X, b + 0.14d, z);
                    return true;
                }
            }
            start = end = default;
            return false;
        }

        private float LowFlightHeight(double x, double z)
        {
            float here = terrain.SurfaceHeight(new WorldPosition(x, 0, z));
            float ahead = terrain.SurfaceHeight(new WorldPosition(x + 3, 0, z));
            return Math.Max(terrain.TryGetWaterColumn(new WorldPosition(x, 0, z), out float water, out _) ? water : here,
                Math.Max(here, ahead)) + 5f;
        }

        private bool PlaceTestGate(int x, WorldPosition start, WorldPosition end)
        {
            int y = (int)Math.Floor(Math.Max(start.Y, end.Y));
            int z = (int)Math.Floor(start.Z);
            for (int row = y - 1; row <= y + 3; row++)
            {
                terrain.TryEdit(new Vector3Int(x, row, z - 2), true);
                terrain.TryEdit(new Vector3Int(x, row, z + 2), true);
            }
            for (int across = z - 1; across <= z + 1; across++)
                if (!terrain.TryEdit(new Vector3Int(x, y + 3, across), true))
                    return false;
            return true;
        }

        private bool ReadGround(WorldPosition position, out double height)
        {
            Vector3 start = terrain.ToLocal(new WorldPosition(position.X, position.Y + 3.5, position.Z));
            Physics.SyncTransforms();
            if (Physics.Raycast(start, Vector3.down, out RaycastHit hit, 10, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore) && hit.collider.transform.IsChildOf(terrain.transform))
            {
                height = terrain.ToWorld(hit.point).Y;
                return true;
            }
            height = 0d;
            return false;
        }
    }
}
#endif
