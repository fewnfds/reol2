using System;
using System.Collections.Generic;
using UnityEngine;

namespace Reol2.World
{
    internal sealed class WorldCaves
    {
        private const int RegionSize = 32768;
        private const int ReachFromRegionCenter = 3200;
        private const int CacheSize = 128;
        private readonly WorldDefinition world;
        private readonly WorldSurface surface;
        private readonly CaveRegion[] regions = new CaveRegion[CacheSize];
        private readonly CavePart[] deepCave;

        internal WorldPosition WestEntrance { get; }
        internal WorldPosition EastEntrance { get; }
        internal WorldPosition Chamber { get; }
        internal WorldPosition DeepChamber { get; }
        internal IReadOnlyList<WorldPosition> Passage { get; }

        internal WorldCaves(WorldDefinition world, WorldSurface surface)
        {
            this.world = world;
            this.surface = surface;
            int east = (world.ContinentCenter.x + 16384) & ~4095;
            int north = world.ContinentCenter.y - 4096;
            var passage = new List<WorldPosition>(9);
            Primary = Build(east, north, world.SeedBits, out var west, out var far, out var chamber, passage);
            Passage = passage;
            WestEntrance = west;
            EastEntrance = far;
            Chamber = chamber;
            DeepChamber = new WorldPosition(east + 2048, -32560, north + 4096);
            deepCave = new[] { new CavePart(DeepChamber, new Vector3(100f, 196f, 72f)) };
        }

        private CavePart[] Primary { get; }

        internal void Collect(Vector3Int first, Vector3Int last, List<CavePart> result)
        {
            result.Clear();
            if (last.y < -63000)
                CollectParts(deepCave, first, last, result);
            if (last.y < WorldSurface.DeepestSeabed * 2 - 1200 || first.y > WorldSurface.HighestTerrain * 2 + 150)
                return;

            if (first.x <= (Chamber.X + ReachFromRegionCenter) * 2 && last.x >= (Chamber.X - ReachFromRegionCenter) * 2
                && first.z <= (Chamber.Z + ReachFromRegionCenter) * 2 && last.z >= (Chamber.Z - ReachFromRegionCenter) * 2)
                CollectParts(Primary, first, last, result);

            // Other cave systems stay inside their owning region's margin. This also bounds cold lookup.
            for (int rz = first.z >> 16; rz <= (last.z >> 16); rz++)
            for (int rx = first.x >> 16; rx <= (last.x >> 16); rx++)
            {
                int cx = rx * RegionSize + RegionSize / 2;
                int cz = rz * RegionSize + RegionSize / 2;
                if (first.x <= (cx + ReachFromRegionCenter) * 2 && last.x >= (cx - ReachFromRegionCenter) * 2
                    && first.z <= (cz + ReachFromRegionCenter) * 2 && last.z >= (cz - ReachFromRegionCenter) * 2)
                    CollectParts(Region(rx, rz, cx, cz), first, last, result);
            }
        }

        private static void CollectParts(CavePart[] parts, Vector3Int first, Vector3Int last, List<CavePart> result)
        {
            foreach (CavePart part in parts)
                if (part.Intersects(first, last))
                    result.Add(part);
        }

        private CavePart[] Region(int rx, int rz, int centerX, int centerZ)
        {
            ulong random = WorldNoise.Hash(rx, rz, world.SeedBits ^ 0xc728378d24b521bfUL);
            int slot = (int)(random & (CacheSize - 1));
            ref CaveRegion region = ref regions[slot];
            var key = new Vector2Int(rx, rz);
            if (region.Parts != null && region.Key == key)
                return region.Parts;

            CavePart[] parts = Array.Empty<CavePart>();
            if ((random >> 11 & 15UL) == 0 && surface.HeightAt(centerX, centerZ) > 210f
                && Math.Abs(centerX - Chamber.X) + Math.Abs(centerZ - Chamber.Z) > 6500)
            {
                int x = centerX + (int)(random >> 24 & 1023) - 512;
                int z = centerZ + (int)(random >> 34 & 1023) - 512;
                parts = Build(x, z, random, out _, out _, out _);
            }
            region = new CaveRegion(key, parts);
            return parts;
        }

