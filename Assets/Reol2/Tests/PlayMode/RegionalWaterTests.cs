using System;
using System.Collections.Generic;
using NUnit.Framework;
using Reol2.World;
using UnityEngine;
using UnityEngine.Rendering;
using Vector2 = System.Numerics.Vector2;
using Object = UnityEngine.Object;

namespace Reol2.Tests
{
    public sealed class RegionalWaterTests
    {
        private WorldSurface surface;
        private RegionalSurfaceTile builder;
        private RegionalLandforms region;

        [OneTimeSetUp]
        public void GenerateCoast()
        {
            var world = new WorldDefinition(20261121);
            region = new RegionalLandforms(world.SeedBits, 64, out _);
            var field = new ContinentField(world, region);
            surface = field.Surface;
            builder = new RegionalSurfaceTile(field);
        }

        [TestCase(0)]
        [TestCase(20)]
        [TestCase(45)]
        public void APlanarSlopeDrainsWithoutMakingRiverChannels(int heading)
        {
            float angle = heading * Mathf.Deg2Rad;
            float east = Mathf.Cos(angle), north = Mathf.Sin(angle);
            RegionalRivers rivers = RiversOn((x, z) => 2f * (x * east + (z - 32f) * north));
            Assert.That(rivers.Reaches, Is.Empty);
        }

        [TestCase(40)]
        [TestCase(80)]
        public void ResamplingASlopedChannelKeepsItsBedAndWaterTogether(int spacing)
        {
            var reaches = new RiverReach[240 / spacing];
            for (int i = 0; i < reaches.Length; i++)
                reaches[i] = new RiverReach(Section(i * spacing), Section((i + 1) * spacing));
            var river = new RegionalRivers(reaches, 1000);
            for (int x = 32; x <= 208; x++)
            {
                float level = Section(x).Level;
                foreach (int lateral in new[] { -24, 0, 24 })
                {
                    var point = new Vector2(x, lateral);
                    float bed = river.Sculpt(point, level + 2f);
                    Assert.That(river.WaterAt(point, bed, out _), Is.EqualTo(level).Within(0.001f));
                    Assert.That(level - bed, Is.EqualTo(3f * (1f - lateral * lateral / 900f)).Within(0.001f));
                }
            }

            static RiverSection Section(int x) => new(new Vector2(x, 0), 102f - x * 0.05f,
                100f - x * 0.05f, 30f, 3f);
        }

        [Test]
        public void ASharpBendSharesItsCrossSectionAcrossBothBanks()
        {
            var before = new RiverSection(new Vector2(-80f, 0), 107f, 105f, 27f, 3f, Vector2.UnitX);
            var join = new RiverSection(Vector2.Zero, 102f, 100f, 27f, 3f, Vector2.Normalize(Vector2.One));
            var after = new RiverSection(new Vector2(0, 80f), 97f, 95f, 27f, 3f, Vector2.UnitY);
            var river = new RegionalRivers(new[] { new RiverReach(before, join), new RiverReach(join, after) }, 1000);
            foreach (float bank in new[] { -15f, 0f, 15f })
            {
                var first = new Vector2(bank - 0.01f, -bank - 0.01f);
                var second = new Vector2(bank + 0.01f, -bank + 0.01f);
                float bedBefore = river.Sculpt(first, 110f), bedAfter = river.Sculpt(second, 110f);
                float waterBefore = river.WaterAt(first, bedBefore, out _), waterAfter = river.WaterAt(second, bedAfter, out _);
                Assert.That(waterBefore - bedBefore, Is.Positive);
                Assert.That(waterAfter - bedAfter, Is.Positive);
                Assert.That(bedBefore, Is.EqualTo(bedAfter).Within(0.02f));
                Assert.That(waterBefore, Is.EqualTo(waterAfter).Within(0.02f));
            }
        }

