using UnityEngine;

namespace Reol2.World
{
    internal readonly struct ContinentTravelPath
    {
        private static readonly Vector3 BelowFeet = new(0.53f, 0.56f, 0.53f);
        private static readonly Vector3 AboveFeet = new(0.53f, 2.08f, 0.53f);
        private readonly Vector3 origin;
        private readonly Vector3 step;
        private readonly Vector3Int anchor;

        internal int SegmentCount { get; }
        internal float SegmentLength { get; }

        internal ContinentTravelPath(Vector3 origin, Vector3 displacement, Vector3Int anchor)
        {
            this.origin = origin;
            this.anchor = anchor;
            float length = displacement.magnitude;
            SegmentCount = Mathf.Max(1, Mathf.CeilToInt(length / (ContinentField.ChunkSize * 0.5f)));
            step = displacement / SegmentCount;
            SegmentLength = length / SegmentCount;
        }

        internal BoundsInt SegmentPages(int segment) =>
            SweptPages(origin + step * segment, origin + step * (segment + 1), anchor);

        internal static BoundsInt SweptPages(Vector3 fromFeet, Vector3 toFeet, Vector3Int anchor)
        {
            // Include both the controller envelope and the contact below its feet.
            Vector3Int min = ContinentField.ChunkAt(Vector3.Min(fromFeet, toFeet) - BelowFeet);
            Vector3Int max = ContinentField.ChunkAt(Vector3.Max(fromFeet, toFeet) + AboveFeet);
            return new BoundsInt(min + anchor, max - min + Vector3Int.one);
        }
    }
}
