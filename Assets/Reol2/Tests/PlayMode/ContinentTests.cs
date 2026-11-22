using System.Collections;
using NUnit.Framework;
using Reol2.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Reol2.Tests
{
    public sealed class ContinentTests
    {
        private const float Step = 1f / 60f;
        private static readonly Vector3 SeaEntry = new(-0.5f, -0.8f, -944.5f);
        private ContinentTerrain terrain;
        private ContinentExplorer player;

        [UnitySetUp]
        public IEnumerator LoadContinent()
        {
            yield return SceneManager.LoadSceneAsync("ContinentDemo", LoadSceneMode.Single);
            yield return null;
            terrain = Object.FindFirstObjectByType<ContinentTerrain>();
            player = Object.FindFirstObjectByType<ContinentExplorer>();
            player.enabled = false;
        }

        [Test]
        public void OceanSurroundsTheIslandAndThePlayerCanDiveToTheSeabed()
        {
            foreach (Vector3 direction in new[] { Vector3.left, Vector3.right, Vector3.back, Vector3.forward })
                Assert.That(terrain.IsOcean(direction * 960f + Vector3.down), Is.True);
            Assert.That(terrain.SurfaceHeight(0f, 0f), Is.GreaterThan(20f));

            player.EnterAt(SeaEntry);
            Move(Vector2.zero, lift: -1f, frames: 1000);
            float seabed = terrain.SurfaceHeight(SeaEntry.x, SeaEntry.z);
            Vector3 feet = player.transform.position;
            Assert.That(seabed, Is.LessThan(-42f));
            Assert.That(feet.y, Is.EqualTo(seabed).Within(0.16f), "The controller must physically stop on the bed.");
            Assert.That(FloorAt(feet + Vector3.up, reach: 2f), Is.EqualTo(seabed).Within(0.04f));
            Assert.That(terrain.IsOcean(feet + Vector3.up * 0.7f), Is.True);
            Assert.That(player.LimitedMoves, Is.Zero, "Nearby pages should keep up with diving.");
        }

        [Test]
        public void EditingSeabedAcrossNegativeAxesSurvivesEvictionAndCanBeFilled()
        {
            float seabed = terrain.SurfaceHeight(SeaEntry.x, SeaEntry.z);
            Vector3 eye = new(SeaEntry.x, seabed + 2f, SeaEntry.z);
            player.EnterAt(eye);
            Assert.That(terrain.Pick(new Ray(eye, Vector3.down), eye, out Vector3Int cell, out _), Is.True);
            Assert.That(cell, Is.EqualTo(new Vector3Int(-1, -57, -945)));
            Assert.That(terrain.TryEdit(cell, fill: false), Is.True);
            float excavated = FloorAt(eye);
            Assert.That(excavated, Is.LessThan(seabed - 0.45f));
            // Three chunk faces adjoin this negative-coordinate cell. The uncut side still has a floor.
            Assert.That(FloorAt(new Vector3(0.04f, eye.y, eye.z)), Is.GreaterThan(excavated));
            Assert.That(FloorAt(new Vector3(eye.x, eye.y, -943.96f)), Is.GreaterThan(excavated));
            Assert.That(terrain.IsOcean(new Vector3(eye.x, (excavated + seabed) * 0.5f, eye.z)), Is.False,
                "Only the original marine volume is wet in this prototype.");

            Vector3 other = terrain.Landing + new Vector3(176f, 68f, 136f);
            player.EnterAt(other);
            terrain.Stream(other, Vector3.zero);
            Assert.That(terrain.HasCoverage(eye, eye), Is.False, "The edited page must be unloaded.");
            player.EnterAt(eye);
            Assert.That(FloorAt(eye), Is.EqualTo(excavated).Within(0.002f));
            Assert.That(terrain.TryEdit(cell, fill: true), Is.True);
            Assert.That(FloorAt(eye), Is.EqualTo(seabed).Within(0.16f));
        }

        [Test]
        public void LandSupportsContinuousWalkingAndRunning()
        {
            Vector3 start = terrain.Landing;
            player.EnterAt(start);
            Move(Vector2.up, frames: 480);
            Assert.That(player.transform.position.z - start.z, Is.GreaterThan(32f));
            Move(Vector2.up, frames: 480, fast: true);
            Assert.That(player.transform.position.z - start.z, Is.GreaterThan(96f));
            Assert.That(player.GetComponent<CharacterController>().isGrounded, Is.True);
            Assert.That(player.LimitedMoves, Is.Zero);
        }

        [Test]
        public void AContinuousShaftReachesTheProtectedBedrockAcrossVerticalPages()
        {
            float seabed = terrain.SurfaceHeight(SeaEntry.x, SeaEntry.z);
            var mouth = new Vector3(SeaEntry.x, seabed + 0.6f, SeaEntry.z);
            player.EnterAt(mouth);
            for (int y = -57; y > ContinentTerrain.LowestElevation; y--)
                Assert.That(terrain.TryEdit(new Vector3Int(-1, y, -945), fill: false), Is.True);

            Move(Vector2.zero, lift: -1f, frames: 205, flight: true);
            Vector3 floor = player.transform.position;
            Assert.That(floor.y, Is.EqualTo(ContinentTerrain.LowestElevation + 1f).Within(0.22f));
            Assert.That(FloorAt(floor + Vector3.up, reach: 2f), Is.EqualTo(floor.y).Within(0.16f));
            Assert.That(terrain.IsOcean(floor + Vector3.up), Is.False);
            Move(Vector2.zero, lift: 1f, frames: 170, flight: true);
            Assert.That(player.transform.position.y, Is.GreaterThan(seabed + 2f));
        }

        [Test]
        public void UnreadyGroundClipsLongSweepAndFlightCanTurnBack()
        {
            Vector3 start = new(-36f, 40f, -940f);
            player.EnterAt(start);
            Vector3 target = start + Vector3.forward * 180f;
            Assert.That(terrain.HasCoverage(start, target), Is.False);
            Vector3 allowed = terrain.LimitDisplacement(start, target - start);
            Assert.That(allowed.z, Is.InRange(0.1f, 20f));
            Assert.That(terrain.HasCoverage(start, start + allowed), Is.True);

            Move(Vector2.up, frames: 180, fast: true, flight: true);
            Vector3 turnaround = player.transform.position;
            Assert.That(turnaround.z - start.z, Is.GreaterThan(190f));
            Move(Vector2.down, frames: 120, fast: true, flight: true);
            Assert.That(turnaround.z - player.transform.position.z, Is.GreaterThan(128f));
        }

        [Test]
        public void FastFlightCanTurnIntoAColdCoastWithFiftyMillisecondSteps()
        {
            const float cappedStep = 0.05f;
            Vector3 start = new(-650f, 5f, -820f);
            player.EnterAt(start);
            for (int i = 0; i < 240; i++)
            {
                bool inland = i >= 120;
                Vector3 feet = player.transform.position;
                Vector3 ahead = feet + (inland ? Vector3.forward : Vector3.right) * 27f;
                float altitude = Mathf.Max(5f, terrain.SurfaceHeight(ahead.x, ahead.z) + 7f);
                float lift = Mathf.Clamp((altitude - feet.y) * 0.40f, -1f, 1f);
                player.MoveForFrame(inland ? Vector2.up : Vector2.right, lift, fast: true, flight: true, cappedStep);
            }
            Assert.That(player.transform.position.x - start.x, Is.GreaterThan(400f));
            Assert.That(player.transform.position.z - start.z, Is.GreaterThan(400f));
            Assert.That(player.LimitedMoves, Is.Zero);
        }

        [Test]
        public void OversizedFlightStepStopsAtReadyTerrainAndContinuedInputRecovers()
        {
            Vector3 start = new(-650f, 165f, -820f);
            player.EnterAt(start);
            player.MoveForFrame(Vector2.right, 0f, fast: true, flight: true, dt: 1.5f);
            Vector3 clipped = player.transform.position;
            Assert.That(player.LimitedMoves, Is.EqualTo(1));
            Assert.That(clipped.x - start.x, Is.LessThan(90f));
            Assert.That(terrain.HasCoverage(start, clipped), Is.True);

            for (int i = 0; i < 40; i++)
                player.MoveForFrame(Vector2.right, 0f, fast: true, flight: true, dt: 0.05f);
            Assert.That(player.transform.position.x - clipped.x, Is.GreaterThan(128f));
        }

        [Test]
        public void ColdFlightPreparesExtraHeadroomWhenBothEndpointPagesStayTheSame()
        {
            var start = new Vector3(-895.4f, 165.8f, -895.4f);
            Vector3 velocity = Vector3.right * 60f;
            player.EnterAt(start);
            terrain.Stream(start, velocity);

            Vector3 raised = start + Vector3.up * 0.25f;
            terrain.Stream(raised, velocity);

            Assert.That(terrain.HasCoverage(raised, raised + velocity * 0.68f), Is.True);
        }

        [Test]
        public void ReturnSiteAndTheExcavationLimitStayProtected()
        {
            Vector3 start = terrain.Landing;
            Assert.That(terrain.TryEdit(Vector3Int.FloorToInt(start - Vector3.up), fill: false), Is.False);
            Assert.That(terrain.TryEdit(Vector3Int.FloorToInt(start + Vector3.up), fill: true), Is.False);
            var motor = player.GetComponent<CharacterController>();
            Assert.That(terrain.TryEdit(Vector3Int.FloorToInt(start), fill: true, occupant: motor), Is.False);
            var bedrock = new Vector3Int(-1, ContinentTerrain.LowestElevation, -945);
            Assert.That(terrain.TryEdit(bedrock, fill: false), Is.False);
            Assert.That(terrain.TryEdit(bedrock + Vector3Int.up, fill: false), Is.True);
        }

        private void Move(Vector2 input, int frames, float lift = 0f, bool fast = false, bool flight = false)
        {
            for (int i = 0; i < frames; i++)
                player.MoveForFrame(input, lift, fast, flight, Step);
        }

        private static float FloorAt(Vector3 eye, float reach = 6f)
        {
            Assert.That(Physics.Raycast(eye, Vector3.down, out RaycastHit hit, reach, Physics.DefaultRaycastLayers), Is.True);
            Assert.That(hit.collider, Is.InstanceOf<MeshCollider>());
            return hit.point.y;
        }
    }
}
