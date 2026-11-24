#if UNITY_EDITOR || DEVELOPMENT_BUILD || REOL_TEST_BUILD
using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace Reol2.World
{
    internal sealed class WorldRenderReport
    {
        private const int HistorySize = 90;
        private const double SampleInterval = 1d / 30d;
        private static readonly WaitForEndOfFrame EndOfFrame = new();
        private readonly ContinentTerrain terrain;
        private readonly ContinentExplorer player;
        private readonly Camera camera;
        private readonly FrameState[] history = new FrameState[HistorySize];
        private int next, count;
        private double nextSample;

        internal bool Saving { get; private set; }

        internal WorldRenderReport(ContinentTerrain terrain, ContinentExplorer player, Camera camera)
        {
            this.terrain = terrain;
            this.player = player;
            this.camera = camera;
        }

        internal void Record()
        {
            double now = Time.unscaledTimeAsDouble;
            if (now < nextSample)
                return;
            nextSample = now + SampleInterval;
            history[next] = ReadFrame(now);
            next = (next + 1) % history.Length;
            count = Math.Min(count + 1, history.Length);
        }

        internal IEnumerator CaptureScreen(Action<string> showStatus)
        {
            Saving = true;
            var state = ReadReport();
            yield return EndOfFrame;
            Texture2D screenshot = null;
            try
            {
                screenshot = ScreenCapture.CaptureScreenshotAsTexture();
                string directory = Path.Combine(Application.persistentDataPath, "RenderReports");
                string path = Save(state, screenshot.EncodeToPNG(), directory);
                Debug.Log($"Render report saved to {path}");
                showStatus($"Saved RenderReports/{Path.GetFileName(path)}");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                showStatus("Render report export failed. See Player.log.");
            }
            finally
            {
                if (screenshot != null)
                    UnityEngine.Object.Destroy(screenshot);
                Saving = false;
            }
        }

        internal Report ReadReport()
        {
            var preceding = new FrameState[count];
            for (int i = 0; i < count; i++)
                preceding[i] = history[(next - count + i + history.Length) % history.Length];
            WeatherSample weather = player.Environment.RenderWeather;
            WorldPosition feet = player.WorldFeet;
            var oceanProbe = new WorldPosition(feet.X, -0.01, feet.Z);
            return new Report
            {
                build = Application.buildGUID,
                unity = Application.unityVersion,
                graphics = $"{SystemInfo.graphicsDeviceName} | {SystemInfo.graphicsDeviceVersion}",
                scene = terrain.gameObject.scene.name,
                seed = terrain.Seed,
                regionKm = terrain.Landforms?.Size / 1000 ?? 0,
                current = ReadFrame(Time.unscaledTimeAsDouble),
                history = preceding,
                cameraAspect = camera.aspect,
                cameraFov = camera.fieldOfView,
                cameraNear = camera.nearClipPlane,
                cameraFar = camera.farClipPlane,
                cameraViewport = camera.rect,
                screenSize = new Vector2Int(Screen.width, Screen.height),
                renderScale = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp ? urp.renderScale : 1f,
                windowMode = Screen.fullScreenMode.ToString(),
                fog = RenderSettings.fogColor,
                fogStart = RenderSettings.fogStartDistance,
                fogEnd = RenderSettings.fogEndDistance,
                cloud = weather.Cloud,
                rain = weather.Rain,
                snow = weather.Snow,
                haze = weather.Haze,
                storm = weather.Storm,
                groundBelowPlayer = terrain.SurfaceHeight(feet),
                seaBelowPlayer = terrain.IsOcean(terrain.ToLocal(oceanProbe)),
                editedSamples = terrain.EditedSamples,
                sharedSeaTriangles = terrain.RenderReportSeaTriangles,
                sharedInlandWaterTriangles = terrain.RenderReportInlandWaterTriangles,
                visibleTiles = terrain.RenderReportTiles()
            };
        }

        private FrameState ReadFrame(double now) => new()
        {
            frame = Time.frameCount,
            seconds = now,
            clockTick = player.Environment.Clock.Tick,
            feet = new Position(player.WorldFeet),
            camera = new Position(terrain.ToWorld(camera.transform.position)),
            cameraRotation = camera.transform.rotation,
            origin = terrain.FrameOrigin,
            velocity = player.ActualVelocity,
            mode = player.MoveMode,
            speedMultiplier = player.MovementMultiplier,
            pages = terrain.ActivePages,
            pendingPages = terrain.PendingPages,
            horizonTiles = terrain.HorizonTiles,
            pendingHorizonTiles = terrain.PendingHorizonTiles,
            streamMs = terrain.LastStreamMs
        };

        internal static string Save(Report report, byte[] screenshot, string directory)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"Reol2-render-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.zip");
            string temporary = path + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
                {
                    using (var json = new StreamWriter(archive.CreateEntry("report.json").Open()))
                        json.Write(JsonUtility.ToJson(report, prettyPrint: true));
                    using Stream image = archive.CreateEntry("frame.png", CompressionLevel.NoCompression).Open();
                    image.Write(screenshot, 0, screenshot.Length);
                }
                File.Move(temporary, path);
                return path;
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        [Serializable]
        internal sealed class Report
        {
            public int version = 3;
            public string build, unity, graphics, scene, windowMode;
            public long seed;
            public int regionKm, editedSamples, sharedSeaTriangles, sharedInlandWaterTriangles;
            public Vector2Int screenSize;
            public float cameraAspect, cameraFov, cameraNear, cameraFar, renderScale;
            public Rect cameraViewport;
            public Color fog;
            public float fogStart, fogEnd, cloud, rain, snow, haze, storm, groundBelowPlayer;
            public bool seaBelowPlayer;
            public FrameState current;
            public FrameState[] history;
            public TileState[] visibleTiles;
        }

        [Serializable]
        internal struct Position
        {
            public double x, y, z;

            internal Position(WorldPosition point) { x = point.X; y = point.Y; z = point.Z; }
        }

        [Serializable]
        internal struct FrameState
        {
            public int frame, pages, pendingPages, horizonTiles, pendingHorizonTiles;
            public double seconds, streamMs;
            public long clockTick;
            public Position feet, camera;
            public Quaternion cameraRotation;
            public Vector3Int origin;
            public Vector3 velocity;
            public ExplorerMoveMode mode;
            public float speedMultiplier;
        }

        [Serializable]
        internal struct TileState
        {
            public int x, z, width, lod, terrainTriangles, regionalWaterTriangles, clippedSeaTriangles;
            public float minTerrainY, maxTerrainY, sharedWaterLevel;
            public bool terrainVisible, sharedSea;
        }
    }
}
#endif
