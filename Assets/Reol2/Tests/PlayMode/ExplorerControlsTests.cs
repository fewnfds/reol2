using System.Collections;
using NUnit.Framework;
using Reol2.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Reol2.Tests
{
    public sealed class ExplorerControlsTests : InputTestFixture
    {
        private const float Dt = 1f / 60f;
        private ContinentExplorer player;
        private ContinentTerrain terrain;
        private Keyboard keyboard;
        private Mouse mouse;

        public override void Setup()
        {
            base.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
        }

        [UnitySetUp]
        public IEnumerator OpenWorld()
        {
            yield return SceneManager.LoadSceneAsync("LargeWorld", LoadSceneMode.Single);
            yield return null;
            player = Object.FindFirstObjectByType<ContinentExplorer>();
            terrain = Object.FindFirstObjectByType<ContinentTerrain>();
            player.enabled = false;
            player.ProcessInput(default, 0f, focused: true);
            player.ProcessInput(default, 0f, focused: true);
        }

        public override void TearDown()
        {
            try
            {
                if (player != null)
                    player.ProcessInput(default, 0f, focused: false);
            }
            finally
            {
                base.TearDown();
            }
        }

        [Test]
        public void TestSpeedDoublesAndHalvesFlightOnEachNewPress()
        {
            Assert.That(player.EnterAt(new WorldPosition(1000.5, 20000, 1000.5)), Is.True);
            player.Look(0f, 0f);
            Tick(default, Key.F);
            Tick(default, Key.W);
            float flightSpeed = HorizontalSpeed();
            Assert.That(flightSpeed, Is.EqualTo(18f).Within(0.03f));

            foreach (int multiplier in new[] { 2, 4, 8, 16, 32, 64 })
            {
                Tick(default, Key.W, Key.Equals);
                Assert.That(HorizontalSpeed(), Is.EqualTo(flightSpeed * multiplier).Within(0.4f));
                Tick(default, Key.W, Key.Equals);
                Assert.That(HorizontalSpeed(), Is.EqualTo(flightSpeed * multiplier).Within(0.4f));
                Tick(default, Key.W);
            }
            Tick(default, Key.W, Key.NumpadPlus);
            Assert.That(HorizontalSpeed(), Is.EqualTo(64f * flightSpeed).Within(0.4f));

            foreach (float multiplier in new[] { 32f, 16f, 8f, 4f, 2f, 1f, 0.5f, 0.25f, 0.25f })
            {
                Tick(default, Key.W, Key.NumpadMinus);
                Assert.That(HorizontalSpeed(), Is.EqualTo(multiplier * flightSpeed).Within(0.4f));
                Tick(default, Key.W);
            }
        }

        [Test]
        public void TestSpeedHudScalesVerticalMovementAndRespectsTravelAndWaterSurface()
        {
            Assert.That(player.EnterAt(new WorldPosition(1000.5, 20000, 1000.5)), Is.True);
            player.Look(0f, 0f);
            Tick(default, Key.F);
            var faster = new MouseState { position = new Vector2(Screen.width - 35f, 43f) };
            Tick(Holding(faster, MouseButton.Left), Key.Space);
            Assert.That(player.ActualVelocity.y, Is.EqualTo(30f).Within(0.03f));
            faster.delta = new Vector2(8f, 0f);
            Tick(Holding(faster, MouseButton.Left), Key.Space);
            Assert.That(player.ActualVelocity.y, Is.EqualTo(30f).Within(0.03f));
            Assert.That(player.MouseGesture, Is.EqualTo(ExplorerMouseGesture.None));

            Tick(faster, Key.T);
            Tick(Holding(faster, MouseButton.Left), Key.Minus);
            Tick(default, Key.Escape);
            player.ProcessInput(new ExplorerInput { SpeedStep = -1 }, Dt, focused: false);
            Tick();
            Tick(default, Key.Space);
            Assert.That(player.ActualVelocity.y, Is.EqualTo(30f).Within(0.03f));

            Tick(default, Key.F1);
            double surfaceFeet = player.WorldFeet.Y;
            Tick(default, Key.X);
            Assert.That(player.WorldFeet.Y, Is.EqualTo(surfaceFeet - 8f * Dt).Within(0.01));
            Tick(default, Key.Space);
            Tick(default, Key.Space);
            Assert.That(player.WorldFeet.Y, Is.EqualTo(-ContinentExplorer.SurfaceSwimDepth).Within(0.01));
            Assert.That(player.MoveMode, Is.EqualTo(ExplorerMoveMode.Swim));
        }

        [Test]
        public void MouseClickStaysFreeAndLeftDragOnlyOrbitsWhileRightDragSteers()
        {
            Assert.That(player.EnterAt(new WorldPosition(1000.5, 20000, 1000.5)), Is.True);
            player.Look(0f, 0f);
            Tick(default, Key.F);
            var left = new MouseState().WithButton(MouseButton.Left);
            Tick(left);
            Assert.That(player.MouseGesture, Is.EqualTo(ExplorerMouseGesture.None));
            Assert.That(Cursor.visible, Is.True);

            left.delta = new Vector2(250f, -150f);
            Tick(left, Key.W);
            Assert.That(player.MouseGesture, Is.EqualTo(ExplorerMouseGesture.Orbit));
            Assert.That(Vector3.Angle(player.transform.forward, Vector3.forward), Is.LessThan(0.01f));
            Assert.That(player.ActualVelocity.x, Is.EqualTo(0f).Within(0.01f));
            Assert.That(player.ActualVelocity.y, Is.EqualTo(0f).Within(0.01f));

            var right = new MouseState().WithButton(MouseButton.Right);
            Tick(right, Key.W);
            Assert.That(player.MouseGesture, Is.EqualTo(ExplorerMouseGesture.Steer));
            Assert.That(player.ActualVelocity.x, Is.GreaterThan(5f));
            Assert.That(player.ActualVelocity.y, Is.LessThan(-3f));
            Tick(right, Key.W, Key.X);
            Assert.That(player.ActualVelocity.y, Is.LessThan(-8f));
            Tick(right, Key.W, Key.X, Key.Space);
            Assert.That(player.ActualVelocity.y, Is.EqualTo(0f).Within(0.02f));
            Tick(right, Key.D);
            Assert.That(player.ActualVelocity.y, Is.EqualTo(0f).Within(0.02f));
            Assert.That(Vector3.Dot(player.ActualVelocity.normalized, player.transform.right), Is.GreaterThan(0.99f));

            Tick(right.WithButton(MouseButton.Left));
            Assert.That(player.MouseGesture, Is.EqualTo(ExplorerMouseGesture.Advance));
            Assert.That(player.ActualVelocity.y, Is.LessThan(-3f));
            Tick();
            Assert.That(player.MouseGesture, Is.EqualTo(ExplorerMouseGesture.None));
            Assert.That(Cursor.visible, Is.True);
        }

        [Test]
        public void SeaShortcutAllowsPitchDivingAndSurfaceHolding()
        {
            Tick(default, Key.F1);
            Assert.That(player.MoveMode, Is.EqualTo(ExplorerMoveMode.Swim));
            double start = player.WorldFeet.Y;
            for (int i = 0; i < 60; i++)
                Tick(default, Key.X);
            Assert.That(player.WorldFeet.Y, Is.LessThan(start - 3.5));
            for (int i = 0; i < 180; i++)
            {
                Tick(default, Key.Space);
                Assert.That(player.MoveMode, Is.EqualTo(ExplorerMoveMode.Swim));
            }
            Assert.That(player.WorldFeet.Y, Is.EqualTo(-ContinentExplorer.SurfaceSwimDepth).Within(0.01));
            Assert.That(player.IsDiving, Is.False);

            player.Look(0f, -35f);
            Assert.That(terrain.IsWater(Camera.main.transform.position), Is.False, "Surface orbit keeps the camera above water.");

            player.Look(0f, 24f);
            var right = new MouseState().WithButton(MouseButton.Right);
            for (int i = 0; i < 45; i++)
                Tick(right, Key.W);
            Assert.That(player.WorldFeet.Y, Is.EqualTo(-ContinentExplorer.SurfaceSwimDepth).Within(0.01));
            Assert.That(player.IsDiving, Is.False);

            player.Look(0f, 50f);
            Tick(right);
            Assert.That(player.IsDiving, Is.False, "Looking down while treading water keeps the player at the surface.");
            Tick(right, Key.W);
            Assert.That(player.IsDiving, Is.True);
            for (int i = 0; i < 80; i++)
                Tick(right, Key.W);
            Assert.That(player.WorldFeet.Y, Is.LessThan(-4));
            Assert.That(terrain.IsWater(Camera.main.transform.position), Is.True);
            Tick(default, Key.Home);
            Assert.That(player.MoveMode, Is.EqualTo(ExplorerMoveMode.Land));
        }

        [Test]
        public void SubmergedCameraRetainsTheOutdoorViewAndSwitchesEffects()
        {
            Tick(default, Key.F1);
            var camera = Camera.main;
            var environment = player.Environment;
            environment.Clock.Paused = true;
            environment.PreviewWeather = new WeatherSample(0.2f, 0f, 0f, 0.13f, 0f);
            player.Look(0f, 0f);
            float range = camera.farClipPlane, fogStart = RenderSettings.fogStartDistance, fogEnd = RenderSettings.fogEndDistance;
            Assert.That(environment.UnderwaterEffectsActive, Is.False);

            WorldPosition sea = player.WorldFeet;
            Assert.That(player.EnterAt(new WorldPosition(sea.X, -8f, sea.Z)), Is.True);
            player.Look(0f, -25f);
            Assert.That(terrain.IsWater(camera.transform.position), Is.True);
            Assert.That(environment.UnderwaterEffectsActive, Is.True);
            Assert.That(camera.GetComponent<AudioLowPassFilter>().enabled, Is.True);
            Assert.That(camera.clearFlags, Is.EqualTo(CameraClearFlags.Skybox));
            Assert.That(camera.farClipPlane, Is.EqualTo(range).Within(0.01f));
            Assert.That(RenderSettings.fogStartDistance, Is.EqualTo(fogStart).Within(0.01f));
            Assert.That(RenderSettings.fogEndDistance, Is.EqualTo(fogEnd).Within(0.01f));

            Assert.That(player.EnterAt(terrain.OceanEntry), Is.True);
            Assert.That(environment.UnderwaterEffectsActive, Is.False);
            Assert.That(camera.GetComponent<AudioLowPassFilter>().enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator TheSwimPoseKeepsItsHipsAndMeshAboveTheSeabed()
        {
            WorldPosition sea = terrain.OceanEntry;
            float bed = terrain.SurfaceHeight(sea);
            Assert.That(player.EnterAt(new WorldPosition(sea.X, bed + 0.6f, sea.Z)), Is.True);
            player.Look(0f, 0f);
            for (int i = 0; i < 36; i++)
                Tick(default, Key.X);
            Assert.That(player.IsDiving, Is.True);
            Assert.That(Physics.Raycast(player.transform.position + Vector3.up * 3f, Vector3.down,
                out RaycastHit floor, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(player.transform.position.y - floor.point.y, Is.LessThan(0.24f));

            var body = player.GetComponentInChildren<PlayerAnimatorDriver>();
            var animator = body.Animator;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var skinned = body.GetComponentsInChildren<SkinnedMeshRenderer>();
            var mesh = new Mesh();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            try
            {
                foreach (float speed in new[] { 0f, 2f, 4f })
                for (int frame = 0; frame < 4; frame++)
                {
                    animator.SetFloat("Speed", speed);
                    animator.Play("Base Layer.Swim", 0, frame / 4f);
                    animator.Update(0f);
                    yield return null;
                    Assert.That(hips.position.y - player.transform.position.y,
                        Is.EqualTo(PlayerAnimatorDriver.SwimHipsHeight).Within(0.01f));
                    float lowest = float.PositiveInfinity;
                    foreach (var renderer in skinned)
                    {
                        renderer.BakeMesh(mesh);
                        foreach (Vector3 vertex in mesh.vertices)
                            lowest = Mathf.Min(lowest, renderer.transform.TransformPoint(vertex).y);
                    }
                    Assert.That(lowest - floor.point.y, Is.GreaterThan(-0.02f), $"Swim blend speed {speed} at frame {frame}");
                }
            }
            finally
            {
                Object.Destroy(mesh);
            }
        }

        [Test]
        public void WalksThroughShallowsIntoSwimmingAndBackOntoTheShore()
        {
            WorldPosition shore = terrain.WorldLanding;
            double sea = terrain.OceanEntry.Z, land = shore.Z;
            while (land - sea > 0.15)
            {
                double middle = (land + sea) * 0.5;
                if (terrain.SurfaceHeight(new WorldPosition(shore.X, 0, middle)) < -0.35f)
                    sea = middle;
                else
                    land = middle;
            }
            var shallows = new WorldPosition(shore.X, 0, land);
            Assert.That(player.EnterAt(new WorldPosition(shallows.X, terrain.SurfaceHeight(shallows) + 0.18, shallows.Z)), Is.True);
            player.Look(180f, 12f);
            for (int i = 0; i < 12; i++)
                Tick();
            Assert.That(player.MoveMode, Is.EqualTo(ExplorerMoveMode.Land));
            Assert.That(player.Grounded, Is.True);
            for (int i = 0; i < 2400 && player.MoveMode != ExplorerMoveMode.Swim; i++)
                Tick(default, Key.W);
            Assert.That(player.MoveMode, Is.EqualTo(ExplorerMoveMode.Swim), $"Feet Y {player.WorldFeet.Y:F2}");

            player.Look(0f, 12f);
            for (int i = 0; i < 2400 && player.MoveMode != ExplorerMoveMode.Land; i++)
                Tick(default, Key.W);
            Assert.That(player.MoveMode, Is.EqualTo(ExplorerMoveMode.Land));
            Assert.That(player.WorldFeet.Z, Is.GreaterThan(shallows.Z - 100d));
        }

        [Test]
        public void GroundKeysKeepTurningStrafingRunningAndCrouchingDistinct()
        {
            for (int i = 0; i < 10; i++)
                Tick();
            Vector3 previous = player.transform.forward;
            Tick(default, Key.D);
            Assert.That(Vector3.Angle(previous, player.transform.forward), Is.GreaterThan(1.5f));
            Assert.That(HorizontalSpeed(), Is.LessThan(0.02f));
            Tick(default, Key.E);
            Assert.That(Vector3.Dot(player.ActualVelocity.normalized, player.transform.right), Is.GreaterThan(0.9f));
            Tick(default, Key.W, Key.LeftShift);
            Assert.That(HorizontalSpeed(), Is.GreaterThan(8f));
            Tick(default, Key.W, Key.C, Key.LeftShift);
            Assert.That(player.IsCrouched, Is.True);
            Assert.That(HorizontalSpeed(), Is.LessThan(1f));
            Tick(default, Key.W, Key.LeftAlt);
            Assert.That(player.IsCrouched, Is.False);
            Assert.That(HorizontalSpeed(), Is.EqualTo(ContinentExplorer.WalkSpeed).Within(0.12f));
        }

        [Test]
        public void GroundSkillsUseThePointerAndIgnoreMouseClicksAndModalInput()
        {
            WorldPosition land = terrain.WorldLanding;
            var target = new WorldPosition(land.X, 0d, land.Z + 3.5d);
            target = new WorldPosition(target.X, terrain.SurfaceHeight(target), target.Z);
            player.Look(0f, 24f);
            Camera view = Camera.main;
            Vector2 pointer = view.WorldToScreenPoint(terrain.ToLocal(target));
            Assert.That(view.pixelRect.Contains(pointer), Is.True);
            var free = new MouseState { position = pointer };

            Tick(Holding(free, MouseButton.Left));
            Tick(free);
            Tick(Holding(free, MouseButton.Right));
            Tick(free);
            Assert.That(terrain.EditedSamples, Is.Zero);

            Tick(free, Key.T, Key.Digit1);
            Tick(free);
            Tick(free, Key.Digit1);
            Tick(free, Key.Escape);
            Assert.That(terrain.EditedSamples, Is.Zero);
            Tick(free, Key.Digit1);
            Assert.That(terrain.EditedSamples, Is.Zero);
            Tick(free);

            Tick(free, Key.Digit1);
            int edited = terrain.EditedSamples;
            Assert.That(edited, Is.Positive);
            Tick(free, Key.Digit1);
            Assert.That(terrain.EditedSamples, Is.EqualTo(edited));
            Tick(free);
            var raised = new WorldPosition(target.X, 0, target.Z + 0.8);
            Vector3 raisedLocal = terrain.ToLocal(raised);
            raisedLocal.y = terrain.SurfaceHeight(raisedLocal.x, raisedLocal.z);
            free.position = view.WorldToScreenPoint(raisedLocal);
            Tick(free, Key.Digit2);
            Assert.That(terrain.EditedSamples, Is.Not.EqualTo(edited));

            int changed = terrain.EditedSamples;
            var steer = Holding(free, MouseButton.Right);
            player.ProcessInput(new ExplorerInput { Steer = true, Dig = true }, Dt, focused: false);
            Tick(steer, Key.Digit1);
            Tick(steer, Key.W);
            Assert.That(player.MouseGesture, Is.EqualTo(ExplorerMouseGesture.None));
            Assert.That(terrain.EditedSamples, Is.EqualTo(changed));
        }

        [Test]
        public void SkillsUseScreenCenterOnlyWhenSteeringAndOrbitCannotEdit()
        {
            player.Look(0f, 24f);
            var outside = new MouseState { position = new Vector2(-40f, -40f) };
            Tick(outside, Key.Digit1);
            Assert.That(terrain.EditedSamples, Is.Zero);
            Tick(outside);

            outside.delta = new Vector2(6f, 0f);
            Tick(Holding(outside, MouseButton.Left), Key.Digit1);
            Assert.That(player.MouseGesture, Is.EqualTo(ExplorerMouseGesture.Orbit));
            Assert.That(terrain.EditedSamples, Is.Zero);
            Tick();

            Tick(Holding(outside, MouseButton.Right), Key.Digit1);
            Assert.That(player.MouseGesture, Is.EqualTo(ExplorerMouseGesture.Steer));
            Assert.That(terrain.EditedSamples, Is.Positive);
        }

        private void Tick(MouseState state = default, params Key[] keys)
        {
            currentTime += Dt;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.QueueStateEvent(mouse, state);
            InputSystem.Update();
            ExplorerInput input = ExplorerInput.Read(keyboard, mouse);
            if (System.Array.Exists(keys, key => key == Key.F || key == Key.F1))
                Assert.That(input.ToggleFlight || input.Destination == Key.F1, Is.True,
                    $"Keyboard edge: pressed F {keyboard.fKey.isPressed}, F1 {keyboard.f1Key.isPressed}");
            player.ProcessInput(input, Dt, focused: true);
        }

        private static MouseState Holding(MouseState state, MouseButton button) => state.WithButton(button);

        private float HorizontalSpeed() => Vector3.ProjectOnPlane(player.ActualVelocity, Vector3.up).magnitude;
    }
}
