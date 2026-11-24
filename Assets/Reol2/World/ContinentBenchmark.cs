#if DEVELOPMENT_BUILD || REOL_TEST_BUILD
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Profiling;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Stage = Reol2.World.ContinentStreamProbe.Stage;

namespace Reol2.World
{
    internal sealed partial class ContinentBenchmark : MonoBehaviour
    {
        private const float Step = 1f / 60f;
        private ContinentTerrain terrain;
        private ContinentStreamProbe probe;
        private ContinentExplorer player;
        private Camera view;
        private RenderPipeline.StandardRequest request;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-continent-benchmark") < 0)
                return;

            Application.runInBackground = true;
            FindFirstObjectByType<ContinentExplorer>()?.gameObject.AddComponent<ContinentBenchmark>();
        }

        private IEnumerator Start()
        {
            yield return null;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            player = GetComponent<ContinentExplorer>();
            player.enabled = false;
            terrain = FindFirstObjectByType<ContinentTerrain>();
            probe = terrain.EnableStreamProbe();
            view = Camera.main;
            var target = new RenderTexture(Screen.width, Screen.height, 24);
            request = new RenderPipeline.StandardRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(view, request) || !target.Create())
            {
                Debug.LogError("Continent benchmark requires an offscreen render target.");
                Destroy(target);
                Application.Quit(1);
                yield break;
            }