        [TestCase(10028f, 11680f, 180f)]
        [TestCase(9330f, 8900f, 235f)]
        public void OpenValleyReachesDoNotMakeAbruptTurns(float x, float z, float radius)
        {
            var upstream = new Dictionary<Vector2, Vector2>();
            var confluences = new HashSet<Vector2>();
            foreach (RiverReach reach in region.Rivers.Reaches)
                if (!upstream.TryAdd(reach.Downstream.Center, reach.Upstream.Center))
                    confluences.Add(reach.Downstream.Center);

            int checkedStations = 0;
            foreach (RiverReach reach in region.Rivers.Reaches)
            {
                Vector2 station = reach.Upstream.Center;
                WorldPosition position = surface.Place(station);
                if (Math.Abs(position.X - x) > radius || Math.Abs(position.Z - z) > radius
                    || confluences.Contains(station) || !upstream.TryGetValue(station, out Vector2 previous))
                    continue;
                Vector2 incoming = Vector2.Normalize(station - previous);
                Vector2 outgoing = Vector2.Normalize(reach.Downstream.Center - station);
                Assert.That(Vector2.Dot(incoming, outgoing), Is.GreaterThan(MathF.Cos(MathF.PI / 4f)),
                    $"Abrupt river turn at {position.X:F1}, {position.Z:F1}");
                checkedStations++;
            }
            Assert.That(checkedStations, Is.GreaterThanOrEqualTo(5));
        }

        [Test]
        public void ASteepGorgeKeepsItsTurnAlongTheValleyFloor()
        {
            static float ValleyZ(float x) => 32f + MathF.Abs(x - 32f);
            RegionalRivers rivers = RiversOn(
                (x, z) => (x - 5f) * 10f + 180f * MathF.Abs(z - ValleyZ(x)),
                (x, z) => new RegionalClimate(15f, z == ValleyZ(x) ? 4000f : 0f, 90f));

            RiverReach[] intoBend = Array.FindAll(rivers.Reaches,
                reach => reach.Downstream.Center.LengthSquared() < 25f);
            RiverReach[] outOfBend = Array.FindAll(rivers.Reaches,
                reach => reach.Upstream.Center.LengthSquared() < 25f);
            Assert.That(intoBend.Length, Is.EqualTo(1));
            Assert.That(outOfBend.Length, Is.EqualTo(1));
            Vector2 above = intoBend[0].Upstream.Center, turn = intoBend[0].Downstream.Center, below = outOfBend[0].Downstream.Center;
            Assert.That(Vector2.Dot(Vector2.Normalize(turn - above), Vector2.Normalize(below - turn)), Is.LessThan(0.3f));
            foreach (Vector2 middle in new[] { (above + turn) * 0.5f, (turn + below) * 0.5f })
                Assert.That(MathF.Abs(middle.Y - MathF.Abs(middle.X)), Is.LessThan(5f));
        }

        [Test]
        public void BothTributariesMeetTheReceivingWaterLevel()
        {
            var left = new RiverSection(new Vector2(-64f, -32f), 105f, 103f, 10f, 2f);
            var right = new RiverSection(new Vector2(-64f, 32f), 106f, 104f, 13f, 2.5f);
            var join = new RiverSection(Vector2.Zero, 102f, 100f, 20f, 3f, Vector2.UnitX);
            var lower = new RiverSection(new Vector2(64f, 0), 99f, 97f, 21f, 3f);
            var river = new RegionalRivers(new[] { new RiverReach(left, join), new RiverReach(right, join),
                new RiverReach(join, lower) }, 1000);
            foreach (var point in new[] { new Vector2(-0.08f, -2f), new Vector2(-0.08f, 2f), new Vector2(0.08f, 0f) })
            {
                float bed = river.Sculpt(point, 110f);
                float water = river.WaterAt(point, bed, out _);
                Assert.That(water, Is.EqualTo(100f).Within(0.02f));
                Assert.That(water - bed, Is.GreaterThan(2.9f));
            }
        }

