using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Reol2.World
{
    internal sealed class ContinentStreamProbe
    {
        internal enum Stage { Schedule, Sample, Extract, View, Upload, Bake, Publish, Horizon, Count }

        private readonly long[] ticks = new long[(int)Stage.Count];
        private readonly Dictionary<Vector3Int, int> firstRequested = new(512);
        private readonly List<Vector3Int> canceled = new(512);
        private readonly HashSet<Vector3Int> missingPath = new(64);
        private int step;

        internal int CreatedViews { get; set; }
        internal int ReusedViews { get; set; }
        internal int ReleasedViews { get; set; }
        internal int SurfacePages { get; set; }
        internal int PagesWithoutSurface { get; set; }
        internal int CanceledRequests { get; private set; }
        internal int OldestPathWait { get; private set; }
        internal int OldestCompletion { get; private set; }
        internal int MissingPathPages => missingPath.Count;
        internal int OutsideWindowPathPages { get; private set; }
        internal float ReadyDistance { get; private set; }

        internal void BeginStep()
        {
            step++;
            Array.Clear(ticks, 0, ticks.Length);
            OldestCompletion = 0;
        }

        internal void SetRequests(Dictionary<Vector3Int, float> requests)
        {
            canceled.Clear();
            foreach (Vector3Int key in firstRequested.Keys)
                if (!requests.ContainsKey(key))
                    canceled.Add(key);
            foreach (Vector3Int key in canceled)
                firstRequested.Remove(key);
            CanceledRequests += canceled.Count;
            foreach (Vector3Int key in requests.Keys)
                firstRequested.TryAdd(key, step);
        }

        internal void BuildCompleted(Vector3Int chunk)
        {
            if (firstRequested.Remove(chunk, out int requested))
                OldestCompletion = Math.Max(OldestCompletion, step - requested);
        }

        internal void ObservePath<T>(ContinentTravelPath path, Dictionary<Vector3Int, T> ready)
        {
            missingPath.Clear();
            OldestPathWait = 0;
            ReadyDistance = 0f;
            OutsideWindowPathPages = 0;
            for (int index = 0; index < path.SegmentCount; index++)
            {
                foreach (Vector3Int chunk in path.SegmentPages(index).allPositionsWithin)
                {
                    if (ready.ContainsKey(chunk) || !missingPath.Add(chunk))
                        continue;
                    if (firstRequested.TryGetValue(chunk, out int requested))
                        OldestPathWait = Math.Max(OldestPathWait, step - requested);
                    else
                        OutsideWindowPathPages++;
                }
                if (missingPath.Count == 0)
                    ReadyDistance = (index + 1) * path.SegmentLength;
            }
        }

        internal void ObserveOpenPath(ContinentTravelPath path)
        {
            missingPath.Clear();
            OldestPathWait = 0;
            OutsideWindowPathPages = 0;
            ReadyDistance = path.SegmentCount * path.SegmentLength;
        }

        internal static long Start(ContinentStreamProbe probe) => probe == null ? 0 : Stopwatch.GetTimestamp();

        internal void End(Stage stage, long start) => ticks[(int)stage] += Stopwatch.GetTimestamp() - start;

        internal double Milliseconds(Stage stage) => ticks[(int)stage] * (1000d / Stopwatch.Frequency);
    }
}
