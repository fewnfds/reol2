using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Reol2.Demo;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Reol2.Tests
{
    public sealed class VoxelDemoTests
    {
        private const float Step = 1f / 60f;

        private VoxelDemoTerrain terrain;
        private VoxelDemoPlayer player;
        private CharacterController motor;

        [UnitySetUp]
        public IEnumerator LoadDemo()
        {
            yield return SceneManager.LoadSceneAsync("VoxelDemo", LoadSceneMode.Single);
            yield return null;
            terrain = Object.FindFirstObjectByType<VoxelDemoTerrain>();
            player = Object.FindFirstObjectByType<VoxelDemoPlayer>();
            motor = player.GetComponent<CharacterController>();
            player.enabled = false;
        }

        [Test]
        public void OpenCrossAndRefillThePassage()
        {
            Walk(new Vector2(0f, 1f), 230);
            Assert.That(player.transform.position.x, Is.InRange(-0.40f, 0.20f), "The sealed passage must stop the character.");
            Assert.That(player.transform.position.y, Is.GreaterThan(2.75f), "Walk up the slope without a jump.");

            DigAt(3.55f, VoxelDemoTerrain.GateLowerCell);
            DigAt(4.52f, VoxelDemoTerrain.GateUpperCell);
            AssertSharedSeam();
            for (int i = 0; i < 100 && player.transform.position.x < 0.55f; i++)
                player.MoveForFrame(Vector2.up, Step);
            Assert.That(player.transform.position.x, Is.GreaterThanOrEqualTo(0.55f), "Walk through the edited seam.");
            Assert.That(terrain.TryEdit(VoxelDemoTerrain.GateLowerCell, fill: true, motor), Is.False,
                "Filling the cell occupied by the player must be rejected.");

            Walk(Vector2.up, 35);
            Assert.That(player.transform.position.x, Is.GreaterThan(1.6f), "Walk into the chamber.");
            for (int i = 0; i < 140 && player.transform.position.x > -1.10f; i++)
                player.MoveForFrame(Vector2.down, Step);
            Assert.That(player.transform.position.x, Is.LessThanOrEqualTo(-1.10f), "The path should work in both directions.");

            var eye = player.transform.position + Vector3.up * 1.35f;
            var aimAtFloor = new Ray(eye, (new Vector3(0.54f, 2.8f, 4.5f) - eye).normalized);
            Assert.That(terrain.TryPick(aimAtFloor, eye, out _, out var fill), Is.True);
            Assert.That(fill, Is.EqualTo(VoxelDemoTerrain.GateLowerCell), "Aiming at the excavated floor should pick the opening.");
            Assert.That(terrain.TryEdit(fill, fill: true, motor), Is.True);
            Assert.That(terrain.TryEdit(VoxelDemoTerrain.GateUpperCell, fill: true, motor), Is.True);
            Walk(Vector2.up, 80);
            Assert.That(player.transform.position.x, Is.LessThan(0.25f), "The filled passage must block movement again.");
            AssertSharedSeam();
        }

        [Test]
        public void DiggingAtNegativeCoordinatesRemovesFootSupport()
        {
            var foot = new Vector3(-4.5f, 2.1f, 4.5f);
            motor.enabled = false;
            player.transform.position = foot;
            motor.enabled = true;
            Walk(Vector2.zero, 25);
            foot = player.transform.position;
            var eye = foot + Vector3.up * 1.25f;
            Assert.That(terrain.TryPick(new Ray(eye, Vector3.down), eye, out var cell, out _), Is.True);
            Assert.That(cell.x, Is.EqualTo(-5));
            Assert.That(terrain.TryEdit(cell, fill: false), Is.True);
            Assert.That(terrain.LastEdit.ChunkCount, Is.EqualTo(1));
            Walk(Vector2.zero, 30);
            Assert.That(player.transform.position.y, Is.LessThan(foot.y - 0.32f), "The player should drop to the excavated floor.");
        }

        [Test]
        public void AimAtFilledOverhangSelectsTheCellThatCreatedIt()
        {
            var placed = new Vector3Int(-5, 2, 4);
            Assert.That(terrain.TryEdit(placed, fill: true), Is.True);
            var aim = new Ray(new Vector3(-3f, 2.5f, 4.5f), Vector3.left);
            Assert.That(Physics.Raycast(aim, out RaycastHit hit, 3f, Physics.DefaultRaycastLayers), Is.True);
            Assert.That(hit.point.x, Is.GreaterThan(-3.975f), "The added surface extends into the next edit cell.");
            Assert.That(terrain.TryPick(aim, aim.origin, out var dig, out _), Is.True);
            Assert.That(dig, Is.EqualTo(placed), "Aiming at the new surface should remove the soil that created it.");

            Assert.That(terrain.TryEdit(dig, fill: false), Is.True);
            Assert.That(Physics.Raycast(aim, 3f, Physics.DefaultRaycastLayers), Is.False);
        }

        [Test]
        public void RepeatedEditsKeepTheRenderedAndPhysicalSeamTogether()
        {
            for (int i = 0; i < 24; i++)
            {
                Assert.That(terrain.TryEdit(VoxelDemoTerrain.GateUpperCell, fill: (i & 1) != 0), Is.True);
                Assert.That(terrain.LastEdit.ChunkCount, Is.EqualTo(2));
                AssertSharedSeam();
            }
        }

        [Test]
        public void TrailheadGroundAndClearanceAreReserved()
        {
            Vector3Int cell = Vector3Int.FloorToInt(VoxelDemoTerrain.Trailhead);
            Assert.That(terrain.TryEdit(cell, fill: false), Is.False);
            Assert.That(terrain.CanFill(cell + Vector3Int.up, occupant: null), Is.False);
            Assert.That(terrain.TryEdit(cell + Vector3Int.up, fill: true), Is.False);
            Walk(Vector2.zero, 30);
            Assert.That(motor.isGrounded, Is.True);
        }

        private void DigAt(float height, Vector3Int expected)
        {
            var eye = new Vector3(-1.1f, height, 4.5f);
            Assert.That(terrain.TryPick(new Ray(eye, Vector3.right), eye, out var cell, out _), Is.True);
            Assert.That(cell, Is.EqualTo(expected));
            Assert.That(terrain.TryEdit(cell, fill: false), Is.True);
            Assert.That(terrain.LastEdit.ChunkCount, Is.EqualTo(2));
        }

        private void Walk(Vector2 input, int frames)
        {
            for (int i = 0; i < frames; i++)
                player.MoveForFrame(input, Step);
        }

        private void AssertSharedSeam()
        {
            MeshFilter[] chunks = terrain.GetComponentsInChildren<MeshFilter>();
            Assert.That(chunks, Has.Length.EqualTo(2));
            var left = Seam(chunks[0]);
            var right = Seam(chunks[1]);
            Assert.That(left, Is.Not.Empty);
            Assert.That(left.SetEquals(right), Is.True, "Both chunks should have the same triangle edges along their shared border.");
        }

        private static HashSet<(Vector3 From, Vector3 To)> Seam(MeshFilter filter)
        {
            Assert.That(filter.GetComponent<MeshCollider>().sharedMesh, Is.SameAs(filter.sharedMesh));
            var border = new HashSet<(Vector3, Vector3)>();
            Vector3[] vertices = filter.sharedMesh.vertices;
            int[] triangles = filter.sharedMesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            for (int corner = 0; corner < 3; corner++)
            {
                Vector3 a = filter.transform.TransformPoint(vertices[triangles[i + corner]]);
                Vector3 b = filter.transform.TransformPoint(vertices[triangles[i + (corner + 1) % 3]]);
                if (a.x != 0f || b.x != 0f)
                    continue;

                bool reverse = a.y > b.y || (a.y == b.y && a.z > b.z);
                border.Add(reverse ? (b, a) : (a, b));
            }

            return border;
        }
    }
}