        private CavePart[] Build(int x, int z, ulong seed, out WorldPosition west, out WorldPosition east,
            out WorldPosition chamber, List<WorldPosition> passage = null)
        {
            int offset = (int)(WorldNoise.Hash(x, z, seed) & 255) - 127;
            float roof = Math.Min(Math.Min(surface.HeightAt(x - 824, z), surface.HeightAt(x + 824, z)),
                Math.Min(surface.HeightAt(x, z - 400), Math.Min(surface.HeightAt(x, z + 400), surface.HeightAt(x, z))));
            float level = roof - 320f;
            chamber = new WorldPosition(x, level, z);
            var westMouth = new WorldPosition(x - 1176, surface.HeightAt(x - 1176, z + offset) + 6f, z + offset);
            var eastMouth = new WorldPosition(x + 1208, surface.HeightAt(x + 1208, z - offset) + 6f, z - offset);
            var westBend = new WorldPosition(x - 740, level - 18, z + offset * 0.5);
            var eastBend = new WorldPosition(x + 756, level + 12, z - offset * 0.5);
            var westInside = new WorldPosition(x - 438, level + 12, z);
            var eastInside = new WorldPosition(x + 433, level + 14, z - 25);
            west = new WorldPosition(westMouth.X - 42, westMouth.Y + 29, westMouth.Z);
            east = new WorldPosition(eastMouth.X + 42, eastMouth.Y + 29, eastMouth.Z);
            if (passage != null)
            {
                passage.Add(west);
                passage.Add(westMouth);
                passage.Add(westBend);
                passage.Add(westInside);
                passage.Add(chamber);
                passage.Add(eastInside);
                passage.Add(eastBend);
                passage.Add(eastMouth);
                passage.Add(east);
            }
            return new[]
            {
                new CavePart(chamber, new Vector3(604f, 132f, 294f)),
                new CavePart(westMouth, westBend, 28f),
                new CavePart(westBend, westInside, 32f),
                new CavePart(eastMouth, eastBend, 29f),
                new CavePart(eastBend, eastInside, 38f),
                new CavePart(new WorldPosition(x + 162, level, z + 186), new WorldPosition(x + 260, level - 74, z + 430), 17f)
            };
        }

        private readonly struct CaveRegion
        {
            internal readonly Vector2Int Key;
            internal readonly CavePart[] Parts;

            internal CaveRegion(Vector2Int key, CavePart[] parts)
            {
                Key = key;
                Parts = parts;
            }
        }
    }