        [Test]
        public void TidalBanksDoNotCutDryHolesIntoTheSea()
        {
            var start = new RiverSection(new Vector2(-64f, 0), 1f, 0f, 15f, 3f);
            var mouth = new RiverSection(Vector2.Zero, -1f, 0f, 15f, 3f);
            var river = new RegionalRivers(new[] { new RiverReach(start, mouth, endCap: true) }, 1000);
            var shelf = new Vector2(-32f, 21f);
            Assert.That(river.WaterAt(shelf, -1f, out _), Is.Zero);
        }

        [Test]
        public void APerpendicularTributarySharesTheWaterAtTheInsideCorner()
        {
            var branch = new RiverSection(new Vector2(0f, -64f), 106f, 104f, 14f, 3f);
            var junction = new RiverSection(Vector2.Zero, 102f, 100f, 20f, 3f, Vector2.UnitX);
            var downstream = new RiverSection(new Vector2(64f, 0f), 99f, 97f, 20f, 3f);
            var river = new RegionalRivers(new[] { new RiverReach(branch, junction, endCap: true),
                new RiverReach(junction, downstream) }, 1000);
            var inside = new Vector2(-2f, 2f);
            float bed = river.Sculpt(inside, 104f);
            Assert.That(bed, Is.LessThan(98f));
            Assert.That(river.WaterAt(inside, bed, out _), Is.EqualTo(junction.Level).Within(0.01f));
        }

        [Test]
        public void AValleyWithASillKeepsItsChannelBelowTheAdjacentBanks()
        {
            static float Height(float x, float z) => 4f * x + 20f * MathF.Abs(z - 32f)
                + 13f * MathF.Max(0f, 1f - MathF.Abs(x - 16f));
            RegionalRivers rivers = RiversOn(Height);
            Assert.That(rivers.Reaches.Length, Is.GreaterThan(8));
            int checkedSections = 0;
            foreach (RiverReach reach in rivers.Reaches)
            {
                Assert.That(reach.Downstream.Level, Is.LessThanOrEqualTo(reach.Upstream.Level));
                if (reach.Upstream.HalfWidth < 8f || reach.Downstream.HalfWidth < 8f)
                    continue;
                Vector2 middle = (reach.Upstream.Center + reach.Downstream.Center) * 0.5f;
                float ground = rivers.Sculpt(middle, Height(middle.X / 250f + 32f, middle.Y / 250f + 32f));
                Assert.That(rivers.WaterAt(middle, ground, out _), Is.GreaterThan(ground + 0.5f));
                if (reach.Downstream.Level > 15f)
                {
                    Vector2 axis = reach.Upstream.Tangent;
                    Vector2 across = new(-axis.Y, axis.X);
                    foreach (float sign in new[] { -1f, 1f })
                    {
                        float crest = float.NegativeInfinity;
                        for (float offset = reach.Upstream.HalfWidth; offset <= reach.Upstream.HalfWidth + 40f; offset += 4f)
                        {
                            Vector2 bank = reach.Upstream.Center + across * sign * offset;
                            crest = MathF.Max(crest, Height(bank.X / 250f + 32f, bank.Y / 250f + 32f));
                        }
                        Assert.That(crest, Is.GreaterThanOrEqualTo(reach.Upstream.Level - 0.01f));
                    }
                }
                checkedSections++;
            }
            Assert.That(checkedSections, Is.GreaterThan(8));
        }

        [Test]
        public void HighCoastalTilesKeepTheVisibleRiverSpines()
        {
            var tiles = new Dictionary<Vector2, WaterMesh>();
            int tested = 0;
            foreach (RiverReach reach in region.Rivers.Reaches)
            for (int sample = 1; sample <= 3; sample++)
            {
                WorldPosition position = surface.Place(Vector2.Lerp(reach.Upstream.Center, reach.Downstream.Center, sample * 0.25f));
                float x = (float)position.X, z = (float)position.Z;
                if (Math.Abs(x + 13551f) > 4500f || Math.Abs(z + 1583f) > 4500f
                    || surface.WaterAt(x, z) - surface.HeightAt(x, z) < 0.8f)
                    continue;
                var key = new Vector2(MathF.Floor(x / 4096f), MathF.Floor(z / 4096f));
                if (!tiles.TryGetValue(key, out WaterMesh tile))
                    tiles.Add(key, tile = BuildAt(x, z, 256));
                Assert.That(tile.Coverage(x, z, out _), Is.Positive, $"Missing far water at {x:F1}, {z:F1}");
                tested++;
            }
            Assert.That(tested, Is.GreaterThan(100));
        }