            // Even a hidden player submits a real URP frame for every sample.
            view.enabled = false;
            try
            {
                Debug.Log($"Continent benchmark | {SystemInfo.processorType} | {SystemInfo.graphicsDeviceName} | {SystemInfo.graphicsDeviceType} | {target.width}x{target.height} offscreen");
                if (terrain.IsLargeWorld)
                {
                    string[] args = Environment.GetCommandLineArgs();
                    int captureBake = Array.IndexOf(args, "-world-capture-surface-bake");
                    if (captureBake >= 0)
                    {
                        try
                        {
                            CaptureSurfaceBake(args[captureBake + 1]);
                            Application.Quit(0);
                        }
                        catch (Exception error)
                        {
                            Debug.LogException(error);
                            Application.Quit(1);
                        }
                        yield break;
                    }
                    int bakeRoute = Array.IndexOf(args, "-world-surface-bake-check");
                    if (bakeRoute >= 0)
                    {
                        yield return CheckSurfaceBake(target, args[bakeRoute + 1]);
                        yield break;
                    }
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "-world-terrain-shadow-check") >= 0)
                    {
                        yield return CheckTerrainShadows(target);
                        yield break;
                    }
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "-landform-check") >= 0)
                    {
                        yield return CheckLandforms(target);
                        yield break;
                    }
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "-world-water-coast-check") >= 0)
                    {
                        yield return CheckWaterAndCoast(target);
                        yield break;
                    }
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "-world-estuary-check") >= 0)
                    {
                        yield return CheckEstuaries(target);
                        yield break;
                    }
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "-world-ocean-orbit-check") >= 0)
                    {
                        yield return CheckOceanOrbit(target);
                        yield break;
                    }
                    int waterReport = Array.IndexOf(Environment.GetCommandLineArgs(), "-world-water-tile-check");
                    if (waterReport >= 0)
                    {
                        yield return CheckWaterTile(target, Environment.GetCommandLineArgs(), waterReport);
                        yield break;
                    }
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "-world-crater-check") >= 0)
                    {
                        try
                        {
                            WorldSculptProbe.CaptureCrater(terrain, player, view, Render, suffix => CaptureIfRequested(target, suffix));
                            Application.Quit(0);
                        }
                        catch (Exception error)
                        {
                            Debug.LogException(error);
                            Application.Quit(1);
                        }
                        yield break;
                    }
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "-world-sculpt-check") >= 0)
                    {
                        yield return WorldSculptProbe.Run(terrain, player, view, Render, suffix => CaptureIfRequested(target, suffix));
                        yield break;
                    }
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "-world-precision-check") >= 0)
                    {
                        try
                        {
                            TerrainPrecisionBenchmark.Run(terrain);
                            Application.Quit(0);
                        }
                        catch (Exception error)
                        {
                            Debug.LogException(error);
                            Application.Quit(1);
                        }
                        yield break;
                    }
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "-world-shoreline-check") >= 0)
                    {
                        yield return CaptureShoreline(target);
                        yield break;
                    }
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "-world-controls-check") >= 0)
                    {
                        yield return MeasureControls(target);
                        yield break;
                    }
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "-world-expansion-check") >= 0)
                    {
                        yield return CaptureExpansion(target);
                        yield break;
                    }
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "-world-seam-check") >= 0)
                    {
                        yield return CaptureSeamRoute(target);
                        yield break;
                    }
                    yield return MeasureLargeWorld(target);
                    yield break;
                }
                player.EnterAt(terrain.Landing);
                player.Look(0f, 12f);
                for (int i = 0; i < 120; i++)
                {
                    terrain.Stream(player.transform.position, Vector3.zero);
                    Render();
                    yield return null;
                }
                yield return Measure("Walk", 1200, i => player.MoveForFrame(Vector2.up, 0f, false, false, Step));

                player.EnterAt(new Vector3(-36f, 5f, -960f));
                yield return Measure("Fly inland and reverse", 780, Fly);

                player.EnterAt(new Vector3(-0.5f, -0.8f, -944.5f));
                player.Look(22f, 22f);
                yield return Measure("Dive", 1000, i => player.MoveForFrame(Vector2.zero, -1f, false, false, Step));

                Vector3 feet = player.transform.position;
                if (Mathf.Abs(feet.y - terrain.SurfaceHeight(feet.x, feet.z)) > 0.16f)
                {
                    Debug.LogError("Continent benchmark diver did not reach the seabed.");
                    Application.Quit(1);
                    yield break;
                }

                player.EnterAt(new Vector3(-650f, 5f, -820f));
                yield return Measure("Cold 90-degree turn at 50 ms steps", 240, FlyIntoColdCoast);

                player.EnterAt(new Vector3(-540f, 165f, -960f));
                player.Look(29f, 18f);
                Render();
                AsyncGPUReadbackRequest readback = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32);
                while (!readback.done)
                    yield return null;
                if (readback.hasError || !HasScenePixels(readback))
                {
                    Debug.LogError("Continent benchmark could not verify the rendered terrain.");
                    Application.Quit(1);
                    yield break;
                }

                CaptureIfRequested(target);
                Application.Quit(0);
            }
            finally
            {
                target.Release();
                Destroy(target);
                view.enabled = true;
            }
        }

        private IEnumerator CaptureShoreline(RenderTexture target)
        {
            var pit = new WorldPosition(7533.5, 0d, -399851.5);
            float ground = terrain.SurfaceHeight(pit);
            if (!Check(ground > 0f && player.EnterAt(new WorldPosition(pit.X, ground + 5f, pit.Z)), "dry pit arrival"))
                yield break;
            for (int z = -1; z <= 1; z++)
            for (int x = -1; x <= 1; x++)
            for (int y = -3; y <= Mathf.FloorToInt(ground + 0.5f); y++)
                terrain.TryEdit(new Vector3Int(pit.Cell.x + x, y, pit.Cell.z + z), fill: false);
            yield return WarmWorld(200);
            var ocean = terrain.transform.Find("Ocean").GetComponent<MeshRenderer>();
            if (!Check(Physics.Raycast(terrain.ToLocal(new WorldPosition(pit.X, ground + 4f, pit.Z)), Vector3.down,
                out RaycastHit bottom) && terrain.ToWorld(bottom.point).Y < -1d, "excavated pit is open below the sea level"))
                yield break;
            if (!Check(!AffectedByOcean(pit, ground + 5f), "no rendered ocean in dry pit"))
                yield break;
            CaptureIfRequested(target, "dry-pit");

            WorldPosition sea = terrain.OceanEntry;
            if (!Check(player.EnterAt(sea), "shoreline sea arrival"))
                yield break;
            yield return WarmWorld(200);
            // Keep the player and their shadow out of the pixel used for the sea surface test.
            var seaView = new WorldPosition(sea.X + 5d, sea.Y, sea.Z);
            if (!Check(terrain.IsWater(terrain.ToLocal(seaView)) && terrain.IsWater(player.transform.position), "natural sea has water"))
                yield break;
            bool hasOcean = AffectedByOcean(seaView, 5d);
            CaptureIfRequested(target, "natural-sea");
            if (!Check(hasOcean, "natural sea surface is rendered"))
                yield break;
            Debug.Log("World shoreline | excavated land stays dry and natural sea renders over swimmable water");
            Application.Quit(0);

            bool AffectedByOcean(WorldPosition point, double height)
            {
                view.transform.SetPositionAndRotation(terrain.ToLocal(new WorldPosition(point.X, height, point.Z)),
                    Quaternion.Euler(90f, 0f, 0f));
                player.Environment.Apply(view, terrain, player.WorldFeet, underwater: false, underground: false);
                var pixel = new Texture2D(1, 1, TextureFormat.RGB24, false);
                RenderTexture previous = RenderTexture.active;
                bool enabled = ocean.enabled;
                try
                {
                    ocean.enabled = false;
                    Color32 dry = ReadCenter();
                    ocean.enabled = enabled;
                    Color32 withOcean = ReadCenter();
                    int rgbDiff = Math.Abs(dry.r - withOcean.r) + Math.Abs(dry.g - withOcean.g) + Math.Abs(dry.b - withOcean.b);
                    Debug.Log($"World shoreline | XZ ({point.X:F2}, {point.Z:F2}) | center pixel {dry} -> {withOcean}");
                    return rgbDiff > 3;
                }
                finally
                {
                    RenderTexture.active = previous;
                    Destroy(pixel);
                }

                Color32 ReadCenter()
                {
                    Render();
                    RenderTexture.active = target;
                    pixel.ReadPixels(new Rect(target.width / 2, target.height / 2, 1, 1), 0, 0, false);
                    return pixel.GetPixel(0, 0);
                }
            }
        }

        private IEnumerator MeasureControls(RenderTexture target)
        {
            // Drive the same frame handler as the normal player and render the resulting movement.
            player.ProcessInput(default, Step, focused: true);
            player.ProcessInput(new ExplorerInput { Destination = Key.F1 }, Step, focused: true);
            if (!Check(player.MoveMode == ExplorerMoveMode.Swim, "controls sea arrival"))
                yield break;
            player.ProcessInput(default, Step, focused: true);
            player.Look(0f, 24f);
            yield return WarmWorld(80);
            int limits = player.LimitedMoves;
            yield return Measure("Controls surface swim", 90, _ => player.ProcessInput(
                new ExplorerInput { Steer = true, Forward = 1f }, Step, focused: true), viewUpdatedByDrive: true);
            if (!Check(!player.IsDiving && Math.Abs(player.WorldFeet.Y + ContinentExplorer.SurfaceSwimDepth) < 0.02d,
                "controls shallow pitch holds the water surface"))
                yield break;
            CaptureIfRequested(target, "surface-swim");
            float surfaceRange = view.farClipPlane;
            player.Look(0f, 40f);
            yield return Measure("Controls pitched dive", 180, _ => player.ProcessInput(
                new ExplorerInput { Steer = true, Forward = 1f }, Step, focused: true), viewUpdatedByDrive: true);
            if (!Check(player.WorldFeet.Y < -10d && terrain.IsWater(view.transform.position), "controls diver and camera submerged"))
                yield break;
            float coastHeading = terrain.Landforms == null ? 0f : 90f;
            player.Look(coastHeading, -17f);
            if (!Check(player.Environment.UnderwaterEffectsActive && view.clearFlags == CameraClearFlags.Skybox
                && Mathf.Abs(view.farClipPlane - surfaceRange) < 0.02f, "controls underwater outdoor view"))
                yield break;
            Debug.Log(FormattableString.Invariant($"World controls | surface far clip {surfaceRange:F0} m | underwater far clip {view.farClipPlane:F0} m"));
            Render();
            CaptureIfRequested(target, "underwater-looks-out");
            player.Look(coastHeading, 3f);
            Render();
            CaptureIfRequested(target, "underwater-distance");
            yield return Measure("Controls rise and float", 240, _ => player.ProcessInput(
                new ExplorerInput { Rise = true }, Step, focused: true), viewUpdatedByDrive: true);
            if (!Check(Math.Abs(player.WorldFeet.Y + ContinentExplorer.SurfaceSwimDepth) < 0.02d && player.MoveMode == ExplorerMoveMode.Swim
                && !player.IsDiving && !player.Environment.UnderwaterEffectsActive,
                "controls stable water surface"))
                yield break;

            if (terrain.Landforms?.Size == 64000 && terrain.Seed == 20261121)
            {
                // Downstream section of the photographed southern creek.
                var river = new WorldPosition(1028d, 0d, -42392d);
                if (!Check(terrain.TryGetWaterColumn(river, out float riverLevel, out float bed)
                    && player.EnterAt(new WorldPosition(river.X, bed + 0.55d, river.Z))
                    && player.MoveMode == ExplorerMoveMode.Swim, "controls riverbed arrival"))
                    yield break;
                player.Look(180f, 24f);
                for (int i = 0; i < 90; i++)
                {
                    player.ProcessInput(new ExplorerInput { Descend = true }, Step, focused: true);
                    Render();
                    yield return null;
                }
                if (!Check(player.IsDiving && player.WorldFeet.Y - bed < 0.32d, "controls riverbed collision"))
                    yield break;
                Debug.Log(FormattableString.Invariant($"World controls | river level {riverLevel:F3} m | bed {bed:F3} m | reference Y {player.WorldFeet.Y:F3}"));
                CaptureIfRequested(target, "river-bed");
            }

            if (!Check(player.EnterAt(terrain.MountainOverview), "controls flight arrival"))
                yield break;
            player.Look(0f, 65f);
            yield return WarmWorld(80);
            double altitude = player.WorldFeet.Y;
            player.ProcessInput(new ExplorerInput { ToggleFlight = true }, Step, focused: true);
            yield return Measure("Controls sprint dive and turn", 240, i => player.ProcessInput(
                new ExplorerInput { Steer = true, Forward = 1f, Run = true, Look = i == 120 ? new Vector2(562.5f, 0f) : default },
                Step, focused: true), viewUpdatedByDrive: true);
            if (!Check(player.WorldFeet.Y < altitude - 57d && player.WorldFeet.X > terrain.MountainOverview.X + 10d
                && player.LimitedMoves == limits, "controls angled flight follows steering without streaming stops"))
                yield break;
            player.ProcessInput(default, Step, focused: true);
            Debug.Log("World controls | water and flight routes passed");
            Application.Quit(0);
        }

        private IEnumerator CaptureExpansion(RenderTexture target)
        {
            WorldEnvironment environment = player.Environment;
            environment.Clock.Paused = true;
            environment.PreviewWeather = new WeatherSample(0.20f, 0, 0, 0, 0);
            if (!Check(player.EnterAt(terrain.WorldLanding), "expansion coastal arrival"))
                yield break;
            player.Look(8f, 12f);
            yield return WarmWorld(95);
            CaptureIfRequested(target, "coast-day");
            for (int i = 0; i < 32; i++)
            {
                player.MoveForFrame(Vector2.up, 0f, false, false, Step, jump: false, crouch: false, walk: true);
                player.UpdateView();
                Render();
                yield return null;
            }
            CaptureIfRequested(target, "walk");
            player.MoveForFrame(Vector2.zero, 0f, false, false, Step, jump: true, crouch: false);
            for (int i = 0; i < 12; i++)
            {
                player.MoveForFrame(Vector2.zero, 0f, false, false, Step);
                player.UpdateView();
                Render();
                yield return null;
            }
            CaptureIfRequested(target, "jump");

            if (!Check(player.EnterAt(terrain.WorldLanding), "expansion weather arrival"))
                yield break;
            player.Look(8f, 12f);
            environment.PreviewWeather = new WeatherSample(0.92f, 0.95f, 0, 0.75f, 0);
            yield return WarmWorld(180);
            CaptureIfRequested(target, "coast-rain");
            Debug.Log($"Expansion benchmark | rain particles {environment.ActivePrecipitation}");
            if (!Check(environment.ActivePrecipitation > 0, "rain emission"))
                yield break;
            yield return Measure("Expansion rain", 360, _ => player.MoveForFrame(Vector2.up * 0.5f, 0f, false, false, Step));

            if (!Check(player.EnterAt(terrain.WorldLanding), "expansion snow arrival"))
                yield break;
            player.Look(8f, 12f);
            environment.PreviewWeather = new WeatherSample(0.80f, 0, 0.8f, 0.60f, 0);
            yield return WarmWorld(95);
            CaptureIfRequested(target, "coast-snow");
            Debug.Log($"Expansion benchmark | snow particles {environment.ActivePrecipitation}");

            environment.PreviewWeather = new WeatherSample(0.12f, 0, 0, 0, 0);
            AdvanceForCapture(9 * WorldClock.Hour);
            yield return WarmWorld(45);
            CaptureIfRequested(target, "coast-dusk");
            AdvanceForCapture(6 * WorldClock.Hour);
            yield return WarmWorld(45);
            CaptureIfRequested(target, "coast-night");
            WorldPosition sea = terrain.OceanEntry;
            if (!Check(player.EnterAt(new WorldPosition(sea.X, -6f, sea.Z)), "expansion dive arrival"))
                yield break;
            player.Look(22f, -25f);
            yield return WarmWorld(75);
            CaptureIfRequested(target, "underwater");
            if (!Check(environment.UnderwaterEffectsActive && environment.ActivePrecipitation == 0,
                "underwater view sheltered from precipitation"))
                yield break;
            AdvanceForCapture(10 * WorldClock.Hour);
            if (!Check(player.EnterAt(terrain.MountainOverview), "expansion mountaintop arrival"))
                yield break;
            player.Look(30f, 51f);
            player.MoveForFrame(Vector2.zero, 0f, false, true, Step);
            yield return WarmWorld(140);
            CaptureIfRequested(target, "mountains");

            WorldPosition basin = terrain.BasinEntry;
            if (!Check(player.EnterAt(new WorldPosition(basin.X - 4500, basin.Y + 2200, basin.Z - 10500)),
                "expansion basin arrival"))
                yield break;
            player.Look(39f, 21f);
            player.MoveForFrame(Vector2.zero, 0f, false, true, Step);
            yield return WarmWorld(140);
            CaptureIfRequested(target, "basin");
            Application.Quit(0);

            void AdvanceForCapture(int seconds)
            {
                environment.Clock.Paused = false;
                environment.Clock.Advance(seconds);
                environment.Clock.Paused = true;
            }
        }

        private IEnumerator CaptureSeamRoute(RenderTexture target)
        {
            var path = terrain.CavePassage;
            WorldPosition mouth = path[1];
            var ridge = new WorldPosition(mouth.X - 110, 0, mouth.Z + 64);
            ridge = new WorldPosition(ridge.X, terrain.SurfaceHeight(ridge) + 5, ridge.Z);
            if (!Check(player.EnterAt(ridge), "seam route ridge arrival"))
                yield break;
            player.Look(105f, 16f);
            yield return WarmWorld(220);
            CaptureIfRequested(target, "ground");

            if (!Check(player.EnterAt(path[0]), "seam route west arrival"))
                yield break;
            player.Look(90f, 23f);
            yield return WarmWorld(180);
            CaptureIfRequested(target, "mouth");

            // Inspect the lip while moving through almost coplanar views.
            for (int i = 0; i < 48; i++)
            {
                player.Look(87f + i * 0.125f, 23f);
                Render();
                if ((i & 7) == 0)
                    CaptureIfRequested(target, $"mouth-sweep-{i / 8}");
                yield return null;
            }

            for (int i = 1; i <= 3; i++)
            {
                for (int step = 0; step < 600; step++)
                {
                    Vector3 toward = terrain.ToLocal(path[i]) - player.transform.position;
                    if (toward.sqrMagnitude <= 0.001f)
                        break;
                    player.MoveWithVelocity(Vector3.ClampMagnitude(toward * 20f, 56f), 0.05f);
                    if ((step & 3) == 3)
                    {
                        player.UpdateView();
                        Render();
                        yield return null;
                    }
                }
                if (!Check((terrain.ToLocal(path[i]) - player.transform.position).sqrMagnitude <= 0.01f,
                    $"seam route waypoint {i}"))
                    yield break;
                player.Look(90f, i == 1 ? 30f : 6f);
                yield return WarmWorld(150);
                CaptureIfRequested(target, $"passage-{i}");
            }

            WorldPosition bend = path[2];
            WorldPosition join = path[3];
            var halfway = new WorldPosition((bend.X + join.X) * 0.5, (bend.Y + join.Y) * 0.5 - 21, (bend.Z + join.Z) * 0.5);
            if (!Check(player.EnterAt(halfway), "seam route tunnel floor arrival"))
                yield break;
            player.Look(86f, 10f);
            yield return WarmWorld(145);
            CaptureIfRequested(target, "tunnel");
            for (int i = 0; i < 20; i++)
            {
                player.MoveWithVelocity(Vector3.right * 16f, 0.05f);
                player.UpdateView();
                Render();
                if ((i & 3) == 3)
                    CaptureIfRequested(target, $"tunnel-step-{i / 4}");
                yield return null;
            }
            CaptureIfRequested(target, "tunnel-after-16m");
            Application.Quit(0);
        }

        private IEnumerator MeasureLargeWorld(RenderTexture target)
        {
            Debug.Log($"World benchmark | seed {terrain.Seed} | generator {WorldDefinition.GeneratorVersion} | axes {2 * WorldDefinition.Extent} m | height {WorldDefinition.MinY}..{WorldDefinition.MaxY} m");
            if (!Check(player.EnterAt(terrain.WorldLanding), "coastal arrival"))
                yield break;
            player.Look(0f, 13f);
            yield return WarmWorld(90);
            int initialLimits = player.LimitedMoves;
            yield return Measure("World coastal walk", 720, _ => player.MoveForFrame(Vector2.up, 0f, false, false, Step));
            yield return Measure("World coastal flight", 1200, _ => FollowTerrain(Vector3.forward, Step));
            if (!Check(player.LimitedMoves == initialLimits, "coastal path coverage"))
                yield break;

            if (!Check(player.EnterAt(terrain.OceanEntry), "ocean arrival"))
                yield break;
            player.Look(22f, 32f);
            yield return Measure("World dive", 990, _ => player.MoveForFrame(Vector2.zero, -1f, false, false, Step));
            WorldPosition sea = player.WorldFeet;
            if (!Check(Math.Abs(sea.Y - terrain.SurfaceHeight(sea)) < 0.35, "seabed collision"))
                yield break;

            WorldPosition abyss = terrain.DeepSeaEntry;
            float trench = terrain.SurfaceHeight(abyss);
            if (!Check(trench < -8000f && player.EnterAt(abyss), "deep sea entry"))
                yield break;
            int descent = Mathf.CeilToInt(-trench / 8.5f) + 12;
            int shiftsBeforeDive = player.OriginShifts;
            initialLimits = player.LimitedMoves;
            player.Look(0f, 60f);
            yield return WarmWorld(12);
            yield return Measure("World trench descent", descent, _ => player.MoveWithVelocity(Vector3.down * 170f, 0.05f));
            if (!Check(Math.Abs(player.WorldFeet.Y - trench) < 0.35 && terrain.IsWater(player.transform.position + Vector3.up)
                && player.OriginShifts - shiftsBeforeDive >= 3 && player.LimitedMoves == initialLimits, "trench floor after vertical origin shifts"))
                yield break;

            var passage = terrain.CavePassage;
            if (!Check(player.EnterAt(passage[0]), "west cave arrival"))
                yield break;
            int next = 1;
            initialLimits = player.LimitedMoves;
            player.Look(90f, 24f);
            yield return Measure("World continuous cave crossing", 1120, _ =>
            {
                Vector3 delta = terrain.ToLocal(passage[next]) - player.transform.position;
                if (delta.sqrMagnitude < 0.0025f && next < passage.Count - 1)
                    delta = terrain.ToLocal(passage[++next]) - player.transform.position;
                player.MoveWithVelocity(Vector3.ClampMagnitude(delta * 20f, 56f), 0.05f);
            });
            if (!Check(next == passage.Count - 1
                && (terrain.ToLocal(passage[next]) - player.transform.position).sqrMagnitude < 0.01f,
                "cave exit reached by continuous movement"))
                yield break;
            if (!Check(player.LimitedMoves == initialLimits, "cave path coverage"))
                yield break;

            if (!Check(player.EnterAt(terrain.CaveChamber), "main chamber arrival"))
                yield break;
            player.Look(14f, 14f);
            yield return WarmWorld(145);
            if (!Check(terrain.CaveTiles > 16, "distant cave walls were published"))
                yield break;
            CaptureIfRequested(target, "cave");

            WorldPosition join = passage[3];
            var corner = new Vector3Int((int)Math.Round(join.X / 8) * 8, (int)Math.Round(join.Y / 8) * 8,
                (int)Math.Round(join.Z / 8) * 8);
            if (!Check(player.EnterAt(new WorldPosition(join.X + 5, join.Y + 4, join.Z + 4)), "cave junction arrival"))
                yield break;
            yield return WarmWorld(45);
            var cornerTimes = new double[60];
            var cornerFrames = new double[cornerTimes.Length];
            for (int i = 0; i < cornerTimes.Length; i++)
            {
                player.MoveWithVelocity(Vector3.zero, Step);
                long tick = System.Diagnostics.Stopwatch.GetTimestamp();
                if (!Check(terrain.TryEdit(corner, fill: (i & 1) == 0), "three-axis cave corner edit"))
                    yield break;
                cornerTimes[i] = Elapsed(tick);
                player.UpdateView();
                Render();
                yield return null;
                cornerFrames[i] = Time.unscaledDeltaTime * 1000d;
            }
            Debug.Log(FormattableString.Invariant($"World benchmark | cave junction | first edit {cornerTimes[0]:F3} ms | first frame {cornerFrames[0]:F3} ms"));
            Report("World cave junction", "cached edit to collision", cornerTimes[1..]);
            Report("World cave junction", "rendered edit frame", cornerFrames);

            WorldPosition deep = terrain.DeepChamber;
            var deepFloor = new WorldPosition(deep.X + 0.5, deep.Y - 194, deep.Z + 0.5);
            if (!Check(player.EnterAt(deepFloor), "deep cavern arrival"))
                yield break;
            player.Look(31f, 49f);
            yield return WarmWorld(40);
            Vector3 eye = player.transform.position;
            if (!Check(terrain.Pick(new Ray(eye, Vector3.down), eye, out var dig, out _), "deep cavern selection"))
                yield break;
            var editTimes = new double[100];
            for (int i = 0; i < editTimes.Length; i++)
            {
                long tick = System.Diagnostics.Stopwatch.GetTimestamp();
                if (!Check(terrain.TryEdit(dig, fill: (i & 1) != 0), "deep edit"))
                    yield break;
                editTimes[i] = Elapsed(tick);
                Render();
                yield return null;
            }
            Report("World deep cavern", "edit to collision", editTimes);
            if (!Check(terrain.TryEdit(dig, fill: false), "deep edit before eviction")
                || !Check(Physics.Raycast(eye, Vector3.down, out var cut, 5f), "edited floor collision"))
                yield break;
            double cutY = terrain.ToWorld(cut.point).Y;

            if (!Check(VerifyCorners(), "four corner collisions and edits")
                || !Check(player.EnterAt(deepFloor), "deep return after eviction"))
                yield break;
            if (!Check(Physics.Raycast(player.transform.position, Vector3.down, out cut, 5f)
                && Math.Abs(terrain.ToWorld(cut.point).Y - cutY) < 0.002, "deep edit survives eviction"))
                yield break;

            WorldPosition sky = terrain.MountainOverview;
            if (!Check(player.EnterAt(sky), "upper bound arrival"))
                yield break;
            player.Look(29f, 51f);
            yield return WarmWorld(80);
            yield return Measure("World high altitude", 720, _ => player.MoveForFrame(Vector2.up, 0f, true, true, Step));
            if (!Check(terrain.ActivePages == 0, "high altitude skips empty volume"))
                yield break;
            AsyncGPUReadbackRequest readback = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32);
            while (!readback.done)
                yield return null;
            if (!Check(!readback.hasError && HasScenePixels(readback), "high altitude GPU readback"))
                yield break;
            CaptureIfRequested(target);

            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-world-long-run") >= 0)
            {
                yield return RunLongRoute();
                if (!longRoutePassed)
                    yield break;
                longRoutePassed = false;
                yield return RunSurfaceRoute();
                if (!longRoutePassed)
                    yield break;
            }
            Application.Quit(0);
        }

        private bool longRoutePassed;

        private IEnumerator RunLongRoute()
        {
            const float dt = 0.5f;
            const float velocity = 68f;
            const double width = 1_000_000d;
            Vector3 heading = new Vector3(1f, 0f, 1f).normalized;
            int count = (int)Math.Ceiling(width * Math.Sqrt(2) / (velocity * dt));
            var times = new double[count];
            var start = new WorldPosition(-width / 2, 10800, -width / 2);
            if (!Check(terrain.TryEdit(new Vector3Int(610128, 10800, -400077), fill: true), "off-route sky edit")
                || !Check(player.EnterAt(start), "long route start"))
                yield break;
            player.Look(45f, 43f);
            yield return WarmWorld(80);
            long warmMemory = Profiler.GetTotalAllocatedMemoryLong();
            long peakMemory = warmMemory;
            int limits = player.LimitedMoves;
            int shifts = player.OriginShifts;
            int peakTiles = 0;
            for (int i = 0; i < count; i++)
            {
                long tick = System.Diagnostics.Stopwatch.GetTimestamp();
                float delta = i < count - 1 ? dt : (float)(width * Math.Sqrt(2) / velocity - (count - 1) * dt);
                player.MoveWithVelocity(heading * velocity, delta);
                times[i] = Elapsed(tick);
                peakTiles = Math.Max(peakTiles, terrain.HorizonTiles);
                if ((i & 31) == 31 || i == count - 1)
                {
                    peakMemory = Math.Max(peakMemory, Profiler.GetTotalAllocatedMemoryLong());
                    player.UpdateView();
                    Render();
                    yield return null;
                }
            }
            var end = player.WorldFeet;
            if (!Check(player.LimitedMoves == limits && terrain.ActivePages == 0
                && Math.Abs(end.X - width / 2) < 12 && Math.Abs(end.Z - width / 2) < 12,
                "1,000 kilometre coordinate span with continuous motion"))
                yield break;
            yield return WarmWorld(120);
            Debug.Log(FormattableString.Invariant(
                $"World benchmark | long route | {count} continuous steps at up to {dt:F2} s | XZ span {end.X - start.X:F2} × {end.Z - start.Z:F2} m | {player.OriginShifts - shifts} origin shifts | {player.LimitedMoves - limits} limits | peak tiles {peakTiles} | allocated warm/peak/after {warmMemory / 1048576d:F1}/{peakMemory / 1048576d:F1}/{Profiler.GetTotalAllocatedMemoryLong() / 1048576d:F1} MiB"));
            Report("World long route", "continuous CPU step", times);
            longRoutePassed = true;
        }

        private IEnumerator RunSurfaceRoute()
        {
            const float StepSeconds = 0.05f;
            const int Count = 18824;
            WorldPosition start = terrain.WorldLanding;
            var launch = new WorldPosition(start.X, start.Y + 7, start.Z);
            if (!Check(player.EnterAt(launch), "low altitude long route start"))
                yield break;
            player.Look(0f, 18f);
            yield return WarmWorld(100);
            var times = new double[Count];
            int limits = player.LimitedMoves;
            int initialPages = terrain.BuiltPages;
            long initialSamples = terrain.EvaluatedDensitySamples;
            long warm = Profiler.GetTotalAllocatedMemoryLong();
            long peak = warm;
            int peakMeshes = 0;
            for (int i = 0; i < Count; i++)
            {
                FollowTerrain(Vector3.forward, StepSeconds);
                times[i] = terrain.LastStreamMs;
                peakMeshes = Math.Max(peakMeshes, terrain.VisibleChunks);
                if ((i & 15) == 15 || i == Count - 1)
                {
                    peak = Math.Max(peak, Profiler.GetTotalAllocatedMemoryLong());
                    player.UpdateView();
                    Render();
                    yield return null;
                }
            }
            double distance = player.WorldFeet.Z - start.Z;
            int stopped = player.LimitedMoves - limits;
            Debug.Log(FormattableString.Invariant(
                $"World benchmark | surface route | {Count} continuous steps at {StepSeconds:F2} s | {distance:F2} m | {stopped} limits | {terrain.BuiltPages - initialPages} prepared pages | {terrain.EvaluatedDensitySamples - initialSamples} density samples | peak collision meshes {peakMeshes} | allocated warm/peak {warm / 1048576d:F1}/{peak / 1048576d:F1} MiB"));
            Report("World surface route", "stream", times);
            if (!Check(distance > 63000 && stopped == 0, "64 kilometre low altitude continuous route"))
                yield break;
            yield return WarmWorld(120);
            long settled = Profiler.GetTotalAllocatedMemoryLong();
            if (!Check(player.EnterAt(terrain.MountainOverview), "leave low altitude regions"))
                yield break;
            yield return WarmWorld(120);
            Debug.Log(FormattableString.Invariant($"World benchmark | surface route | allocated settled/after departure {settled / 1048576d:F1}/{Profiler.GetTotalAllocatedMemoryLong() / 1048576d:F1} MiB | active pages {terrain.ActivePages}"));
            longRoutePassed = true;
        }

        private IEnumerator WarmWorld(int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                terrain.Stream(player.transform.position, Vector3.zero);
                player.UpdateView();
                Render();
                yield return null;
            }
        }

        private bool VerifyCorners()
        {
            const double end = WorldDefinition.Extent - 0.75;
            foreach (int x in new[] { -1, 1 })
            foreach (int z in new[] { -1, 1 })
            {
                var at = new WorldPosition(end * x, 0, end * z);
                var eye = new WorldPosition(at.X, terrain.SurfaceHeight(at) + 1.25, at.Z);
                long tick = System.Diagnostics.Stopwatch.GetTimestamp();
                if (!player.EnterAt(eye))
                    return false;
                double entryMs = Elapsed(tick);
                Vector3 local = player.transform.position;
                if (!terrain.Pick(new Ray(local, Vector3.down), local, out var cell, out _)
                    || cell.x != at.Cell.x || cell.z != at.Cell.z || !terrain.TryEdit(cell, fill: false))
                    return false;
                Debug.Log(FormattableString.Invariant($"World benchmark | far corner edit | {cell.x}, {cell.y}, {cell.z} | arrival {entryMs:F3} ms"));
            }
            return true;
        }

        private static double Elapsed(long start) => (System.Diagnostics.Stopwatch.GetTimestamp() - start)
            * (1000d / System.Diagnostics.Stopwatch.Frequency);

        private static bool Check(bool condition, string stage)
        {
            if (condition)
                return true;
            Debug.LogError($"World benchmark failed at {stage}.");
            Application.Quit(1);
            return false;
        }

        private void Fly(int frame)
        {
            bool returning = frame >= 540;
            if (frame == 0 || frame == 540)
                player.Look(returning ? 180f : 0f, 13f);
            FollowTerrain(returning ? Vector3.back : Vector3.forward, Step);
        }

        private void FlyIntoColdCoast(int frame)
        {
            bool turningInland = frame >= 120;
            if (frame == 0 || frame == 120)
                player.Look(turningInland ? 0f : 90f, 13f);
            FollowTerrain(turningInland ? Vector3.forward : Vector3.right, 0.05f);
        }

        private void FollowTerrain(Vector3 forward, float step)
        {
            Vector3 p = player.transform.position;
            Vector3 ahead = p + forward * 27f;
            float desiredAltitude = Mathf.Max(5f, terrain.SurfaceHeight(ahead.x, ahead.z) + 7f);
            if (terrain.IsLargeWorld)
            {
                WorldPosition global = terrain.ToWorld(ahead);
                desiredAltitude = terrain.ToLocal(new WorldPosition(global.X, Math.Max(5d, terrain.SurfaceHeight(global) + 7d), global.Z)).y;
            }
            float lift = Mathf.Clamp((desiredAltitude - p.y) * 0.40f, -1f, 1f);
            player.MoveForFrame(Vector2.up, lift, true, true, step);
        }

        private IEnumerator Measure(string route, int count, Action<int> drive, bool viewUpdatedByDrive = false)
        {
            var frames = new double[count];
            var stream = new double[count];
            var gpu = terrain.IsLargeWorld && FrameTimingManager.IsFeatureEnabled() ? new double[count] : null;
            var gpuTiming = gpu != null ? new FrameTiming[1] : null;
            using var environmentTiming = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Reol2.World.Environment");
            using var animationTiming = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Reol2.Player.Animation");
            using var ikTiming = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Reol2.Player.FootIK");
            var environmentTimes = new double[count];
            var animationTimes = new double[count];
            var ikTimes = new double[count];
            int gpuSamples = 0;
            var readyDistance = new double[count];
            var stageTotals = new double[(int)Stage.Count];
            var stageTimes = new double[stageTotals.Length][];
            for (int stage = 0; stage < stageTimes.Length; stage++)
                stageTimes[stage] = new double[count];

            int initialLimits = player.LimitedMoves;
            int initialBuilds = terrain.BuiltPages;
            long initialSamples = terrain.EvaluatedDensitySamples;
            int created = probe.CreatedViews, reused = probe.ReusedViews, released = probe.ReleasedViews;
            int canceled = probe.CanceledRequests;
            int surfaces = probe.SurfacePages, noSurface = probe.PagesWithoutSurface;
            int peakPages = 0, peakMeshes = 0, peakQueue = 0;
            int peakPath = 0, peakUnrequested = 0, oldestPath = 0, oldestCompleted = 0;
            long bytes = 0, peakMemory = 0;
            double pathXZ = 0d;
            WorldPosition previous = player.WorldFeet;
            int peakTiles = 0, peakTriangles = 0, peakCaveTiles = 0;
            for (int i = 0; i < count; i++)
            {
                long startBytes = GC.GetAllocatedBytesForCurrentThread();
                drive(i);
                bytes += GC.GetAllocatedBytesForCurrentThread() - startBytes;
                WorldPosition p = player.WorldFeet;
                double dx = p.X - previous.X, dz = p.Z - previous.Z;
                pathXZ += Math.Sqrt(dx * dx + dz * dz);
                previous = p;
                stream[i] = terrain.LastStreamMs;
                readyDistance[i] = probe.ReadyDistance;
                peakPath = Math.Max(peakPath, probe.MissingPathPages);
                peakUnrequested = Math.Max(peakUnrequested, probe.OutsideWindowPathPages);
                oldestPath = Math.Max(oldestPath, probe.OldestPathWait);
                oldestCompleted = Math.Max(oldestCompleted, probe.OldestCompletion);
                for (int stage = 0; stage < stageTimes.Length; stage++)
                {
                    double duration = probe.Milliseconds((Stage)stage);
                    stageTimes[stage][i] = duration;
                    stageTotals[stage] += duration;
                }
                peakPages = Math.Max(peakPages, terrain.ActivePages);
                peakMeshes = Math.Max(peakMeshes, terrain.VisibleChunks);
                peakQueue = Math.Max(peakQueue, terrain.PendingPages);
                peakTiles = Math.Max(peakTiles, terrain.HorizonTiles);
                peakTriangles = Math.Max(peakTriangles, terrain.HorizonTriangles);
                peakCaveTiles = Math.Max(peakCaveTiles, terrain.CaveTiles);
                if (i % 60 == 0 || i == count - 1)
                    peakMemory = Math.Max(peakMemory, Profiler.GetTotalAllocatedMemoryLong());
                if (!viewUpdatedByDrive)
                    player.UpdateView();
                if (gpu != null)
                    FrameTimingManager.CaptureFrameTimings();
                Render();
                yield return null;
                frames[i] = Time.unscaledDeltaTime * 1000d;
                environmentTimes[i] = environmentTiming.Valid ? environmentTiming.LastValue / 1e6 : 0d;
                animationTimes[i] = animationTiming.Valid ? animationTiming.LastValue / 1e6 : 0d;
                ikTimes[i] = ikTiming.Valid ? ikTiming.LastValue / 1e6 : 0d;
                if (gpu != null && i >= 8 && FrameTimingManager.GetLatestTimings(1, gpuTiming) != 0 && gpuTiming[0].gpuFrameTime > 0)
                    gpu[gpuSamples++] = gpuTiming[0].gpuFrameTime;
            }

            Debug.Log(FormattableString.Invariant(
                $"Continent benchmark | {route} | {count} frames | XZ path {pathXZ:F2} m | end ({previous.X:F2}, {previous.Y:F2}, {previous.Z:F2}) | limits {player.LimitedMoves - initialLimits} | built {terrain.BuiltPages - initialBuilds} | peak pages {peakPages} | meshes {peakMeshes} | queue {peakQueue} | motion allocation {bytes} B | Unity allocated peak {peakMemory / 1048576d:F1} MiB"));
            if (terrain.IsLargeWorld)
            {
                Debug.Log($"World benchmark | {route} | peak horizon tiles {peakTiles} | visible horizon triangles {peakTriangles} | peak cave tiles {peakCaveTiles} | density samples {terrain.EvaluatedDensitySamples - initialSamples}");
                int overBudget = 0;
                foreach (double frame in frames)
                    if (frame > 1000d / 60)
                        overBudget++;
                Debug.Log($"World benchmark | {route} | over 16.67 ms {overBudget}/{count} | GPU timing samples {gpuSamples}");
            }
            Report(route, "rendered frame", frames);
            Report(route, "stream", stream);
            if (environmentTiming.Valid)
                Report(route, "environment", environmentTimes);
            if (animationTiming.Valid)
                Report(route, "animation driver", animationTimes);
            if (ikTiming.Valid)
                Report(route, "foot IK", ikTimes);
            if (gpuSamples > 0)
            {
                Array.Resize(ref gpu, gpuSamples);
                Report(route, "GPU frame", gpu);
            }
            Array.Sort(readyDistance);
            Debug.Log(FormattableString.Invariant(
                $"Continent benchmark | {route} | path coverage min {readyDistance[0]:F2} m, p05 {Percentile(readyDistance, 0.05):F2} m | missing path peak {peakPath} | outside window path peak {peakUnrequested} | oldest pending path {oldestPath} steps | oldest completed {oldestCompleted} steps | window canceled {probe.CanceledRequests - canceled} | surface/other pages {probe.SurfacePages - surfaces}/{probe.PagesWithoutSurface - noSurface} | views new/reused/released {probe.CreatedViews - created}/{probe.ReusedViews - reused}/{probe.ReleasedViews - released}"));
            for (int stage = 0; stage < stageTimes.Length; stage++)
            {
                Array.Sort(stageTimes[stage]);
                Debug.Log(FormattableString.Invariant(
                    $"Continent benchmark | {route} | {(Stage)stage}: total {stageTotals[stage]:F2} ms, frame p99 {Percentile(stageTimes[stage], 0.99):F3} ms"));
            }
        }

        private void Render() => RenderPipeline.SubmitRenderRequest(view, request);

        private static bool HasScenePixels(AsyncGPUReadbackRequest readback)
        {
            var pixels = readback.GetData<Color32>();
            Color32 first = pixels[0];
            for (int i = 0; i < pixels.Length; i += 997)
            {
                Color32 pixel = pixels[i];
                if (pixel.r != first.r || pixel.g != first.g || pixel.b != first.b)
                    return true;
            }
            return false;
        }

        private static void CaptureIfRequested(RenderTexture target, string suffix = null)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-continent-image");
            if (index < 0 || index + 1 >= args.Length)
                return;

            string output = args[index + 1];
            if (suffix != null)
                output = Path.ChangeExtension(output, null) + "-" + suffix + ".png";
            File.WriteAllBytes(output, EncodeFrame(target));
        }

        private static byte[] EncodeFrame(RenderTexture target)
        {
            var capture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                capture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                capture.Apply();
                return capture.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previous;
                Destroy(capture);
            }
        }

        private static void Report(string route, string metric, double[] samples)
        {
            Array.Sort(samples);
            Debug.Log(FormattableString.Invariant(
                $"Continent benchmark | {route} | {metric}: p50 {Percentile(samples, 0.5):F3}, p95 {Percentile(samples, 0.95):F3}, p99 {Percentile(samples, 0.99):F3}, max {samples[^1]:F3} ms"));
        }

        private static double Percentile(double[] sorted, double p) => sorted[(int)Math.Ceiling(sorted.Length * p) - 1];
    }
}
#endif
