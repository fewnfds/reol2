using UnityEngine;

#if DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
#endif

namespace Reol2.Demo
{
    [RequireComponent(typeof(VoxelDemoTerrain))]
    public sealed class VoxelDemoBenchmark : MonoBehaviour
    {
#if DEVELOPMENT_BUILD
        private const int WarmupFrames = 120;
        private const int SampleCount = 100;

        private IEnumerator Start()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            if (Array.IndexOf(arguments, "-voxel-benchmark") < 0)
                yield break;

            Application.runInBackground = true;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            Camera view = Camera.main;
            var target = new RenderTexture(Screen.width, Screen.height, 24);
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(view, request) || !target.Create())
            {
                Debug.LogError("Voxel benchmark could not start offscreen rendering.");
                Destroy(target);
                Application.Quit(1);
                yield break;
            }

            // A hidden Player can skip the normal camera loop. Submit exactly one real render per sample.
            view.enabled = false;
            try
            {
                yield return Measure(view, request);
            }
            finally
            {
                target.Release();
                Destroy(target);
                view.enabled = true;
            }
        }

        private IEnumerator Measure(Camera view, RenderPipeline.StandardRequest request)
        {
            VoxelDemoTerrain terrain = GetComponent<VoxelDemoTerrain>();
            Vector3Int cell = VoxelDemoTerrain.GateLowerCell;
            terrain.TryEdit(cell, fill: false);
            terrain.TryEdit(cell, fill: true);
            for (int frame = 0; frame < WarmupFrames; frame++)
            {
                RenderPipeline.SubmitRenderRequest(view, request);
                yield return null;
            }

            var idleFrames = new double[SampleCount];
            for (int i = 0; i < SampleCount; i++)
            {
                RenderPipeline.SubmitRenderRequest(view, request);
                yield return null;
                idleFrames[i] = Time.unscaledDeltaTime * 1000d;
            }

            var editedFrames = new double[SampleCount];
            var editTimes = new double[SampleCount];
            var extracts = new double[SampleCount];
            var uploads = new double[SampleCount];
            var bakes = new double[SampleCount];
            var publishes = new double[SampleCount];
            long allocated = 0;
            long worstAllocation = 0;

            for (int i = 0; i < SampleCount; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                bool changed = terrain.TryEdit(cell, fill: (i & 1) != 0);
                long end = Stopwatch.GetTimestamp();
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                VoxelDemoTerrain.EditTiming timing = terrain.LastEdit;
                if (!changed || timing.ChunkCount != 2)
                {
                    Debug.LogError($"Voxel benchmark failed at edit {i}, updated chunks {timing.ChunkCount}.");
                    Application.Quit(1);
                    yield break;
                }

                allocated += bytes;
                worstAllocation = Math.Max(worstAllocation, bytes);
                editTimes[i] = (end - start) * (1000d / Stopwatch.Frequency);
                extracts[i] = timing.ExtractMs;
                uploads[i] = timing.UploadMs;
                bakes[i] = timing.BakeMs;
                publishes[i] = timing.PublishMs;
                RenderPipeline.SubmitRenderRequest(view, request);
                yield return null;
                editedFrames[i] = Time.unscaledDeltaTime * 1000d;
            }

            AsyncGPUReadbackRequest readback = AsyncGPUReadback.Request(request.destination, 0, TextureFormat.RGBA32);
            while (!readback.done)
                yield return null;
            if (readback.hasError || !HasScenePixels(readback))
            {
                Debug.LogError("Voxel benchmark could not verify the offscreen frame.");
                Application.Quit(1);
                yield break;
            }

            Debug.Log($"Voxel benchmark | {SystemInfo.processorType} | {SystemInfo.graphicsDeviceName} | {SystemInfo.graphicsDeviceType} | {request.destination.width}x{request.destination.height} offscreen");
            Report("Rendered idle frame", idleFrames);
            Report("Rendered edit frame", editedFrames);
            Report("Edit to collision", editTimes);
            Report("Surface extraction", extracts);
            Report("Mesh upload", uploads);
            Report("Collider bake", bakes);
            Report("Publish", publishes);
            Debug.Log($"Voxel benchmark | {SampleCount} two-chunk edits | managed allocation total {allocated} B, max {worstAllocation} B");
            Application.Quit(0);
        }

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

        private static void Report(string metric, double[] values)
        {
            Array.Sort(values);
            Debug.Log(FormattableString.Invariant(
                $"Voxel benchmark | {metric}: p50 {values[49]:F3}, p95 {values[94]:F3}, p99 {values[98]:F3}, max {values[99]:F3} ms"));
        }
#endif
    }
}