        private static RegionalRivers RiversOn(Func<float, float, float> height, Func<int, int, RegionalClimate> climateAt = null)
        {
            const int side = 65, step = RegionalLandforms.DrainageSpacing;
            const int size = (side - 1) * step;
            var ground = new float[side * side];
            var climate = new RegionalClimate[ground.Length];
            for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
            {
                int i = x + z * side;
                ground[i] = height(x, z);
                climate[i] = climateAt?.Invoke(x, z) ?? new RegionalClimate(15f, 1600f, 90f);
            }
            var drainage = new RegionalDrainage(ground, climate, side, step, new LandformProfile(20261121, 256000));
            return new RegionalRivers(drainage, size, step, hasLake: false,
                (x, z) => height(x / step + 32f, z / step + 32f));
        }

        [Test]
        public void HeadwatersTaperAndShortCoastalCatchmentsRetainOnlyStrongStreams()
        {
            var incoming = new HashSet<Vector2>();
            foreach (RiverReach reach in region.Rivers.Reaches)
                incoming.Add(reach.Downstream.Center);

            int heads = 0;
            foreach (RiverReach reach in region.Rivers.Reaches)
            {
                if (incoming.Contains(reach.Upstream.Center))
                    continue;
                heads++;
                Assert.That(reach.Upstream.HalfWidth, Is.Zero);
                Assert.That(reach.Upstream.Depth, Is.Zero);
                Assert.That(reach.Downstream.HalfWidth, Is.GreaterThan(0f));
                Assert.That(reach.Upstream.Level, Is.GreaterThanOrEqualTo(reach.Downstream.Level));
            }
            Assert.That(heads, Is.GreaterThan(30));

            Assert.That(Array.Exists(region.Rivers.Reaches, reach => reach.Downstream.Center == new Vector2(-27500, 6750)), Is.False,
                "The weak one-edge outlet from the fourth screenshot must not generate a river.");
            RiverReach[] reaches = region.Rivers.Reaches;
            var lengths = new Dictionary<Vector2, float>();
            var upstreams = new HashSet<Vector2>();
            for (int i = reaches.Length - 1; i >= 0; i--)
            {
                RiverReach reach = reaches[i];
                Vector2 from = reach.Upstream.Center, to = reach.Downstream.Center;
                lengths.TryGetValue(from, out float upstreamLength);
                lengths.TryGetValue(to, out float existingLength);
                lengths[to] = MathF.Max(existingLength, upstreamLength + Vector2.Distance(from, to));
                upstreams.Add(from);
            }
            Assert.That(Array.Exists(reaches, reach => reach.Downstream.Level == 0f
                && !upstreams.Contains(reach.Downstream.Center) && lengths[reach.Downstream.Center] <= 1500f), Is.True,
                "The area still contains shorter coastal creeks where the terrain supports them.");
        }

        [TestCase(-7097.3f, -23391.2f, 283.4987f)]
        [TestCase(-20199.3f, -11623.1f, 0f)]
        public void TheReceivingWaterIsDrawnOnceAtTheSwimmingLevel(float x, float z, float expected)
        {
            WaterMesh near = BuildAt(x, z, 8);
            Assert.That(near.Coverage(x, z, out float level), Is.EqualTo(1));
            Assert.That(level, Is.EqualTo(expected).Within(0.01f));
            Assert.That(surface.WaterAt(x, z), Is.EqualTo(level).Within(0.001f));
            Assert.That(surface.HeightAt(x, z), Is.LessThan(level));
        }