    internal readonly struct CavePart
    {
        internal const float BlendDistance = 10f;
        private readonly WorldPosition origin;
        private readonly Vector3 radii;
        private readonly Vector3 axis;
        private readonly float radius;
        private readonly float reciprocalLengthSquared;
        private readonly BoundsInt samples;

        internal CavePart(WorldPosition center, Vector3 radii)
        {
            origin = center;
            this.radii = radii;
            axis = Vector3.zero;
            radius = Mathf.Min(radii.x, Mathf.Min(radii.y, radii.z));
            reciprocalLengthSquared = 0f;
            samples = SampleBounds(origin, -radii, radii);
        }

        internal CavePart(WorldPosition from, WorldPosition to, float radius)
        {
            origin = from;
            radii = Vector3.zero;
            axis = new Vector3((float)(to.X - from.X), (float)(to.Y - from.Y), (float)(to.Z - from.Z));
            reciprocalLengthSquared = 1f / axis.sqrMagnitude;
            this.radius = radius;
            Vector3 padding = Vector3.one * radius;
            samples = SampleBounds(origin, Vector3.Min(Vector3.zero, axis) - padding, Vector3.Max(Vector3.zero, axis) + padding);
        }

        internal bool Intersects(Vector3Int first, Vector3Int last) => first.x < samples.xMax && last.x >= samples.xMin
            && first.y < samples.yMax && last.y >= samples.yMin && first.z < samples.zMax && last.z >= samples.zMin;

        internal BoundsInt SampleRange => samples;

        internal Vector2 DistanceRange(Vector3Int first, Vector3Int last)
        {
            Vector3 lo = Offset(first);
            Vector3 hi = Offset(last);
            if (reciprocalLengthSquared != 0f)
            {
                float middle = Distance((lo + hi) * 0.5f);
                float span = (hi - lo).magnitude * 0.5f;
                return new Vector2(Math.Max(middle - span, -radius), middle + span);
            }
            var near = new Vector3(Nearest(lo.x, hi.x) / radii.x, Nearest(lo.y, hi.y) / radii.y, Nearest(lo.z, hi.z) / radii.z);
            var far = new Vector3(Farthest(lo.x, hi.x) / radii.x, Farthest(lo.y, hi.y) / radii.y, Farthest(lo.z, hi.z) / radii.z);
            return new Vector2((near.magnitude - 1f) * radius, (far.magnitude - 1f) * radius);
        }

        private static float Nearest(float lo, float hi) => Mathf.Max(0, Mathf.Max(lo, -hi));
        private static float Farthest(float lo, float hi) => Mathf.Max(Mathf.Abs(lo), Mathf.Abs(hi));

        internal float Carve(Vector3Int sample, float density)
        {
            if (!samples.Contains(sample))
                return density;
            return CarveAtDistance(Distance(Offset(sample)), density);
        }

        internal void Apply(Vector3Int origin, int stride, int width, float[] heights, float[] target)
        {
            int x0 = Math.Max(0, (int)Math.Ceiling((samples.xMin - origin.x) / (double)stride));
            int y0 = Math.Max(0, (int)Math.Ceiling((samples.yMin - origin.y) / (double)stride));
            int z0 = Math.Max(0, (int)Math.Ceiling((samples.zMin - origin.z) / (double)stride));
            int x1 = Math.Min(width, (int)Math.Ceiling((samples.xMax - origin.x) / (double)stride));
            int y1 = Math.Min(width, (int)Math.Ceiling((samples.yMax - origin.y) / (double)stride));
            int z1 = Math.Min(width, (int)Math.Ceiling((samples.zMax - origin.z) / (double)stride));
            if (x0 >= x1 || y0 >= y1 || z0 >= z1)
                return;

            for (int z = z0; z < z1; z++)
            {
                int sz = Math.Clamp(origin.z + z * stride, -2 * WorldDefinition.Extent, 2 * WorldDefinition.Extent);
                float dz = (float)(sz * 0.5 - this.origin.Z);
                for (int y = y0; y < y1; y++)
                {
                    int sy = Math.Clamp(origin.y + y * stride, 2 * WorldDefinition.MinY, 2 * WorldDefinition.MaxY);
                    float yWorld = sy * 0.5f;
                    float dy = (float)(sy * 0.5 - this.origin.Y);
                    int index = x0 + width * (y + z * width);
                    int heightRow = z * width;
                    for (int x = x0; x < x1; x++, index++)
                    {
                        int sx = origin.x + x * stride;
                        var offset = new Vector3((float)(sx * 0.5 - this.origin.X), dy, dz);
                        float distance = Distance(offset);
                        float carved = Math.Max(target[index], CarveAtDistance(distance, yWorld - heights[heightRow + x]));
                        target[index] = ContinentField.NonzeroDensity(carved);
                    }
                }
            }
        }

        private static float CarveAtDistance(float distance, float density)
        {
            if (distance >= BlendDistance || -distance <= density)
                return density;
            if (distance <= 0f)
                return -distance;
            return Mathf.Lerp(density, -distance, 1f - WorldNoise.Ramp(distance / BlendDistance));
        }

        private Vector3 Offset(Vector3Int sample) => new((float)(sample.x * 0.5 - origin.X),
            (float)(sample.y * 0.5 - origin.Y), (float)(sample.z * 0.5 - origin.Z));

        private float Distance(Vector3 offset) => reciprocalLengthSquared != 0f
            ? (offset - axis * Mathf.Clamp01(Vector3.Dot(offset, axis) * reciprocalLengthSquared)).magnitude - radius
            : (new Vector3(offset.x / radii.x, offset.y / radii.y, offset.z / radii.z).magnitude - 1f) * radius;

        private static BoundsInt SampleBounds(WorldPosition center, Vector3 low, Vector3 high)
        {
            // The ellipsoid evaluator scales its normalized distance by the shortest radius.
            // 50 m covers the blend even on its longest axis (604 / 132 * 10 m).
            const int margin = 50;
            var min = new WorldPosition((center.X + low.x - margin) * 2, (center.Y + low.y - margin) * 2,
                (center.Z + low.z - margin) * 2).Cell;
            var max = new WorldPosition((center.X + high.x + margin) * 2, (center.Y + high.y + margin) * 2,
                (center.Z + high.z + margin) * 2).Cell + Vector3Int.one;
            return new BoundsInt(min, max - min);
        }
    }
}
