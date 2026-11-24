#if DEVELOPMENT_BUILD || REOL_TEST_BUILD
using System;
using System.Collections;
using UnityEngine;

namespace Reol2.World
{
    internal sealed partial class ContinentBenchmark
    {
        private IEnumerator CheckTerrainShadows(RenderTexture target)
        {
            if (!Check(terrain.Landforms?.Size == 64000 && terrain.Seed == 20261121,
                "terrain shadow fixture requires -landforms 64 -world-seed 20261121"))
                yield break;

            var spot = new WorldPosition(-4719, 937.1, 3676);
            var at = new WorldPosition(spot.X, Math.Max(spot.Y, terrain.SurfaceHeight(spot) + 0.15), spot.Z);
            if (!Check(player.EnterAt(at), "terrain shadow arrival"))
                yield break;

            player.Environment.Clock.Paused = true;
            for (int i = 0; i < 4; i++)
                player.ProcessInput(new ExplorerInput { Zoom = -1f }, 0f, focused: true);
            for (int i = 0; i < 60; i++)
                player.MoveForFrame(Vector2.zero, 0f, false, false, Step);
            yield return SettleView();
            player.Look(240f, 6f);

            Light sun = RenderSettings.sun;
            if (!Check(sun != null && sun.shadows != LightShadows.None, "terrain shadow light setup"))
                yield break;
            Render();
            CaptureIfRequested(target, "shadows");
            Color32[] lit = ReadTerrainFrame(target);
            LightShadows mode = sun.shadows;
            Color32[] unshadowed;
            try
            {
                sun.shadows = LightShadows.None;
                Render();
                CaptureIfRequested(target, "no-shadows");
                unshadowed = ReadTerrainFrame(target);
            }
            finally
            {
                sun.shadows = mode;
            }

            int width = target.width, height = target.height;
            // At this camera the stray shadow crosses both slopes, outside the player's local shadow.
            int far = CountBrightened(lit, unshadowed, width, height, Rect.MinMaxRect(0f, 0.15f, 0.43f, 1f))
                + CountBrightened(lit, unshadowed, width, height, Rect.MinMaxRect(0.625f, 0.50f, 0.75f, 0.84f));
            int near = CountBrightened(lit, unshadowed, width, height, Rect.MinMaxRect(0.466f, 0.32f, 0.53f, 0.40f));
            Debug.Log($"Terrain shadow check | far changed pixels {far} | feet changed pixels {near} | {terrain.HorizonTiles} tiles | {terrain.ActivePages} pages");
            if (!Check(far == 0 && near > 0, "terrain shadow distance and near character shadow"))
                yield break;

            player.Look(230f, 6f);
            Render();
            CaptureIfRequested(target, "yaw-230");
            player.Look(250f, 12f);
            Render();
            CaptureIfRequested(target, "yaw-250");
            Application.Quit(0);
        }

        private static int CountBrightened(Color32[] lit, Color32[] unshadowed, int width, int height, Rect area)
        {
            int count = 0;
            int xMin = (int)(width * area.xMin), xMax = (int)(width * area.xMax);
            int yMin = (int)(height * area.yMin), yMax = (int)(height * area.yMax);
            for (int y = yMin; y < yMax; y++)
            for (int x = xMin; x < xMax; x++)
            {
                int index = x + y * width;
                Color32 a = lit[index], b = unshadowed[index];
                if (b.r - a.r >= 10 || b.g - a.g >= 10 || b.b - a.b >= 10)
                    count++;
            }
            return count;
        }

        private static Color32[] ReadTerrainFrame(RenderTexture target)
        {
            var capture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                capture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                return capture.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                Destroy(capture);
            }
        }
    }
}
#endif