        [Test]
        public void SeaLevelEstuariesRemainWetInTheTerrainAndWaterMeshes()
        {
            var hasOutlet = new HashSet<Vector2>();
            foreach (RiverReach reach in region.Rivers.Reaches)
                hasOutlet.Add(reach.Upstream.Center);

            int checkedMouths = 0;
            foreach (RiverReach reach in region.Rivers.Reaches)
            {
                if (reach.Downstream.Level != 0f || reach.Downstream.HalfWidth < 12f
                    || hasOutlet.Contains(reach.Downstream.Center))
                    continue;

                WorldPosition outlet = surface.Place(reach.Downstream.Center);
                float x = (float)outlet.X + 0.13f, z = (float)outlet.Z + 0.19f;
                Assert.That(surface.HeightAt(x, z), Is.LessThan(-0.8f), $"Bed at ({x}, {z})");
                Assert.That(surface.WaterAt(x, z), Is.EqualTo(0f).Within(0.12f), $"Swim level at ({x}, {z})");
                foreach (int step in new[] { 8, 128 })
                {
                    WaterMesh tile = BuildAt(x, z, step);
                    Assert.That(tile.Coverage(x, z, out float level), Is.Positive,
                        $"Missing estuary water at ({x}, {z}) at {step} m base spacing");
                    Assert.That(level, Is.EqualTo(0f).Within(0.12f));
                }
                if (++checkedMouths == 6)
                    break;
            }
            Assert.That(checkedMouths, Is.GreaterThanOrEqualTo(4));
        }

        [Test]
        public void ASubmergedCoastalTileCanJoinTheSharedSeaMesh()
        {
            WaterMesh sea = BuildAt(-576f, 11584f, 8);
            Assert.That(builder.SharedWaterLevel, Is.EqualTo(0f));
            Assert.That(sea.Coverage(-575.7f, 11584.1f, out float level), Is.Positive);
            Assert.That(level, Is.Zero);

            BuildAt(-470f, 11099f, 8);
            Assert.That(builder.SharedWaterLevel, Is.Null, "A mixed shoreline still needs its clipped water mesh.");
        }

        [Test]
        public void ADeepLakeTileCanJoinSharedWaterAtItsOwnElevation()
        {
            const float west = -8192f, south = -23552f;
            const int step = 64, width = step * 16;
            BuildAt(west + 1f, south + 1f, step);
            Assert.That(builder.SharedWaterLevel, Is.EqualTo(region.LakeLevel));
            float level = builder.SharedWaterLevel.Value;

            var root = new GameObject("Shared water test");
            var shared = new OceanSurface(root.transform, null);
            try
            {
                var frame = new WorldFrame();
                frame.CenterOn(new WorldPosition(west - 512f, 1536f, south));
                float x = west - frame.Origin.x, z = south - frame.Origin.z;
                OceanSurface.AppendQuad(shared.Vertices, x, z, x + width, z + width, level);
                shared.Publish(frame);
                var water = root.GetComponentInChildren<MeshFilter>();
                Assert.That(water.sharedMesh.GetIndexCount(0), Is.EqualTo(6));

                frame.CenterOn(new WorldPosition(west + width, 2560f, south - width));
                shared.Reposition(frame);
                foreach (Vector3 vertex in water.sharedMesh.vertices)
                {
                    WorldPosition point = frame.ToWorld(water.transform.TransformPoint(vertex));
                    Assert.That(point.X, Is.EqualTo(west).Or.EqualTo(west + width));
                    Assert.That(point.Y, Is.EqualTo(level).Within(0.001));
                    Assert.That(point.Z, Is.EqualTo(south).Or.EqualTo(south + width));
                }
                foreach (UnityEngine.Vector2 uv in water.sharedMesh.uv)
                    Assert.That(uv, Is.EqualTo(new UnityEngine.Vector2(OceanSurface.DeepWaterDepth, level)));
            }
            finally
            {
                shared.Release();
                Object.DestroyImmediate(root);
            }

            WaterMesh shore = BuildAt(west + width + 1f, south + 1f, step);
            Assert.That(builder.SharedWaterLevel, Is.Null);
            Assert.That(shore.Coverage(west + width + 1f, south + 0.5f * width, out float shoreLevel), Is.Positive);
            Assert.That(shoreLevel, Is.EqualTo(level).Within(0.001f));
            Assert.That(shore.Coverage(west + 1.75f * width, south + 0.5f * width, out _), Is.Zero);
        }

