using System.Collections;
using NUnit.Framework;
using Reol2.Voxels;
using Reol2.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Reol2.Tests
{
    public sealed class LargeWorldTests
    {
        private ContinentTerrain terrain;
        private ContinentExplorer player;

        [UnitySetUp]
        public IEnumerator OpenLargeWorld()
        {
            yield return SceneManager.LoadSceneAsync("LargeWorld", LoadSceneMode.Single);
            yield return null;
            terrain = Object.FindFirstObjectByType<ContinentTerrain>();
            player = Object.FindFirstObjectByType<ContinentExplorer>();
            player.enabled = false;
            Assert.That(terrain.IsLargeWorld, Is.True);
        }

        [Test]
        public void EachFarCornerHasEditableCollisionIncludingTheLastGridCell()
        {
            const double inside = WorldDefinition.Extent - 0.75;
            foreach (int sideX in new[] { -1, 1 })
            foreach (int sideZ in new[] { -1, 1 })
            {
                var position = new WorldPosition(sideX * inside, 0, sideZ * inside);
                float height = terrain.SurfaceHeight(position);
                var target = new WorldPosition(position.X, height + 1.25, position.Z);
                Assert.That(player.EnterAt(target), Is.True);
                Vector3 eye = player.transform.position;
                Assert.That(terrain.IsOcean(eye), Is.True);
                Assert.That(terrain.Pick(new Ray(eye, Vector3.down), eye, out Vector3Int cell, out _), Is.True);
                Assert.That(cell.x, Is.EqualTo(position.Cell.x));
                Assert.That(cell.z, Is.EqualTo(position.Cell.z));
                double original = Floor(eye);
                Assert.That(terrain.TryEdit(cell, fill: false), Is.True);
                double excavated = Floor(eye);
                Assert.That(excavated, Is.LessThan(original - 0.01));

                Assert.That(player.EnterAt(terrain.WorldLanding), Is.True);
                Assert.That(player.EnterAt(target), Is.True);
                Assert.That(Floor(player.transform.position), Is.EqualTo(excavated).Within(0.002));
                Assert.That(terrain.TryEdit(cell, fill: true), Is.True);
                Assert.That(Floor(player.transform.position), Is.GreaterThan(excavated + 0.01));
                Assert.That(player.EnterAt(new WorldPosition(position.X, original - 5, position.Z)), Is.False);
            }
        }

        [Test]
        public void HorizontalAndVerticalOriginShiftsPreservePositionAndWater()
        {
            const double e = WorldDefinition.Extent - 8192 - 0.5;
            var high = new WorldPosition(e, WorldDefinition.MaxY - 8, -e);
            Assert.That(player.EnterAt(high), Is.True);
            int shifts = player.OriginShifts;
            for (int i = 0; i < 1000; i++)
                player.MoveWithVelocity(new Vector3(-50, -57, 47), 0.05f);
            WorldPosition after = player.WorldFeet;
            Assert.That(player.OriginShifts, Is.GreaterThan(shifts));
            Assert.That(after.X, Is.EqualTo(high.X - 2500).Within(0.08));
            Assert.That(after.Y, Is.EqualTo(high.Y - 2850).Within(0.12));
            Assert.That(after.Z, Is.EqualTo(high.Z + 2350).Within(0.08));
            Assert.That(terrain.ActivePages, Is.Zero, "The high-altitude unedited volume is implicit.");
            Assert.That(terrain.HorizonTiles, Is.InRange(1, 224));
            Assert.That(player.LimitedMoves, Is.Zero);
            Assert.That(player.EnterAt(new WorldPosition(e, WorldDefinition.MaxY, -e)), Is.False);
            Assert.That(player.EnterAt(new WorldPosition(e, WorldDefinition.MinY - 1, -e)), Is.False);

            float seaBed = terrain.SurfaceHeight(high);
            var sea = new WorldPosition(high.X, -1.5, high.Z);
            Assert.That(seaBed, Is.LessThan(-2250f));
            Assert.That(player.EnterAt(sea), Is.True);
            shifts = player.OriginShifts;
            for (int i = 0; i < 850; i++)
                player.MoveWithVelocity(Vector3.down * 64f, 0.05f);
            Assert.That(player.OriginShifts, Is.GreaterThan(shifts));
            Assert.That(terrain.IsOcean(player.transform.position + Vector3.up * 0.7f), Is.True);
            Assert.That(player.WorldFeet.Y, Is.LessThan(-2200));
            Assert.That(terrain.ActivePages, Is.LessThan(1500));
        }

        [Test]
        public void FarArrivalDiscardsPendingPagesFromThePreviousLocation()
        {
            Assert.That(player.EnterAt(terrain.WorldLanding), Is.True);
            terrain.Stream(player.transform.position, Vector3.zero);
            Assert.That(terrain.PendingPages, Is.Positive);

            Assert.That(player.EnterAt(new WorldPosition(120000.5, 20000.5, 120000.5)), Is.True);
            Assert.That(terrain.PendingPages, Is.Zero);
        }

        [Test]
        public void AnEditInTheSkyKeepsLocalCollisionAndDoesNotLoadDistantAirPages()
        {
            var cell = new Vector3Int(3000, 11000, 3000);
            var eye = new WorldPosition(cell.x + 0.5, cell.y + 2, cell.z + 0.5);
            Assert.That(terrain.TryEdit(cell, fill: true), Is.True);
            Assert.That(player.EnterAt(eye), Is.True);
            terrain.Stream(player.transform.position, Vector3.zero);
            Assert.That(Floor(player.transform.position), Is.EqualTo(cell.y + 1).Within(0.13));
            Assert.That(terrain.ActivePages, Is.GreaterThan(0));

            Assert.That(player.EnterAt(new WorldPosition(120000, cell.y + 2, 120000)), Is.True);
            terrain.Stream(player.transform.position, Vector3.zero);
            Assert.That(terrain.ActivePages, Is.Zero);
        }

        [Test]
        public void CavePassageHasContinuousCollisionAndBothEntrancesConnect()
        {
            var path = terrain.CavePassage;
            Assert.That(player.EnterAt(path[0]), Is.True);
            int limits = player.LimitedMoves;
            for (int i = 1; i < path.Count; i++)
                FlyTo(path[i]);
            Assert.That(player.WorldFeet.X - path[0].X, Is.GreaterThan(2300));
            Assert.That(player.LimitedMoves - limits, Is.Zero);
            Assert.That(player.EnterAt(terrain.CaveChamber), Is.True);
            for (int i = 0; i < 260; i++)
                terrain.Stream(player.transform.position, Vector3.zero);
            Assert.That(terrain.CaveTiles, Is.InRange(16, 232));

            WorldPosition deep = terrain.DeepChamber;
            var chamberBottom = new WorldPosition(deep.X + 0.5, deep.Y - 194, deep.Z + 0.5);
            Assert.That(player.EnterAt(chamberBottom), Is.True);
            Vector3 eye = player.transform.position;
            double previous = Floor(eye);
            Assert.That(previous, Is.LessThan(-32740));
            Assert.That(terrain.Pick(new Ray(eye, Vector3.down), eye, out var dig, out _), Is.True);
            Assert.That(terrain.TryEdit(dig, fill: false), Is.True);
            double changed = Floor(eye);
            Assert.That(changed, Is.LessThan(previous - 0.01));
            Assert.That(player.EnterAt(terrain.WorldLanding), Is.True);
            Assert.That(player.EnterAt(chamberBottom), Is.True);
            Assert.That(Floor(player.transform.position), Is.EqualTo(changed).Within(0.002));
        }

        [Test]
        public void SeededSurfaceAndCaveSamplesAgreeAcrossSharedHalo()
        {
            var world = new WorldDefinition(14376);
            var first = new ContinentField(world);
            var second = new ContinentField(world);
            var other = new ContinentField(new WorldDefinition(14481));
            Assert.That(first.Landing.Z, Is.EqualTo(second.Landing.Z));
            Assert.That(first.Landing.Z, Is.Not.EqualTo(other.Landing.Z));
            var point = first.Caves.Chamber;
            float wallX = 0.82f * 604f;
            var wall = new WorldPosition(point.X + wallX, point.Y - 60, point.Z + 110);
            var buffer = new float[DensityVolume.SampleWidth * DensityVolume.SampleWidth * DensityVolume.SampleWidth];
            var compared = new float[buffer.Length];
            var reverse = new float[buffer.Length];
            int mixedPages = 0;
            for (int dx = -2; dx <= 2; dx++)
            for (int dy = -2; dy <= 2; dy++)
            {
                Vector3Int key = wall.Page + new Vector3Int(dx, dy, 0);
                PageContent expected = second.CopySurfaceSamples(key + Vector3Int.right, compared);
                PageContent actual = first.CopySurfaceSamples(key, buffer);
                Assert.That(second.CopySurfaceSamples(key, reverse), Is.EqualTo(actual));
                if (expected != PageContent.Mixed || actual != PageContent.Mixed)
                    continue;
                mixedPages++;
                int w = DensityVolume.SampleWidth;
                Vector3Int last = key * DensityVolume.ChunkCells - Vector3Int.one;
                for (int z = 0; z < w; z++)
                for (int y = 0; y < w; y++)
                for (int x = 0; x < 3; x++)
                {
                    float actualValue = buffer[x + 16 + w * (y + w * z)];
                    Assert.That(actualValue, Is.EqualTo(compared[x + w * (y + w * z)]));
                    if ((y & 3) == 0 && (z & 3) == 0)
                        Assert.That(actualValue, Is.EqualTo(first.SampleAt(last + new Vector3Int(x + 16, y, z))));
                }
            }
            Assert.That(mixedPages, Is.GreaterThan(0), "The test must cover actual surface seams.");
        }

        [Test]
        public void CaveProxyClassifiesOpenVolumeAndKeepsTheCombinedMouthSurface()
        {
            const int pageWidth = WorldCaveHorizon.PageWidth;
            const int width = DensityVolume.SampleWidth;
            var field = new ContinentField(new WorldDefinition(20261121));
            WorldPosition mouth = field.Caves.Passage[1];
            var samples = new float[width * width * width];
            Vector3Int entrance = Vector3Int.FloorToInt(new Vector3((float)mouth.X, (float)mouth.Y, (float)mouth.Z) / pageWidth);
            Assert.That(field.CopyCaveProxy(entrance, samples), Is.EqualTo(PageContent.Mixed));
            Assert.That(field.CaveProxyIncludesGround(entrance, PageContent.Mixed), Is.True);
            WorldPosition chamber = field.Caves.Chamber;
            Vector3Int inside = Vector3Int.FloorToInt(new Vector3((float)chamber.X, (float)chamber.Y, (float)chamber.Z) / pageWidth);
            Assert.That(field.CopyCaveProxy(inside, samples), Is.EqualTo(PageContent.Air));
            Assert.That(field.CaveProxyIncludesGround(inside, PageContent.Air), Is.False);
            Assert.That(field.CopyCaveProxy(inside + Vector3Int.back * 10, samples), Is.EqualTo(PageContent.Solid));
            float ground = field.HeightAt(mouth.X, mouth.Z);
            var lip = new WorldPosition(mouth.X, ground, mouth.Z);
            Assert.That(field.MaterialAt(lip, Vector3.up, ground, DensityVolume.Spacing * WorldCaveHorizon.SampleScale),
                Is.EqualTo(field.MaterialAt(lip, Vector3.up, ground, DensityVolume.Spacing)));
        }

        [Test]
        public void EditedPagesAtTheOverlappingCaveJunctionMatchDirectSampling()
        {
            var field = new ContinentField(new WorldDefinition(20261121));
            WorldPosition junction = field.Caves.Passage[3];
            Vector3Int cell = junction.Page * ContinentField.ChunkSize;
            Assert.That(field.Edit(cell, fill: true, out BoundsInt changed), Is.True);
            BoundsInt affected = ContinentField.AffectedChunks(changed);
            Assert.That(affected.size, Is.EqualTo(Vector3Int.one * 2));
            const int width = DensityVolume.SampleWidth;
            var samples = new float[width * width * width];
            foreach (Vector3Int page in affected.allPositionsWithin)
            {
                field.CopyEditedSurfaceSamples(page, samples);
                Vector3Int start = page * DensityVolume.ChunkCells - Vector3Int.one;
                for (int z = 0; z < width; z += 3)
                for (int y = 0; y < width; y += 3)
                for (int x = 0; x < width; x += 3)
                    Assert.That(samples[x + width * (y + z * width)],
                        Is.EqualTo(field.SampleAt(start + new Vector3Int(x, y, z))));
            }
        }

        [Test]
        public void HorizonRefinesThePlayersTileAtAFourWayRootBoundary()
        {
            const double boundary = 131072;
            var target = new WorldPosition(boundary + 90, 0, boundary + 100);
            target = new WorldPosition(target.X, terrain.SurfaceHeight(target) + 8, target.Z);
            Assert.That(player.EnterAt(target), Is.True);
            for (int i = 0; i < 190; i++)
                terrain.Stream(player.transform.position, Vector3.zero);
            Assert.That(terrain.HorizonLevelAt(target), Is.EqualTo(0));
            Assert.That(terrain.HorizonTiles, Is.LessThanOrEqualTo(224));
        }

        [Test]
        public void LakeAndMainRiverHaveSeededWaterAcrossTheGeographyGrid()
        {
            var surface = new WorldSurface(new WorldDefinition(20261121));
            float oldLevel = float.PositiveInfinity;
            for (int north = 56000; north >= -106000; north -= 4096)
            {
                Vector3 river = surface.RiverAt(north);
                float water = surface.WaterAt(surface.Center.x + river.x, surface.Center.y + north);
                Assert.That(water, Is.LessThan(oldLevel));
                Assert.That(surface.HeightAt(surface.Center.x + (double)river.x, surface.Center.y + north), Is.LessThan(water));
                oldLevel = water;
            }

            WorldPosition lake = terrain.BasinEntry;
            var submerged = new WorldPosition(lake.X, WorldSurface.LakeLevel - 0.8, lake.Z);
            Assert.That(player.EnterAt(submerged), Is.True);
            Assert.That(terrain.IsWater(player.transform.position), Is.True);
            for (int i = 0; i < 240; i++)
                player.MoveForFrame(Vector2.zero, -1f, false, false, 1f / 60f);
            Assert.That(player.WorldFeet.Y, Is.EqualTo(terrain.SurfaceHeight(lake)).Within(0.3));
        }

        private void FlyTo(WorldPosition goal)
        {
            for (int i = 0; i < 1000; i++)
            {
                Vector3 remaining = terrain.ToLocal(goal) - player.transform.position;
                if (remaining.magnitude < 0.05f)
                    return;
                player.MoveWithVelocity(Vector3.ClampMagnitude(remaining * 20f, 56f), 0.05f);
            }
            WorldPosition feet = player.WorldFeet;
            Assert.Fail($"Could not fly to {goal.X:F1}, {goal.Y:F1}, {goal.Z:F1} from {feet.X:F1}, {feet.Y:F1}, {feet.Z:F1}.");
        }

        private double Floor(Vector3 eye)
        {
            Assert.That(Physics.Raycast(eye, Vector3.down, out RaycastHit hit, 4f), Is.True);
            Assert.That(hit.collider, Is.InstanceOf<MeshCollider>());
            return terrain.ToWorld(hit.point).Y;
        }
    }
}
