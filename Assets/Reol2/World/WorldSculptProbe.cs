#if DEVELOPMENT_BUILD || REOL_TEST_BUILD
using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Reol2.World
{
    internal static class WorldSculptProbe
    {
        private const int RunLength = 20;

        internal static void CaptureCrater(ContinentTerrain terrain, ContinentExplorer player, Camera view,
                                           Action render, Action<string> capture)
        {
            player.Environment.Clock.Paused = true;
            player.Environment.PreviewWeather = new WeatherSample(0.12f, 0, 0, 0, 0);
            const double x = 7536, z = -399848;
            double naturalHeight = terrain.SurfaceHeight(new WorldPosition(x, 0, z));
            double height = Math.Round(naturalHeight * 2) * 0.5 + 0.0005;
            Require(player.EnterAt(new WorldPosition(x, height + 1, z - 2.45)), "crater arrival");
            for (int row = -1; row <= 1; row++)
            {
                double laneZ = z + row * 1.5;
                terrain.TryGrade(new WorldPosition(x - 4, height, laneZ), new WorldPosition(x + 4, height, laneZ));
            }
            foreach (Vector2 offset in new[] { Vector2.left, Vector2.right, Vector2.down, Vector2.up })
                Require(terrain.TryEditSphere(new WorldPosition(x + 0.66 * offset.x, height - 0.20, z + 0.66 * offset.y), false), "crater stroke");

            double center = ReadGroundHeight(terrain, x, z, height);
            double side = ReadGroundHeight(terrain, x + 0.10, z, height);
            Debug.Log(FormattableString.Invariant($"World sculpt | crater | center rise over 10 cm {center - side:F5} m | center {center:F5} m | side {side:F5} m"));
            Require(center - side < 0.13, "crater retains a collision spur");
            player.UpdateView();
            Vector3 aim = terrain.ToLocal(new WorldPosition(x, height - 0.30, z));
            Vector3 camera = aim + new Vector3(1.55f, 2.4f, -4.7f);
            view.transform.SetPositionAndRotation(camera, Quaternion.LookRotation(aim - camera));
            render();
            capture("crater");
            Light sun = RenderSettings.sun;
            LightShadows shadows = sun.shadows;
            try
            {
                sun.shadows = LightShadows.Hard;
                render();
                capture("crater-hard-shadows");
                sun.shadows = LightShadows.None;
                render();
                capture("crater-no-shadows");
            }
            finally
            {
                sun.shadows = shadows;
            }

            int detailId = Shader.PropertyToID("_ReolGroundOrigin");
            Vector4 detail = Shader.GetGlobalVector(detailId);
            try
            {
                Shader.SetGlobalVector(detailId, new Vector4(detail.x, detail.y, detail.z, 0));
                render();
                capture("crater-no-ground-tone");
            }
            finally
            {
                Shader.SetGlobalVector(detailId, detail);
            }
        }

        private static double ReadGroundHeight(ContinentTerrain terrain, double x, double z, double height)
        {
            Vector3 origin = terrain.ToLocal(new WorldPosition(x, height + 2, z));
            Require(Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 5f) && hit.collider is MeshCollider,
                    "missing ground collision");
            return terrain.ToWorld(hit.point).Y;
        }

        internal static IEnumerator Run(ContinentTerrain terrain, ContinentExplorer player, Camera view,
                                        Action render, Action<string> capture)
        {
            bool grid = Array.IndexOf(Environment.GetCommandLineArgs(), "-sculpt-grid") >= 0;
            IEnumerator probe = Compare(terrain, player, view, render, capture, grid);
            while (true)
            {
                bool next;
                try
                {
                    next = probe.MoveNext();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    Application.Quit(1);
                    yield break;
                }
                if (!next)
                    break;
                yield return probe.Current;
            }
            Debug.Log($"World sculpt | PASS | {(grid ? "grid" : "continuous")} | seed {terrain.Seed}");
            Application.Quit(0);
        }

        private static IEnumerator Compare(ContinentTerrain terrain, ContinentExplorer player, Camera view,
                                            Action render, Action<string> capture, bool grid)
        {
            player.Environment.Clock.Paused = true;
            player.Environment.PreviewWeather = new WeatherSample(0.12f, 0, 0, 0, 0);
            WorldPosition junction = terrain.CavePassage[3];
            var cell = new Vector3Int((int)Math.Round(junction.X / 8) * 8, (int)Math.Round(junction.Y / 8) * 8,
                                      (int)Math.Round(junction.Z / 8) * 8);
            var center = new WorldPosition(cell.x + 0.5, cell.y + 0.5, cell.z + 0.5);
            Require(player.EnterAt(new WorldPosition(junction.X + 5, junction.Y + 4, junction.Z + 4)), "cave arrival");
            terrain.PrepareEntry(terrain.ToLocal(center));
            Vector3 ray = terrain.ToLocal(center) + Vector3.up * 1.75f;
            Require(!Physics.Raycast(ray, Vector3.down, 2.5f), "cave starts empty");
            const int count = 100;
            int initialSamples = terrain.EditedSamples;
            int totalPages = 0, peakPages = 0, peakPending = terrain.PendingPages;
            double cold = 0;
            var hot = new double[count];
            var frames = new double[count];
            for (int i = 0; i <= count; i++)
            {
                bool fill = (i & 1) == 0;
                long tick = Stopwatch.GetTimestamp();
                bool changed = grid ? terrain.TryEdit(cell, fill) : terrain.TryEditSphere(center, fill);
                double elapsed = Milliseconds(tick);
                if (i == 0)
                    cold = elapsed;
                else
                    hot[i - 1] = elapsed;
                Require(changed, "stroke must change the field");
                totalPages += terrain.LastEditPublishedPages;
                peakPages = Math.Max(peakPages, terrain.LastEditPublishedPages);
                peakPending = Math.Max(peakPending, terrain.PendingPages);
                bool supported = Physics.Raycast(ray, Vector3.down, out RaycastHit hit, 2.5f);
                Require(supported == fill, "collision must change in the same stroke");
                if (supported)
                    Require(hit.collider is MeshCollider mesh && mesh.sharedMesh == mesh.GetComponent<MeshFilter>().sharedMesh,
                            "render and collision meshes must match");
                render();
                yield return null;
                if (i > 0)
                    frames[i - 1] = Time.unscaledDeltaTime * 1000d;
            }
            Array.Sort(hot);
            Array.Sort(frames);
            string mode = grid ? "grid" : "continuous";
            Debug.Log(FormattableString.Invariant($"World sculpt | {mode} | cold edit {cold:F3} ms | cached 100 p50 {hot[49]:F3} p95 {hot[94]:F3} p99 {hot[98]:F3} max {hot[99]:F3} ms | cached rendered frame p95 {frames[94]:F3} p99 {frames[98]:F3} max {frames[99]:F3} ms | unique samples {terrain.EditedSamples - initialSamples} | published {totalPages} pages, peak {peakPages} per edit | peak queue {peakPending}"));

            Vector3Int origin = FindCourse(terrain);
            var start = new WorldPosition(origin.x, origin.y, origin.z);
            var flatEnd = new WorldPosition(origin.x + RunLength, origin.y, origin.z);
            var end = new WorldPosition(flatEnd.X, flatEnd.Y + 1, flatEnd.Z);
            var middle = new WorldPosition(origin.x + RunLength * 0.5, origin.y + 1, origin.z);
            Require(player.EnterAt(new WorldPosition(origin.x - 4, origin.y + 3, origin.z + 4)), "road arrival");
            for (int i = 0; i < 60; i++)
            {
                terrain.Stream(terrain.ToLocal(middle), Vector3.zero);
                render();
                yield return null;
            }
            terrain.PrepareEntry(terrain.ToLocal(start));
            terrain.PrepareEntry(terrain.ToLocal(flatEnd));
            // Both processes start with the same narrow, flat strip. No geometry outside it is levelled.
            terrain.TryGrade(start, flatEnd);
            var positions = new Vector3[RunLength * 8 + 1];
            ReadProfile(terrain, origin, 0, positions, out double initialError, out _);
            Require(initialError < 0.03, "common starting strip must be level before the comparison");

            player.UpdateView();
            Vector3 focus = terrain.ToLocal(new WorldPosition(middle.X, origin.y + 0.28, middle.Z));
            Vector3 cameraPosition = focus + new Vector3(-2.5f, 4.6f, -17.5f);
            view.transform.SetPositionAndRotation(cameraPosition, Quaternion.LookRotation(focus - cameraPosition));
            render();
            capture("before");

            int strokes = 0;
            long routeTick = Stopwatch.GetTimestamp();
            if (grid)
            {
                // Nearest whole metre to the 5% design: the height rounds up at its midpoint.
                for (int x = RunLength / 2; x <= RunLength; x++)
                for (int z = -1; z <= 0; z++)
                    if (terrain.TryEdit(new Vector3Int(origin.x + x, origin.y, origin.z + z), true))
                        strokes++;
            }
            else if (terrain.TryGrade(start, end))
                strokes++;
            double execute = Milliseconds(routeTick);
            Require(strokes > 0, "route edit");
            ReadProfile(terrain, origin, 1, positions, out double error, out double largestRise);
            Require(grid ? error > 0.25 && largestRise > 0.35 : error < 0.03 && largestRise < 0.055,
                    "published route did not reproduce the expected stepped or graded shape");
            render();
            capture("after");
            Debug.Log(FormattableString.Invariant($"World sculpt | {mode} | grade from ({origin.x},{origin.y},{origin.z}) | horizontal {RunLength} m rise 1 m | accepted operations {strokes} total edit {execute:F3} ms | original profile error {initialError:F5} m | post profile max error {error:F5} m | largest half-metre rise {largestRise:F5} m | 161 collision rays"));

            // Extra annotated frame uses actual ray hits. The before/after frames retain the raw terrain.
            var outline = UnityEngine.Object.FindAnyObjectByType<LineRenderer>(FindObjectsInactive.Include);
            if (outline != null)
            {
                var line = new GameObject("Measured road profile").AddComponent<LineRenderer>();
                line.sharedMaterial = outline.sharedMaterial;
                line.useWorldSpace = true;
                line.widthMultiplier = 0.05f;
                line.positionCount = positions.Length;
                for (int i = 0; i < positions.Length; i++)
                    positions[i] += 0.04f * Vector3.up;
                line.SetPositions(positions);
                render();
                capture("profile");
                line.enabled = false;
                UnityEngine.Object.Destroy(line.gameObject);
            }

            if (!grid)
                CheckGridCuts(terrain, view, render, capture, origin + Vector3Int.right * 8);
        }

        private static void CheckGridCuts(ContinentTerrain terrain, Camera view, Action render, Action<string> capture,
                                          Vector3Int corner)
        {
            Vector3 aim = terrain.ToLocal(new WorldPosition(corner.x, corner.y + 0.20, corner.z));
            Vector3 eye = aim + new Vector3(-2.8f, 4.3f, -4f);
            view.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
            render();
            capture("cut-before");

            Vector3Int first = corner - new Vector3Int(1, 0, 1);
            Require(terrain.TryEdit(first, fill: false), "single grid cut through grade");
            render();
            capture("cut-single");

            Require(terrain.TryEdit(first + Vector3Int.right, fill: false), "east grid cut");
            Require(terrain.TryEdit(first + Vector3Int.forward, fill: false), "north grid cut");
            Require(terrain.TryEdit(corner, fill: false), "northeast grid cut");
            double low = double.PositiveInfinity, high = double.NegativeInfinity;
            for (int z = -4; z <= 4; z++)
            for (int x = -4; x <= 4; x++)
            {
                double floor = ReadGroundHeight(terrain, corner.x + x / 8d, corner.z + z / 8d, corner.y + 1);
                low = Math.Min(low, floor);
                high = Math.Max(high, floor);
            }
            Require(high - low < 0.005 && Math.Abs(low - (corner.y - 0.055)) < 0.01, "grid cuts share one floor");
            render();
            capture("cut-joined");

            Require(terrain.TryEdit(first, fill: true), "refill first cell");
            double raised = ReadGroundHeight(terrain, first.x + 0.5, first.z + 0.5, first.y + 1);
            Require(raised > high + 0.8, "refill restores collision");
            render();
            capture("cut-refilled");
            Debug.Log(FormattableString.Invariant($"World sculpt | 4 grid cuts | 81 floor rays {low:F5}..{high:F5} m | refilled {raised:F5} m"));
        }

        private static Vector3Int FindCourse(ContinentTerrain terrain)
        {
            Vector3Int landing = terrain.WorldLanding.Cell;
            for (int attempt = 0; attempt < 36; attempt++)
            {
                int x0 = landing.x + 32 + (attempt % 6) * 8, z0 = landing.z + 40 + (attempt / 6) * 8;
                double low = double.PositiveInfinity, high = double.NegativeInfinity;
                for (int x = -4; x <= RunLength * 2 + 4; x++)
                for (int z = -4; z <= 4; z++)
                {
                    float h = terrain.SurfaceHeight(new WorldPosition(x0 + x * 0.5, 0, z0 + z * 0.5));
                    low = Math.Min(low, h);
                    high = Math.Max(high, h);
                }
                int level = (int)Math.Floor(low + 0.16);
                if (level >= 2 && high - level < 1.17)
                    return new Vector3Int(x0, level, z0);
            }
            throw new InvalidOperationException("World sculpt | no levelable above-water natural strip in the fixture area.");
        }

        private static void ReadProfile(ContinentTerrain terrain, Vector3Int origin, int rise, Vector3[] positions,
                                        out double maxError, out double largestRise)
        {
            maxError = 0;
            largestRise = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                double x = origin.x + i / 8d;
                Vector3 ray = terrain.ToLocal(new WorldPosition(x, origin.y + 4, origin.z));
                Require(Physics.Raycast(ray, Vector3.down, out RaycastHit hit, 8f) && hit.collider is MeshCollider,
                        "missing terrain collision beneath road profile");
                positions[i] = hit.point;
                maxError = Math.Max(maxError, Math.Abs(terrain.ToWorld(hit.point).Y - (origin.y + rise * i / (8d * RunLength))));
                if (i >= 4)
                    largestRise = Math.Max(largestRise, positions[i].y - positions[i - 4].y);
            }
        }

        private static void Require(bool condition, string stage)
        {
            if (!condition)
                throw new InvalidOperationException("World sculpt | " + stage);
        }

        private static double Milliseconds(long start) =>
            (Stopwatch.GetTimestamp() - start) * (1000d / Stopwatch.Frequency);
    }
}
#endif
