using System;
using UnityEngine;

namespace Reol2.World
{
    public readonly struct WorldPosition
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public WorldPosition(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        internal Vector3Int Cell => new((int)Math.Floor(X), (int)Math.Floor(Y), (int)Math.Floor(Z));

        internal Vector3Int Page => new((int)Math.Floor(X / ContinentField.ChunkSize),
                                        (int)Math.Floor(Y / ContinentField.ChunkSize),
                                        (int)Math.Floor(Z / ContinentField.ChunkSize));
    }

    internal sealed class WorldFrame
    {
        private const int Alignment = 512;
        private const float ShiftAt = 2048f;

        internal Vector3Int Origin { get; private set; }
        internal Vector3Int PageOrigin => Origin / ContinentField.ChunkSize;

        internal Vector3 ToLocal(WorldPosition point) =>
            new((float)(point.X - Origin.x), (float)(point.Y - Origin.y), (float)(point.Z - Origin.z));

        internal WorldPosition ToWorld(Vector3 local) =>
            new((double)Origin.x + local.x, (double)Origin.y + local.y, (double)Origin.z + local.z);

        internal void CenterOn(WorldPosition point) => Origin = new Vector3Int(
            Align(point.X), Align(point.Y), Align(point.Z));

        internal bool NeedsShift(Vector3 local) =>
            Mathf.Abs(local.x) >= ShiftAt || Mathf.Abs(local.y) >= ShiftAt || Mathf.Abs(local.z) >= ShiftAt;

        private static int Align(double coordinate) => (int)Math.Floor(coordinate / Alignment) * Alignment;
    }
}
