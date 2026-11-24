#if DEVELOPMENT_BUILD || REOL_TEST_BUILD
using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Reol2.World
{
    internal sealed partial class ContinentBenchmark
    {
        private IEnumerator CheckWaterAndCoast(RenderTexture target)
        {
            if (!Check(terrain.Landforms?.Size == 64000 && terrain.Seed == 20261121,
                "water and coast fixture requires -landforms 64 -world-seed 20261121"))
                yield break;

            player.Environment.Clock.Paused = true;
            const int arrivalFrames = 144;
            bool captureSettled = Array.IndexOf(Environment.GetCommandLineArgs(), "-world-water-coast-settle") >= 0;
            var shots = new (string Name, WorldPosition Feet, float Heading, float Pitch)[]
            {
                ("01-basin-inlets", new(-6095, 1407.8, -25709), -64f, 31f),
                ("02-lake-mouth", new(-6972, 650.6, -23862), -18f, 32f),
                ("03-inland-rivers", new(-10623, 2999.8, -16006), -21f, 55f),
                ("04-sea-mouths", new(-18975, 964.3, -11848), -77f, 40f),
                ("05-coast-high", new(-13548, 2706.9, 804), 0f, 77f),
                ("05-coast-low", new(-13548, 450, 804), 0f, 77f)
            };
            foreach (var shot in shots)
            {
                if (!Check(player.EnterAt(shot.Feet), shot.Name))
                    yield break;
                player.Look(shot.Heading, shot.Pitch);
                for (int i = 0; i < arrivalFrames; i++)
                {
                    terrain.Stream(player.transform.position, Vector3.zero);
                    Render();
                    yield return null;
                }
                CaptureIfRequested(target, shot.Name);
                ReportWaterAndCoastView(shot.Name, shot.Feet, arrivalFrames);
                if (captureSettled && (shot.Name == "01-basin-inlets" || shot.Name == "03-inland-rivers"))
                {
                    int wait = 0;
                    while (terrain.PendingHorizonTiles > 0 && wait < 1024)
                    {
                        terrain.Stream(player.transform.position, Vector3.zero);
                        Render();
                        wait++;
                        yield return null;
                    }
                    CaptureIfRequested(target, shot.Name + "-settled");
                    ReportWaterAndCoastView(shot.Name + "-settled", shot.Feet, arrivalFrames + wait);
                }
            }

            if (captureSettled)
            {
                Application.Quit(0);
                yield break;
            }

            float ascent = (2706.9f - 450f) / (480 * Step);
            var before = terrain.RegionalHorizonBuilds;
            yield return Measure("regional coast ascent", 480, _ => player.MoveWithVelocity(Vector3.up * ascent, Step));
            CaptureIfRequested(target, "05-coast-after-ascent");
            var after = terrain.RegionalHorizonBuilds;
            Debug.Log($"Water and coast | ascent published {after.Tiles - before.Tiles} water tiles | "
                + $"visited {after.Cells - before.Cells} cell candidates in published tiles | longest water tile {after.Longest} frames");

            if (!Check(player.EnterAt(new WorldPosition(-13548, 1800, 804)), "coast flight start"))
                yield break;
            player.Look(0f, 45f);
            yield return Measure("regional coast fast flight", 480, _ =>
                player.MoveWithVelocity(Vector3.forward * (18f * 64f), Step));
            CaptureIfRequested(target, "06-coast-after-flight");
            if (!Check(player.EnterRiverDemo(), "river valley view"))
                yield break;
            yield return SettleView();
            CaptureIfRequested(target, "07-river-valley");
            Debug.Log("Water and coast | passed");
            Application.Quit(0);
        }

        private void ReportWaterAndCoastView(string name, WorldPosition feet, int frames) =>
            Debug.Log($"Water and coast | {name} | {frames} frames | player tile level {terrain.HorizonLevelAt(feet)} | "
                + $"resident tiles {terrain.HorizonTiles} | pending tiles {terrain.PendingHorizonTiles} | "
                + $"visible terrain triangles {terrain.HorizonTriangles}");

        private IEnumerator CheckEstuaries(RenderTexture target)
        {
            if (!Check(terrain.Landforms?.Size == 64000 && terrain.Seed == 20261121,
                "estuary fixture requires -landforms 64 -world-seed 20261121"))
                yield break;

            player.Environment.Clock.Paused = true;
            var offshore = new WorldPosition(-502, 76.5, 11521);
            bool offshoreFirst = Array.IndexOf(Environment.GetCommandLineArgs(), "-world-estuary-offshore-first") >= 0;
            (string Name, WorldPosition Feet, float Heading, float Pitch)[] shots = offshoreFirst
                ? new[] { ("02-offshore-cold", offshore, -39.4f, 29.9f) }
                : new[]
                {
                    ("01-estuaries", new WorldPosition(-3238, 2934.8, 11757), 180f, 72f),
                    ("02-offshore", offshore, -39.4f, 29.9f),
                    ("03-mouth-bed", EstuaryDive(offshore), 0f, 5f)
                };
            foreach (var shot in shots)
            {
                if (!Check(player.EnterAt(shot.Feet), shot.Name))
                    yield break;
                player.Look(shot.Heading, shot.Pitch);
                yield return CaptureEstuaryView(target, shot.Name, shot.Feet);
            }

            if (offshoreFirst)
            {
                Application.Quit(0);
                yield break;
            }
            if (!Check(player.MoveMode == ExplorerMoveMode.Swim, "the estuary bed is submerged and swimmable"))
                yield break;

            if (!Check(player.EnterAt(offshore), "estuary flight start"))
                yield break;
            player.Look(270f, 45f);
            var before = terrain.RegionalHorizonBuilds;
            yield return Measure("estuary coast flight", 480, _ =>
                player.MoveWithVelocity(Vector3.left * 544f, Step));
            var after = terrain.RegionalHorizonBuilds;
            Debug.Log($"Estuary check | flight published {after.Tiles - before.Tiles} regional tiles | "
                + $"visited {after.Cells - before.Cells} cell candidates | remaining {terrain.PendingHorizonTiles}");
            Debug.Log("Estuary check | finished");
            Application.Quit(0);
        }

        private WorldPosition EstuaryDive(WorldPosition reference)
        {
            RegionalLandforms region = terrain.Landforms;
            WorldPosition home = terrain.WorldLanding;
            double centerX = home.X - region.Landing.X, centerZ = home.Z - region.Landing.Y;
            double nearest = double.PositiveInfinity;
            WorldPosition destination = reference;
            foreach (RiverReach reach in region.Rivers.Reaches)
            {
                if (reach.Downstream.Level != 0f || reach.Downstream.HalfWidth < 12f)
                    continue;
                double x = centerX + reach.Downstream.Center.X, z = centerZ + reach.Downstream.Center.Y;
                double distance = (x - reference.X) * (x - reference.X) + (z - reference.Z) * (z - reference.Z);
                var point = new WorldPosition(x, -1.15f, z);
                if (distance < nearest && terrain.TrySampleWaterColumn(point, out float water, out float ground)
                    && water - ground > 1.8f && MathF.Abs(water) < 0.06f)
                {
                    destination = point;
                    nearest = distance;
                }
            }
            return destination;
        }

        private IEnumerator CaptureEstuaryView(RenderTexture target, string name, WorldPosition feet)
        {
            for (int i = 1; i <= 768; i++)
            {
                terrain.Stream(player.transform.position, Vector3.zero);
                Render();
                if (name.StartsWith("02-offshore", StringComparison.Ordinal) && (i == 1 || i == 16))
                    CaptureIfRequested(target, $"{name}-frame-{i}");
                if (i == 144)
                {
                    CaptureIfRequested(target, name + "-arrival");
                    ReportWaterAndCoastView(name + "-arrival", feet, i);
                }
                if ((i >= 144 && terrain.PendingHorizonTiles == 0) || i == 768)
                {
                    CaptureIfRequested(target, name);
                    ReportWaterAndCoastView(name, feet, i);
                    break;
                }
                yield return null;
            }
            var sample = new WorldPosition(feet.X, -1f, feet.Z);
            terrain.TrySampleWaterColumn(sample, out float water, out _);
            float ground = terrain.SurfaceHeight(feet);
            bool wetsSample = terrain.IsWater(terrain.ToLocal(sample));
            Debug.Log($"Estuary check | {name} | bed {ground:F3} | water {water:F3} | wet at -1 m {wetsSample} | mode {player.MoveMode}");
        }

        private IEnumerator CheckOceanOrbit(RenderTexture target)
        {
            if (!Check(terrain.Landforms?.Size == 64000 && terrain.Seed == 20261121,
                "ocean orbit fixture requires -landforms 64 -world-seed 20261121"))
                yield break;

            const int scanWidth = 400, scanHeight = 225;
            var scan = new Texture2D(scanWidth, scanHeight, TextureFormat.RGBA32, false);
            var reduced = RenderTexture.GetTemporary(scanWidth, scanHeight);
            string[] args = Environment.GetCommandLineArgs();
            int reportFlag = Array.IndexOf(args, "-world-render-report-dir");
            var reports = reportFlag >= 0 && reportFlag + 1 < args.Length ? new WorldRenderReport(terrain, player, view) : null;
            try
            {
                var shots = new (string Name, WorldPosition Feet, int Hour)[]
                {
                    ("01-west-high", new(-16139, 1609.3, -2911), 14),
                    ("02-west-low", new(-16189, 546.3, -2589), 15),
                    ("03-north-near", new(-502, 76.5, 11521), 15)
                };
                foreach (var shot in shots)
                {
                    WorldClock clock = player.Environment.Clock;
                    clock.Paused = false;
                    clock.Advance(shot.Hour * WorldClock.Hour - clock.Tick);
                    clock.Paused = true;
                    if (!Check(player.EnterAt(shot.Feet), shot.Name))
                        yield break;
                    Debug.Log($"Ocean orbit | {shot.Name} | seabed {terrain.SurfaceHeight(shot.Feet):F1} m");

                    for (int orbit = 0; orbit < 2; orbit++)
                    {
                        int peak = 0, flagged = 0;
                        string phase = $"{shot.Name}-orbit-{orbit + 1}";
                        for (int frame = 0; frame < 216; frame++)
                        {
                            terrain.Stream(player.transform.position, Vector3.zero);
                            float heading = (frame % 72) * 5f;
                            float pitch = 29f + (frame / 72) * 25f;
                            player.Look(heading, pitch);
                            reports?.Record();
                            Render();
                            if (frame == 0 && orbit == 0)
                                yield return null;
                            int warm = CountWarmOceanSamples(target, reduced, scan, out WorldPosition sample);
                            if (warm > 24)
                                flagged++;
                            if (warm > Math.Max(24, peak))
                            {
                                if (peak <= 24)
                                    CaptureIfRequested(target, phase + "-first-warm");
                                Debug.Log($"Ocean orbit | {phase} | frame {frame} | yaw {heading:F0} pitch {pitch:F0} | "
                                    + $"warm {warm} | first sample ({sample.X:F1}, {sample.Z:F1}) | pending {terrain.PendingHorizonTiles}");
                            }
                            peak = Math.Max(peak, warm);
                            if (frame == 24 || frame == 126)
                                CaptureIfRequested(target, $"{phase}-frame-{frame}");
                            if (reports != null && frame == 24 && orbit == 0)
                            {
                                string path = WorldRenderReport.Save(reports.ReadReport(), EncodeFrame(target), args[reportFlag + 1]);
                                Debug.Log($"Ocean orbit | exported {phase} | {path}");
                            }
                            yield return null;
                        }
                        Debug.Log($"Ocean orbit | {phase} | 216 frames | flagged {flagged} | peak {peak} | "
                            + $"tiles {terrain.HorizonTiles} | pending {terrain.PendingHorizonTiles}");
                    }
                }

                if (!Check(player.EnterAt(shots[0].Feet), "ocean orbit flight start"))
                    yield break;
                var builtBefore = terrain.RegionalHorizonBuilds;
                yield return Measure("ocean orbit coast flight", 480, frame =>
                {
                    player.MoveWithVelocity(Vector3.back * (18f * 64f), Step);
                    player.Look(frame * 5f, 52f);
                }, viewUpdatedByDrive: true);
                var builtAfter = terrain.RegionalHorizonBuilds;
                Debug.Log($"Ocean orbit | flight published {builtAfter.Tiles - builtBefore.Tiles} regional tiles | "
                    + $"visited {builtAfter.Cells - builtBefore.Cells} cell candidates | pending {terrain.PendingHorizonTiles}");
            }
            finally
            {
                if (RenderTexture.active == reduced)
                    RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(reduced);
                Destroy(scan);
            }
            Application.Quit(0);
        }

        private IEnumerator CheckWaterTile(RenderTexture target, string[] args, int reportFlag)
        {
            if (!Check(reportFlag + 1 < args.Length && File.Exists(args[reportFlag + 1]), "water tile report JSON"))
                yield break;
            var report = JsonUtility.FromJson<WorldRenderReport.Report>(File.ReadAllText(args[reportFlag + 1]));
            if (!Check(report != null && terrain.Landforms is RegionalLandforms region && report.seed == terrain.Seed
                && report.regionKm == region.Size / 1000, "water tile report world"))
                yield break;

            var saved = report.current;
            var feet = new WorldPosition(saved.feet.x, saved.feet.y, saved.feet.z);
            var camera = new WorldPosition(saved.camera.x, saved.camera.y, saved.camera.z);
            if (Array.IndexOf(args, "-world-water-tile-warm") >= 0)
            {
                foreach (var stop in new WorldPosition[]
                {
                    new(feet.X + 18000, 900, feet.Z + 8000),
                    new(feet.X - 12000, 550, feet.Z),
                    new(feet.X + 2400, 455, feet.Z - 1800),
                    new(feet.X - 1300, 760, feet.Z + 800),
                })
                {
                    if (!player.EnterAt(new WorldPosition(stop.X, Math.Max(terrain.SurfaceHeight(stop) + 100d, stop.Y), stop.Z)))
                        continue;
                    player.Look(195f, 48f);
                    for (int i = 0; i < 650 && terrain.PendingHorizonTiles > 0; i++)
                    {
                        terrain.Stream(player.transform.position, Vector3.zero);
                        if (i % 3 == 0)
                            Render();
                        yield return null;
                    }
                }
            }
            WorldClock clock = player.Environment.Clock;
            clock.Paused = false;
            clock.Advance(saved.clockTick - clock.Tick);
            clock.Paused = true;
            Vector3 angles = saved.cameraRotation.eulerAngles;
            bool arrived = saved.mode == ExplorerMoveMode.Flight
                ? player.EnterFlight(feet, angles.y, angles.x)
                : player.EnterAt(feet);
            if (!Check(arrived && player.MoveMode == saved.mode, "water tile report position and movement mode"))
                yield break;
            if (saved.mode == ExplorerMoveMode.Flight)
                player.MoveForFrame(Vector2.zero, 0f, fast: false, flight: true, dt: Step);
            terrain.CenterWorld(new WorldPosition(saved.origin.x, saved.origin.y, saved.origin.z));
            player.transform.position = terrain.ToLocal(feet);
            player.UpdateView();
            view.transform.SetPositionAndRotation(terrain.ToLocal(camera), saved.cameraRotation);
            view.fieldOfView = report.cameraFov;
            view.nearClipPlane = report.cameraNear;
            view.farClipPlane = terrain.ViewDistanceOverride > 0f ? terrain.ViewDistanceOverride * 1.07f : report.cameraFar;
            view.aspect = report.cameraAspect;
            view.rect = report.cameraViewport;
            Debug.Log($"Water tile | replay mode {player.MoveMode} | origin {terrain.FrameOrigin}");

            int frameLimit = Array.IndexOf(args, "-world-water-tile-settle") >= 0 ? 2500 : 1000;
            var beforeBuild = terrain.RegionalHorizonBuilds;
            for (int i = 1; i <= frameLimit; i++)
            {
                terrain.Stream(player.transform.position, Vector3.zero);
                Render();
                if (i == 16 || i == 144)
                    CaptureIfRequested(target, $"frame-{i}");
                if (terrain.PendingHorizonTiles == 0 || i == frameLimit)
                {
                    var build = terrain.RegionalHorizonBuilds;
                    Debug.Log($"Water tile | frames {i} | {terrain.PendingHorizonTiles} pending | "
                        + $"{build.Tiles - beforeBuild.Tiles} regional tiles and {build.Cells - beforeBuild.Cells} cell visits");
                    break;
                }
                yield return null;
            }
            CaptureIfRequested(target, "settled");
            for (int i = -16; i <= 16; i++)
            {
                view.transform.rotation = Quaternion.Euler(0f, i * 0.125f, 0f) * saved.cameraRotation;
                Render();
                CaptureIfRequested(target, $"pan-{i + 16:00}");
                yield return null;
            }
            for (int i = -16; i <= 16; i++)
            {
                view.transform.rotation = saved.cameraRotation * Quaternion.Euler(i * 0.125f, 0f, 0f);
                Render();
                CaptureIfRequested(target, $"tilt-{i + 16:00}");
                yield return null;
            }
            view.transform.rotation = saved.cameraRotation;
            Render();
            CaptureIfRequested(target, "after-pan");

            terrain.CenterWorld(new WorldPosition(saved.origin.x + 512d, saved.origin.y - 512d, saved.origin.z - 512d));
            player.transform.position = terrain.ToLocal(feet);
            view.transform.position = terrain.ToLocal(camera);
            Render();
            CaptureIfRequested(target, "rebased");
            Debug.Log($"Water tile | replay after rebase | origin {terrain.FrameOrigin}");
            terrain.CenterWorld(new WorldPosition(saved.origin.x, saved.origin.y, saved.origin.z));
            player.transform.position = terrain.ToLocal(feet);
            view.transform.position = terrain.ToLocal(camera);

            if (Array.IndexOf(args, "-world-water-tile-screen") >= 0)
            {
                view.enabled = true;
                for (int i = -16; i <= 16; i += 2)
                {
                    view.transform.rotation = Quaternion.Euler(0f, i * 0.125f, 0f) * saved.cameraRotation;
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    Texture2D screen = ScreenCapture.CaptureScreenshotAsTexture();
                    int screenImage = Array.IndexOf(args, "-continent-image");
                    if (screenImage >= 0 && screenImage + 1 < args.Length)
                        File.WriteAllBytes(Path.ChangeExtension(args[screenImage + 1], null) + $"-screen-pan-{i + 16:00}.png",
                            screen.EncodeToPNG());
                    Destroy(screen);
                }
                view.enabled = false;
                view.transform.rotation = saved.cameraRotation;
            }
            if (Array.IndexOf(args, "-world-water-tile-present") >= 0)
            {
                view.enabled = true;
                yield return null;
                Debug.Log($"Presentation check | ready | {Screen.fullScreenMode} | {Screen.width}x{Screen.height} "
                    + $"| {Screen.currentResolution.refreshRateRatio.value:F3} Hz | focused {Application.isFocused}");
                double end = Time.realtimeSinceStartupAsDouble + 12d;
                while (Time.realtimeSinceStartupAsDouble < end)
                    yield return null;
                view.enabled = false;
            }
            var state = new WorldRenderReport(terrain, player, view).ReadReport();
            string imageFlag = "-continent-image";
            int output = Array.IndexOf(args, imageFlag);
            if (output >= 0 && output + 1 < args.Length)
                File.WriteAllText(Path.ChangeExtension(args[output + 1], ".json"), JsonUtility.ToJson(state, true));

            Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(view);
            foreach (MeshRenderer renderer in terrain.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled || renderer.name != "Water surface")
                    continue;
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                if (mesh.vertexCount == 0)
                    continue;
                var origin = terrain.ToWorld(renderer.transform.position);
                if (Math.Abs(origin.X - feet.X) > 5000d || Math.Abs(origin.Z - feet.Z) > 5000d)
                    continue;
                Debug.Log($"Water tile | ({origin.X:F0},{origin.Y:F0},{origin.Z:F0}) | vertices {mesh.vertexCount} | "
                    + $"local bounds {mesh.bounds} | renderer bounds {renderer.bounds} | frustum {GeometryUtility.TestPlanesAABB(frustum, renderer.bounds)}");
            }

            Application.Quit(0);
        }

        private int CountWarmOceanSamples(RenderTexture target, RenderTexture reduced, Texture2D scan, out WorldPosition first)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(target, reduced);
                RenderTexture.active = reduced;
                scan.ReadPixels(new Rect(0, 0, scan.width, scan.height), 0, 0, recalculateMipMaps: false);
            }
            finally
            {
                RenderTexture.active = previous;
            }

            int count = 0;
            first = default;
            var colors = scan.GetRawTextureData<Color32>();
            float sea = terrain.ToLocal(new WorldPosition(0, 0, 0)).y;
            for (int y = 4; y < scan.height - 4; y += 2)
            for (int x = 4; x < scan.width - 4; x += 2)
            {
                if (Mathf.Abs(x - scan.width / 2) < 20 && Mathf.Abs(y - scan.height / 2) < 36)
                    continue;
                Color32 pixel = colors[x + y * scan.width];
                if (pixel.r < 110 || pixel.g - pixel.r > 23 || pixel.g - pixel.b < 5 || pixel.r < pixel.b)
                    continue;
                Ray sight = view.ViewportPointToRay(new Vector3((x + 0.5f) / scan.width, (y + 0.5f) / scan.height));
                if (sight.direction.y >= -0.1f)
                    continue;
                WorldPosition at = terrain.ToWorld(sight.GetPoint((sea - sight.origin.y) / sight.direction.y));
                if (terrain.SurfaceHeight(at) > -1f)
                    continue;
                if (count++ == 0)
                    first = at;
            }
            return count;
        }
    }
}
#endif
