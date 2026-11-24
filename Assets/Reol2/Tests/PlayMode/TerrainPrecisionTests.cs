using System;
using System.Collections;
using System.Diagnostics;
using NUnit.Framework;
using Reol2.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Reol2.Tests
{
    public sealed class TerrainPrecisionTests
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
        }

        [Test]
        public void PointerDigTargetsOneCellAtAFarPageSeamAndSurvivesReturn()
        {
            const double seam = -WorldDefinition.Extent + 128;
            const double x = seam + 0.5, z = seam + 4.5;
            double h = terrain.SurfaceHeight(new WorldPosition(x, 0, z));
            var observer = new WorldPosition(x, h + 1.75, z - 0.2);
            Assert.That(player.EnterAt(observer), Is.True);
            player.Look(0, 82);
            player.ProcessInput(default, 0f, focused: true);
            double neighbourX = seam - 0.5;
            double neighbour = FloorAt(neighbourX, z, h + 3);
            double before = FloorAt(x, z, h + 3);
            AimAt(x, z, before, new ExplorerInput { Dig = true });
            double cut = FloorAt(x, z, h + 3);
            Assert.That(cut, Is.LessThan(before - 0.15));
            Assert.That(FloorAt(neighbourX, z, h + 3), Is.EqualTo(neighbour).Within(0.02),
                "The adjacent cell must still have its floor.");

            Assert.That(player.EnterAt(terrain.WorldLanding), Is.True);
            Assert.That(player.EnterAt(observer), Is.True);
            Assert.That(FloorAt(x, z, h + 3), Is.EqualTo(cut).Within(0.004));
            player.Look(0, 82);
            var fillPosition = new WorldPosition(x, cut, z);
            Vector3 pointer = Camera.main.WorldToScreenPoint(terrain.ToLocal(fillPosition));
            Ray sight = Camera.main.ScreenPointToRay(pointer);
            Assert.That(terrain.PickSurface(sight, player.transform.position + Vector3.up * 1.38f, out RaycastHit hit),
                Is.True, "The player must have an unobstructed line into the crater.");
            WorldPosition selected = terrain.ToWorld(hit.point);
            double beforeFill = FloorAt(selected.X, selected.Z, h + 3);
            AimAt(x, z, fillPosition.Y, new ExplorerInput { Fill = true });
            double afterFill = FloorAt(selected.X, selected.Z, h + 3);
            Debug.Log(FormattableString.Invariant($"Terrain precision | far seam fill | offset from aim ({selected.X - x:F4}, {selected.Y - fillPosition.Y:F4}, {selected.Z - z:F4}) m | height gain at hit {afterFill - beforeFill:F4} m"));
            Assert.That(afterFill, Is.GreaterThan(beforeFill + 0.15));
        }

        [Test]
        public void GridCutsShareOneFloorAcrossFourPages()
        {
            const int x = 7536, z = -399848;
            int y = Mathf.FloorToInt(terrain.SurfaceHeight(new WorldPosition(x, 0, z)));
            double top = y + 0.70;
            double rayHeight = top + 3;
            Assert.That(player.EnterAt(new WorldPosition(x, top + 2, z - 3)), Is.True);
            Assert.That(terrain.TryGrade(new WorldPosition(x - 3, top, z), new WorldPosition(x + 3, top, z)), Is.True);
            double outside = FloorAt(x + 2.5, z, rayHeight);
            for (int dz = -1; dz <= 0; dz++)
            for (int dx = -1; dx <= 0; dx++)
                Assert.That(terrain.TryEdit(new Vector3Int(x + dx, y, z + dz), fill: false), Is.True);

            double minimum = double.PositiveInfinity, maximum = double.NegativeInfinity;
            for (int dz = -5; dz <= 5; dz++)
            for (int dx = -5; dx <= 5; dx++)
            {
                double floor = FloorAt(x + dx * 0.125, z + dz * 0.125, rayHeight);
                minimum = Math.Min(minimum, floor);
                maximum = Math.Max(maximum, floor);
            }
            Debug.Log(FormattableString.Invariant($"Grid cuts | floor relative to cell {minimum - y:F5}..{maximum - y:F5} m | spread {maximum - minimum:F5} m"));
            Assert.That(minimum, Is.EqualTo(y - 0.055).Within(0.01));
            Assert.That(maximum - minimum, Is.LessThan(0.004), "Crossings on the shared floor must align at the page corner.");
            Assert.That(FloorAt(x + 2.5, z, rayHeight), Is.EqualTo(outside).Within(0.002));

            player.Look(0, 38);
            player.ProcessInput(default, 0f, focused: true);
            AimAt(x - 2.5, z, FloorAt(x - 2.5, z, rayHeight), new ExplorerInput { Grade = true });
            AimAt(x + 2.5, z, outside, new ExplorerInput { Grade = true });
            Assert.That(FloorAt(x, z, rayHeight), Is.EqualTo(top).Within(0.025),
                "Both construction endpoints must still work through the player input.");
        }

        [Test]
        public void AFillThatCrossesAPageFaceCanBePickedOnTheNeighbouringSide()
        {
            var cell = new Vector3Int(-8, 12000, -8);
            var center = new WorldPosition(cell.x + 0.5, cell.y + 0.5, cell.z + 0.5);
            Assert.That(terrain.TryEdit(cell, fill: true), Is.True);
            Assert.That(player.EnterAt(new WorldPosition(center.X, center.Y, center.Z - 3)), Is.True);
            terrain.PrepareEntry(terrain.ToLocal(center));

            Vector3 eye = terrain.ToLocal(new WorldPosition(center.X - 2, center.Y, center.Z));
            var ray = new Ray(eye, Vector3.right);
            Assert.That(terrain.Pick(ray, eye, out Vector3Int dig, out _, out WorldPosition sample, out bool placed), Is.True);
            Assert.That(sample.Cell, Is.EqualTo(cell + Vector3Int.left));
            Assert.That(placed, Is.True);
            Assert.That(dig, Is.EqualTo(cell), "The overhanging surface belongs to the cell that created it.");
            Assert.That(terrain.TryEdit(dig, fill: false), Is.True);
            Assert.That(Physics.Raycast(ray, 3f), Is.False);
        }

        [Test]
        public void DigPicksTheThinSurfaceCellAboveAnIntegerHeight()
        {
            const int x = 7536, z = -399848;
            int y = Mathf.FloorToInt(terrain.SurfaceHeight(new WorldPosition(x, 0, z)));
            double height = y + 0.01;
            Assert.That(player.EnterAt(new WorldPosition(x, height + 2, z - 3)), Is.True);
            Assert.That(terrain.TryGrade(new WorldPosition(x - 3, height, z), new WorldPosition(x + 3, height, z)), Is.True);
            Vector3 eye = terrain.ToLocal(new WorldPosition(x + 0.5, height + 2, z + 0.5));
            Assert.That(terrain.Pick(new Ray(eye, Vector3.down), eye, out Vector3Int dig, out _), Is.True);
            Assert.That(dig, Is.EqualTo(new Vector3Int(x, y, z)));
            Assert.That(terrain.TryEdit(dig, fill: false), Is.True);
            Assert.That(FloorAt(x + 0.5, z + 0.5, height + 2), Is.EqualTo(y - 0.055).Within(0.01));
        }

        [Test]
        public void AdjacentSphereCutsDoNotLeaveANarrowCollisionSpurAtAPageCorner()
        {
            const double x = 7536, z = -399848;
            double naturalHeight = terrain.SurfaceHeight(new WorldPosition(x, 0, z));
            double height = Math.Round(naturalHeight * 2) * 0.5 + 0.0005;
            var observer = new WorldPosition(x, height + 1, z - 2.45);
            Assert.That(player.EnterAt(observer), Is.True);
            for (int row = -1; row <= 1; row++)
            {
                double laneZ = z + row * 1.5;
                terrain.TryGrade(new WorldPosition(x - 4, height, laneZ), new WorldPosition(x + 4, height, laneZ));
            }
            float rayHeight = (float)height + 2;
            double outside = FloorAt(x + 1.8, z, rayHeight);
            Assert.That(FloorAt(x, z, rayHeight), Is.EqualTo(height).Within(0.002));
            foreach (Vector2 offset in new[] { Vector2.left, Vector2.right, Vector2.down, Vector2.up })
                Assert.That(terrain.TryEditSphere(new WorldPosition(x + 0.66 * offset.x, height - 0.20, z + 0.66 * offset.y), false), Is.True);

            double center = FloorAt(x, z, rayHeight);
            double side = FloorAt(x + 0.10, z, rayHeight);
            Assert.That(center - side, Is.LessThan(0.13), "An isolated spike must not remain above the surrounding cut.");
            Assert.That(center, Is.LessThan(height - 0.35));
            Assert.That(FloorAt(x + 1.8, z, rayHeight), Is.EqualTo(outside).Within(0.002));
            Assert.That(terrain.TryEditSphere(new WorldPosition(x, height - 0.20, z + 0.66), false), Is.False);
            Assert.That(player.EnterAt(new WorldPosition(120000.5, 22000, 120000.5)), Is.True);
            Assert.That(player.EnterAt(observer), Is.True);
            Assert.That(FloorAt(x, z, rayHeight), Is.EqualTo(center).Within(0.002));
        }

        [Test]
        public void GridBrushDigAndFillAtHomeAndRejectCellsOutsideTheWorld()
        {
            WorldPosition land = terrain.WorldLanding;
            Assert.That(player.EnterAt(new WorldPosition(land.X - 5, land.Y + 1.25, land.Z)), Is.True);
            Vector3 eye = terrain.ToLocal(new WorldPosition(land.X, land.Y + 3, land.Z));
            double original = FloorAt(land.X, land.Z, land.Y + 3);
            Assert.That(terrain.Pick(new Ray(eye, Vector3.down), eye, out Vector3Int cell, out _), Is.True);
            Assert.That(terrain.TryEdit(cell, fill: false), Is.True);
            double lowered = FloorAt(land.X, land.Z, land.Y + 3);
            Assert.That(lowered, Is.LessThan(original - 0.01));
            Assert.That(terrain.TryEdit(cell, fill: true), Is.True);
            Assert.That(FloorAt(land.X, land.Z, land.Y + 3), Is.GreaterThan(lowered + 0.1));

            var lowest = new Vector3Int(land.Cell.x, WorldDefinition.MinY, land.Cell.z);
            Assert.That(terrain.TryEdit(lowest, fill: false), Is.True);
            Assert.That(terrain.TryEdit(lowest, fill: true), Is.True);
            int edited = terrain.EditedSamples;
            Assert.That(terrain.TryEdit(lowest + Vector3Int.down, fill: false), Is.False);
            Assert.That(terrain.TryEdit(new Vector3Int(lowest.x, WorldDefinition.MaxY, lowest.z), fill: true), Is.False);
            Assert.That(terrain.TryEdit(new Vector3Int(WorldDefinition.Extent, 0, lowest.z), fill: true), Is.False);
            Assert.That(terrain.TryEdit(new Vector3Int(lowest.x, 0, -WorldDefinition.Extent - 1), fill: true), Is.False);
            Assert.That(terrain.EditedSamples, Is.EqualTo(edited));
        }

        [Test]
        public void GradeThatWouldBuryThePlayerOnlySucceedsAfterSteppingAway()
        {
            WorldPosition land = terrain.WorldLanding;
            double x = land.X + 30, z = land.Z + 48;
            double original = terrain.SurfaceHeight(new WorldPosition(x, 0, z));
            Assert.That(player.EnterAt(new WorldPosition(x, original + 0.2, z)), Is.True);
            var start = new WorldPosition(x - 2, original + 0.65, z);
            var end = new WorldPosition(x + 2, original + 0.65, z);
            CharacterController body = player.GetComponent<CharacterController>();
            int edited = terrain.EditedSamples;
            Assert.That(terrain.TryGrade(start, end, body), Is.False);
            Assert.That(terrain.EditedSamples, Is.EqualTo(edited));
            Assert.That(FloorAt(x, z, original + 3), Is.EqualTo(original).Within(0.005));
            Assert.That(player.EnterAt(new WorldPosition(x, original + 1, z - 3)), Is.True);
            Assert.That(terrain.TryGrade(start, end, body), Is.True);
            Assert.That(FloorAt(x, z, original + 3), Is.EqualTo(start.Y).Within(0.025));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ReturningHomeFindsTheCurrentlyPublishedSurface(bool fill)
        {
            WorldPosition land = terrain.WorldLanding;
            Assert.That(player.EnterAt(new WorldPosition(land.X - 6, land.Y + 2, land.Z)), Is.True);
            double original = FloorAt(land.X, land.Z, land.Y + 5);
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                var point = new WorldPosition(land.X + dx * 0.5, original + (fill ? 0.2 : -0.2), land.Z + dz * 0.5);
                Assert.That(terrain.TryEditSphere(point, fill), Is.True);
            }
            double ground = FloorAt(land.X, land.Z, land.Y + 5);
            Assert.That((ground - original) * (fill ? 1 : -1), Is.GreaterThan(0.35));
            Assert.That(player.EnterAt(new WorldPosition(120000.5, 22000, 120000.5)), Is.True);
            Assert.That(player.TryReturnHome(), Is.True);
            Assert.That(player.WorldFeet.X, Is.EqualTo(land.X).Within(0.02));
            Assert.That(player.WorldFeet.Z, Is.EqualTo(land.Z).Within(0.02));
            Assert.That(FloorAt(land.X, land.Z, ground + 3), Is.EqualTo(ground).Within(0.004));
            Assert.That(player.WorldFeet.Y - ground, Is.InRange(-0.05, 0.55));
        }

        [Test]
        public void TwoPointerMarksGradeTheGroundBetweenTheHits()
        {
            WorldPosition land = terrain.WorldLanding;
            double x = land.X + 30, z = land.Z + 48;
            double h = terrain.SurfaceHeight(new WorldPosition(x, 0, z));
            Assert.That(player.EnterAt(new WorldPosition(x, h + 0.45, z - 3)), Is.True);
            player.Look(0, 27);
            player.ProcessInput(default, 0f, focused: true);
            Assert.That(terrain.TryEditSphere(new WorldPosition(x, h + 0.08, z), fill: false), Is.True);
            double hollow = FloorAt(x, z, h + 3);
            double left = FloorAt(x - 1.75, z, h + 3);
            double right = FloorAt(x + 1.75, z, h + 3);
            int samples = terrain.EditedSamples;
            AimAt(x - 1.75, z, left, new ExplorerInput { Grade = true });
            Assert.That(terrain.EditedSamples, Is.EqualTo(samples), "The first mark is a preview.");
            AimAt(x + 1.75, z, right, new ExplorerInput { Grade = true });
            double graded = FloorAt(x, z, h + 3);
            Assert.That(graded, Is.GreaterThan(hollow + 0.2));
            Assert.That(graded, Is.EqualTo((left + right) * 0.5).Within(0.035));
        }

        [Test]
        public void ASecondLayerRejectsTheWholeGradeAndKeepsTheCavityOpen()
        {
            WorldPosition land = terrain.WorldLanding;
            double x = land.X + 30, z = land.Z + 48;
            double h = terrain.SurfaceHeight(new WorldPosition(x, 0, z));
            Assert.That(player.EnterAt(new WorldPosition(x - 3, h + 0.6, z - 3)), Is.True);
            var start = new WorldPosition(x - 6, terrain.SurfaceHeight(new WorldPosition(x - 6, 0, z)) + 0.3, z);
            var end = new WorldPosition(x, h + 0.3, z);
            var intact = new ContinentField(new WorldDefinition(terrain.Seed));
            Assert.That(intact.EditGrade(start, end, out _), Is.True, "This segment must be valid before digging below it.");

            var cavity = new WorldPosition(x, Math.Floor(h * 2) * 0.5 - 1, z);
            Assert.That(terrain.TryEditSphere(cavity, fill: false), Is.True);
            double roof = FloorAt(x, z, h + 3);
            double pit = FloorAt(x, z, cavity.Y);
            Assert.That(roof - pit, Is.GreaterThan(1.0));
            int samples = terrain.EditedSamples;
            double unaffected = FloorAt(start.X, z, h + 3);
            Assert.That(terrain.TryGrade(start, end), Is.False);
            Assert.That(terrain.EditedSamples, Is.EqualTo(samples));
            Assert.That(FloorAt(start.X, z, h + 3), Is.EqualTo(unaffected).Within(0.002), "No partial work at the start.");
            Assert.That(FloorAt(x, z, h + 3), Is.EqualTo(roof).Within(0.002));
            Assert.That(FloorAt(x, z, cavity.Y), Is.EqualTo(pit).Within(0.002));
        }

        [Test]
        public void RaisingGroundPreservesACavityCeilingJustOutsideTheGradeScan()
        {
            WorldPosition land = terrain.WorldLanding;
            double x = land.X + 30, z = land.Z + 48;
            double y0 = Math.Round(terrain.SurfaceHeight(new WorldPosition(x, 0, z)) * 2d) / 2d;
            Assert.That(player.EnterAt(new WorldPosition(x, y0 + 1, z - 4)), Is.True);
            // Establish both target surfaces with the same editing operations available in the scene.
            for (int row = -1; row <= 1; row++)
            {
                double laneZ = z + 1.5 * row;
                Assert.That(terrain.TryGrade(new WorldPosition(x - 8, y0 + 0.10, laneZ),
                    new WorldPosition(x + 8, y0 + 0.10, laneZ)), Is.True);
            }
            var cavity = new WorldPosition(x, y0 - 1.30, z);
            Assert.That(terrain.TryEditSphere(cavity, fill: false), Is.True);
            Vector3 cavityRay = terrain.ToLocal(cavity);
            Assert.That(Physics.Raycast(cavityRay, Vector3.up, out RaycastHit before, 1.5f), Is.True);
            double ceiling = terrain.ToWorld(before.point).Y;
            Assert.That(ceiling, Is.EqualTo(y0 - 0.65).Within(0.006), "The inner ceiling must lie below the original scan.");
            Assert.That(FloorAt(x, z, y0 + 3), Is.EqualTo(y0 + 0.10).Within(0.006));

            var start = new WorldPosition(x - 3, y0 + 1.25, z);
            var end = new WorldPosition(x + 3, y0 + 1.25, z);
            int samples = terrain.EditedSamples;
            bool accepted = terrain.TryGrade(start, end);
            if (!accepted)
                Assert.That(terrain.EditedSamples, Is.EqualTo(samples));
            Assert.That(FloorAt(x - 2, z, y0 + 3), Is.EqualTo(accepted ? start.Y : y0 + 0.10).Within(0.006));
            Assert.That(Physics.Raycast(cavityRay, Vector3.up, out RaycastHit after, 1.5f), Is.True);
            Assert.That(terrain.ToWorld(after.point).Y, Is.EqualTo(ceiling).Within(0.006),
                "Raising the upper surface must preserve the lower crossing of the shared sample.");
        }

        [Test]
        public void GradedTerrainPublishesAWalkableOneInTwentySlopeAndRemainsDiggable()
        {
            const double run = 20;
            WorldPosition land = terrain.WorldLanding;
            double x0 = land.X + 32, z = land.Z + 40.25;
            double near = terrain.SurfaceHeight(new WorldPosition(x0, 0, z));
            double far = terrain.SurfaceHeight(new WorldPosition(x0 + run, 0, z));
            int direction = near >= far ? 1 : -1;
            double startX = direction == 1 ? x0 : x0 + run;
            double startY = (near + far) * 0.5 - 0.5;
            var start = new WorldPosition(startX, startY, z);
            var end = new WorldPosition(startX + direction * run, startY + 1, z);
            Assert.That(player.EnterAt(new WorldPosition(x0 + run / 2, startY + 2.8, z - 4)), Is.True);
            terrain.PrepareEntry(terrain.ToLocal(start));
            terrain.PrepareEntry(terrain.ToLocal(end));
            double previousHighEnd = FloorAt(end.X - direction, z, startY + 3);
            Assert.That(terrain.TryGrade(start, end), Is.True);

            double maxError = 0, maxStepError = 0;
            double previous = FloorAt(startX, z, startY + 3);
            for (int i = 1; i <= 40; i++)
            {
                double y = FloorAt(startX + direction * i * 0.5, z, startY + 3);
                maxError = Math.Max(maxError, Math.Abs(y - (startY + i * 0.025)));
                maxStepError = Math.Max(maxStepError, Math.Abs(y - previous - 0.025));
                previous = y;
            }
            Assert.That(maxError, Is.LessThan(0.03), "Published collision should follow the target profile.");
            Assert.That(maxStepError, Is.LessThan(0.03), "A correct endpoint alone could conceal stairs.");
            Assert.That(FloorAt(end.X - direction, z, startY + 3), Is.GreaterThan(previousHighEnd + 0.15));

            Assert.That(player.EnterAt(new WorldPosition(startX + direction, startY + 0.6, z)), Is.True);
            player.Look(direction * 90f, 12f);
            const float dt = 1f / 60f;
            for (int i = 0; i < 45; i++)
                player.MoveForFrame(Vector2.zero, 0f, false, false, dt);
            double yAtFoot = player.WorldFeet.Y;
            for (int i = 0; i < 360 && (player.WorldFeet.X - startX) * direction < run - 1; i++)
                player.MoveForFrame(Vector2.up, 0f, false, false, dt);
            Assert.That(player.MoveMode, Is.EqualTo(ExplorerMoveMode.Land));
            Assert.That((player.WorldFeet.X - startX) * direction, Is.GreaterThanOrEqualTo(run - 1));
            Assert.That(player.WorldFeet.Y - yAtFoot, Is.InRange(0.77, 1.02), "The character must walk up without jumping.");

            double middleX = x0 + run / 2;
            double beforeCut = FloorAt(middleX, z, startY + 3);
            Assert.That(terrain.TryEditSphere(new WorldPosition(middleX, beforeCut - 0.2, z), fill: false), Is.True);
            double afterCut = FloorAt(middleX, z, startY + 3);
            Assert.That(afterCut, Is.LessThan(beforeCut - 0.35), "The new grade must use destructible terrain.");
            Debug.Log(FormattableString.Invariant($"Terrain precision | graded 20m | max profile error {maxError:F5} m | max half-metre rise error {maxStepError:F5} m | cut depth {beforeCut - afterCut:F3} m"));
        }

        [TestCase(false, TestName = "CaveJunctionGridBrushPublishesCollision")]
        [TestCase(true, TestName = "CaveJunctionContinuousBrushPublishesCollision")]
        public void CaveJunctionBrushComparison(bool continuous)
        {
            WorldPosition join = terrain.CavePassage[3];
            var corner = new Vector3Int((int)Math.Round(join.X / 8) * 8, (int)Math.Round(join.Y / 8) * 8,
                (int)Math.Round(join.Z / 8) * 8);
            var center = new WorldPosition(corner.x + 0.5, corner.y + 0.5, corner.z + 0.5);
            Assert.That(player.EnterAt(new WorldPosition(join.X + 5, join.Y + 4, join.Z + 4)), Is.True);
            terrain.PrepareEntry(terrain.ToLocal(center));
            Vector3 rayOrigin = terrain.ToLocal(center) + Vector3.up * 1.75f;
            Assert.That(Physics.Raycast(rayOrigin, Vector3.down, 2.5f), Is.False);
            var times = new double[100];
            double cold = 0;
            int initialSamples = terrain.EditedSamples;
            for (int i = 0; i <= times.Length; i++)
            {
                bool fill = (i & 1) == 0;
                long tick = Stopwatch.GetTimestamp();
                bool changed = continuous ? terrain.TryEditSphere(center, fill) : terrain.TryEdit(corner, fill);
                double elapsed = (Stopwatch.GetTimestamp() - tick) * (1000d / Stopwatch.Frequency);
                if (i == 0)
                    cold = elapsed;
                else
                    times[i - 1] = elapsed;
                Assert.That(changed, Is.True, $"Edit {i} must change the terrain.");
                Assert.That(Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 2.5f), Is.EqualTo(fill));
                if (fill)
                    Assert.That(hit.collider.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(((MeshCollider)hit.collider).sharedMesh));
            }
            Array.Sort(times);
            string brush = continuous ? "sphere" : "grid";
            Debug.Log(FormattableString.Invariant($"Terrain precision | cave junction {brush} | cold {cold:F3} ms | 100 cached edit-to-collision samples p50 {times[49]:F3} p95 {times[94]:F3} p99 {times[98]:F3} max {times[99]:F3} ms | unique edited samples {terrain.EditedSamples - initialSamples}"));
        }

        private void AimAt(double x, double z, double y, ExplorerInput input)
        {
            Vector3 screen = Camera.main.WorldToScreenPoint(terrain.ToLocal(new WorldPosition(x, y, z)));
            Assert.That(screen.z, Is.Positive);
            Assert.That(Camera.main.pixelRect.Contains(screen), Is.True);
            input.HasPointer = true;
            input.Pointer = screen;
            player.ProcessInput(input, 0f, focused: true);
        }

        private double FloorAt(double x, double z, double fromY)
        {
            Vector3 origin = terrain.ToLocal(new WorldPosition(x, fromY, z));
            Assert.That(Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 8f), Is.True,
                $"No published surface below {x:F3}, {fromY:F3}, {z:F3}.");
            Assert.That(hit.collider, Is.InstanceOf<MeshCollider>());
            return terrain.ToWorld(hit.point).Y;
        }
    }
}