        [Test]
        public void LocalDetailPreservesInlandChannelsAndTheSameCoastAtHigherLod()
        {
            WaterMesh distant = BuildAt(-11000f, -14900f, 128);
            int sampled = 0, missing = 0;
            foreach (RiverReach reach in region.Rivers.Reaches)
            for (int i = 1; i < 10; i++)
            {
                Vector2 along = Vector2.Lerp(reach.Upstream.Center, reach.Downstream.Center, i * 0.1f);
                WorldPosition point = surface.Place(along);
                float x = (float)point.X + 0.07f, z = (float)point.Z + 0.13f;
                if (!distant.Contains(x, z) || surface.WaterAt(x, z) - surface.HeightAt(x, z) < 0.8f)
                    continue;
                sampled++;
                if (distant.Coverage(x, z, out _) == 0)
                    missing++;
            }
            Assert.That(sampled, Is.GreaterThan(25));
            Assert.That(missing, Is.LessThanOrEqualTo(sampled * 0.05f));

            const float coastX = -13548f, coastZ = 829.7f;
            float near = FindShore(BuildAt(coastX, coastZ, 32), coastZ);
            float far = FindShore(BuildAt(coastX, coastZ, 128), coastZ);
            Assert.That(far, Is.EqualTo(near).Within(4f));
        }

        private static float FindShore(WaterMesh water, float z)
        {
            float sea = -13650f, land = -13350f;
            Assert.That(water.Coverage(sea, z, out _), Is.Positive);
            Assert.That(water.Coverage(land, z, out _), Is.Zero);
            for (int i = 0; i < 13; i++)
            {
                float mid = (sea + land) * 0.5f;
                if (water.Coverage(mid, z, out _) > 0)
                    sea = mid;
                else
                    land = mid;
            }
            return (sea + land) * 0.5f;
        }

        private WaterMesh BuildAt(float x, float z, int step)
        {
            int width = 16 * step;
            int west = Mathf.FloorToInt(x / width) * width, south = Mathf.FloorToInt(z / width) * width;
            var ground = new Mesh();
            var water = new Mesh { indexFormat = IndexFormat.UInt32 };
            try
            {
                builder.Begin(west, south, step, 16);
                while (!builder.Prepare(128)) { }
                builder.Emit(0, builder.CellCount);
                int elevation = builder.Publish(ground);
                builder.PublishWater(water);
                return new WaterMesh(water.vertices, west, elevation, south, width);
            }
            finally
            {
                Object.DestroyImmediate(ground);
                Object.DestroyImmediate(water);
            }
        }

        private readonly struct WaterMesh
        {
            private readonly Vector3[] triangles;
            private readonly int x, y, z, width;

            internal WaterMesh(Vector3[] triangles, int x, int y, int z, int width)
            {
                this.triangles = triangles;
                this.x = x;
                this.y = y;
                this.z = z;
                this.width = width;
            }

            internal bool Contains(float east, float north) => east >= x && east < x + width && north >= z && north < z + width;

            internal int Coverage(float east, float north, out float level)
            {
                var point = new Vector2(east - x, north - z);
                int hits = 0;
                level = float.NegativeInfinity;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    Vector2 u = new(b.x - a.x, b.z - a.z), v = new(c.x - a.x, c.z - a.z);
                    Vector2 offset = point - new Vector2(a.x, a.z);
                    float determinant = Cross(u, v);
                    if (MathF.Abs(determinant) < 0.00001f)
                        continue;
                    float s = Cross(offset, v) / determinant, t = Cross(u, offset) / determinant;
                    if (s < 0f || t < 0f || s + t > 1f)
                        continue;
                    hits++;
                    level = MathF.Max(level, y + a.y + (b.y - a.y) * s + (c.y - a.y) * t);
                }
                return hits;
            }

            private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
        }
    }
}
