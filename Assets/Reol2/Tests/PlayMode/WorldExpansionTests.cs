using System.Collections;
using NUnit.Framework;
using Reol2.World;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Reol2.Tests
{
    public sealed class WorldExpansionTests
    {
        [Test]
        public void ClockAdvancesArithmeticallyAndWeatherIsContinuousAcrossSlots()
        {
            var clock = new WorldClock();
            long initial = clock.Tick;
            clock.Paused = true;
            clock.Advance(WorldClock.Day);
            Assert.That(clock.Tick, Is.EqualTo(initial));
            clock.Paused = false;
            clock.Advance(18L * WorldClock.Year + WorldClock.Day);
            Assert.That(clock.DayFraction, Is.EqualTo(0.375f));

            long boundary = 92L * WorldWeather.Interval;
            var a = new WorldPosition(-32768.5, 840, 135.25);
            var b = new WorldPosition(-32767.5, 840, 135.25);
            var lower = new WorldPosition(a.X, 30, a.Z);
            ClimateSample climate = WorldClimate.Sample(a, 850, boundary, 14376);
            Assert.That(climate.Temperature, Is.LessThan(WorldClimate.Sample(lower, 30, boundary, 14376).Temperature - 4f));
            WeatherSample before = WorldWeather.Sample(a, boundary - 1, climate, 657);
            WeatherSample after = WorldWeather.Sample(b, boundary + 1, climate, 657);
            Assert.That(after.Cloud, Is.EqualTo(before.Cloud).Within(0.002f));
            Assert.That(after.Rain, Is.EqualTo(before.Rain).Within(0.002f));
            Assert.That(after.Cloud, Is.EqualTo(WorldWeather.Sample(b, boundary + 1, climate, 657).Cloud));
        }

        [Test]
        public void OreExistsOnlyInsideItsOwningRegionAndItsHostRock()
        {
            const ulong seed = 14376;
            bool found = false;
            for (int i = -40; i < -5; i++)
            {
                var region = new Vector3Int(-WorldDefinition.Extent / 128, -200, i);
                if (!WorldResources.TryVein(region, seed, out MineralVein vein))
                    continue;
                found = true;
                Assert.That(WorldResources.Region(vein.Center), Is.EqualTo(region));
                Assert.That(WorldResources.At(vein.Center, RockLayer.Basalt, seed), Is.Not.EqualTo(Mineral.None));
                Assert.That(WorldResources.At(vein.Center, RockLayer.Soil, seed), Is.EqualTo(Mineral.None));
                var boundary = new WorldPosition(region.x * 128, vein.Center.Y, vein.Center.Z);
                Assert.That(WorldResources.At(boundary, RockLayer.Basalt, seed), Is.EqualTo(Mineral.None));
            }
            Assert.That(found, Is.True);
            Assert.That(WorldResources.TryVein(new Vector3Int(0, 259, 0), seed, out _), Is.False);
        }

        [UnityTest]
        public IEnumerator LargeWorldRuntimeChangesSkyAndFindsActualTerrain()
        {
            yield return SceneManager.LoadSceneAsync("LargeWorld", LoadSceneMode.Single);
            yield return null;
            var player = Object.FindFirstObjectByType<ContinentExplorer>();
            var terrain = Object.FindFirstObjectByType<ContinentTerrain>();
            player.enabled = false;
            Assert.That(Camera.main.clearFlags, Is.EqualTo(CameraClearFlags.Skybox));
            float daylight = RenderSettings.sun.intensity;
            player.Environment.Clock.Advance(15 * WorldClock.Hour);
            player.UpdateView();
            Assert.That(RenderSettings.sun.intensity, Is.LessThan(daylight * 0.5f));
            WorldPosition landing = terrain.WorldLanding;
            var ground = new WorldPosition(landing.X + 4, 0, landing.Z);
            Vector3 probe = terrain.ToLocal(new WorldPosition(ground.X, terrain.SurfaceHeight(ground) + 2, ground.Z));
            Assert.That(terrain.Pick(new Ray(probe, Vector3.down), probe,
                out _, out _, out WorldPosition point, out bool placed), Is.True);
            Assert.That(placed, Is.False);
            Assert.That(terrain.Prospect(point, placed), Is.Not.Empty);
            Assert.That(terrain.Prospect(point, true), Is.EqualTo("Placed earth"));

            player.Environment.PreviewWeather = new WeatherSample(1f, 0.9f, 0f, 0.8f, 0.8f);
            player.Environment.Clock.Advance(120);
            player.UpdateView();
            yield return new WaitForSeconds(0.3f);
            Assert.That(player.Environment.ActivePrecipitation, Is.GreaterThan(0));
            Assert.That(player.Environment.ActivePrecipitation, Is.LessThanOrEqualTo(1070));
            Assert.That(player.EnterAt(terrain.OceanEntry), Is.True);
            player.Look(0f, -24f);
            Assert.That(Camera.main.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
            Assert.That(player.Environment.ActivePrecipitation, Is.Zero);
        }

        [UnityTest]
        public IEnumerator RemovingEnvironmentRestoresSceneFogAndSun()
        {
            yield return SceneManager.LoadSceneAsync("LargeWorld", LoadSceneMode.Single);
            yield return null;
            var player = Object.FindFirstObjectByType<ContinentExplorer>();
            var terrain = Object.FindFirstObjectByType<ContinentTerrain>();
            player.enabled = false;
            Object.Destroy(player.Environment);
            yield return null;

            Light sun = RenderSettings.sun;
            float intensity = sun.intensity;
            Color fog = RenderSettings.fogColor;
            float fogEnd = RenderSettings.fogEndDistance;
            Camera view = Camera.main;
            float farClip = view.farClipPlane;
            var environment = player.gameObject.AddComponent<WorldEnvironment>();
            environment.Clock.Advance(15 * WorldClock.Hour);
            environment.Apply(view, terrain, player.WorldFeet, underwater: true, underground: false);
            Assert.That(RenderSettings.fogEndDistance, Is.EqualTo(210f));
            Assert.That(sun.intensity, Is.LessThan(intensity));

            Object.Destroy(environment);
            yield return null;
            Assert.That(RenderSettings.fogColor, Is.EqualTo(fog));
            Assert.That(RenderSettings.fogEndDistance, Is.EqualTo(fogEnd));
            Assert.That(sun.intensity, Is.EqualTo(intensity));
            Assert.That(view.farClipPlane, Is.EqualTo(farClip));
        }

        [UnityTest]
        public IEnumerator PlayerJumpsAndCannotStandUnderALowCeiling()
        {
            yield return SceneManager.LoadSceneAsync("LargeWorld", LoadSceneMode.Single);
            yield return null;
            var player = Object.FindFirstObjectByType<ContinentExplorer>();
            player.enabled = false;
            var driver = player.GetComponentInChildren<PlayerAnimatorDriver>();
            Assert.That(driver.Animator.isHuman && driver.Animator.avatar.isValid, Is.True);
            Assert.That(driver.Animator.applyRootMotion, Is.False);
            Assert.That(Quaternion.Angle(driver.transform.localRotation, Quaternion.identity), Is.LessThan(0.01f));
            Assert.That(player.EnterAt(new WorldPosition(5000.5, 20000.15, -5000.5)), Is.True);
            Vector3 start = player.transform.position;
            Cube(start - Vector3.up * 0.65f, new Vector3(8f, 1f, 8f));
            Physics.SyncTransforms();
            const float dt = 1f / 60f;
            for (int i = 0; i < 25; i++)
                player.MoveForFrame(Vector2.zero, 0f, false, false, dt);
            Assert.That(player.Grounded, Is.True);
            double ground = player.WorldFeet.Y;
            player.MoveForFrame(Vector2.zero, 0f, false, false, dt, jump: true, crouch: false);
            Assert.That(driver.Pose, Is.EqualTo(ExplorerPose.Takeoff));
            for (int i = 0; i < 14; i++)
                player.MoveForFrame(Vector2.zero, 0f, false, false, dt);
            Assert.That(player.WorldFeet.Y, Is.GreaterThan(ground + 0.6));
            Assert.That(driver.Pose, Is.EqualTo(ExplorerPose.Air));
            for (int i = 0; i < 45; i++)
                player.MoveForFrame(Vector2.zero, 0f, false, false, dt);
            Assert.That(player.Grounded, Is.True);

            player.MoveForFrame(Vector2.zero, 0f, false, false, dt, jump: false, crouch: true);
            Assert.That(player.GetComponent<CharacterController>().height, Is.EqualTo(1.10f).Within(0.001f));
            GameObject ceiling = Cube(player.transform.position + Vector3.up * 1.52f, new Vector3(2f, 0.3f, 2f));
            Physics.SyncTransforms();
            player.MoveForFrame(Vector2.zero, 0f, false, false, dt);
            Assert.That(player.IsCrouched, Is.True);
            ceiling.SetActive(false);
            player.MoveForFrame(Vector2.zero, 0f, false, false, dt);
            Assert.That(player.IsCrouched, Is.False);
            Assert.That(player.ActualVelocity.x, Is.EqualTo(0f).Within(0.001f));
            player.MoveForFrame(Vector2.zero, 0f, true, true, dt);
            yield return null;
            Assert.That(driver.Pose, Is.EqualTo(ExplorerPose.Flight));
            Assert.That(driver.IkWeight, Is.Zero);
        }

        [UnityTest]
        public IEnumerator WalksAThirtyDegreeRampAndALowStepThenStopsAtAHighStep()
        {
            yield return SceneManager.LoadSceneAsync("LargeWorld", LoadSceneMode.Single);
            yield return null;
            var player = Object.FindFirstObjectByType<ContinentExplorer>();
            player.enabled = false;
            Assert.That(player.EnterAt(new WorldPosition(2500.5, 20000.15, -2500.5)), Is.True);
            Vector3 start = player.transform.position;
            player.Look(0f, 8f);
            Cube(start + new Vector3(0, -0.65f, 0), new Vector3(8, 1, 18));
            var ramp = Cube(start + new Vector3(0, 0.61f, 2.62f), new Vector3(2f, 0.18f, 3f));
            ramp.transform.rotation = Quaternion.Euler(-30, 0, 0);
            Cube(start + new Vector3(0, 0.25f, 4.45f), new Vector3(2, 2.4f, 1.26f));
            Cube(start + new Vector3(0, 0.31f, 5.45f), new Vector3(2, 2.59f, 1.2f));
            Cube(start + new Vector3(0, 0.32f, 6.55f), new Vector3(2, 3.37f, 1f));
            Physics.SyncTransforms();
            const float dt = 1f / 60f;
            for (int i = 0; i < 26; i++)
                player.MoveForFrame(Vector2.zero, 0, false, false, dt);
            for (int i = 0; i < 104; i++)
                player.MoveForFrame(Vector2.up * 0.5f, 0, false, false, dt);
            Assert.That(player.transform.position.y, Is.GreaterThan(start.y + 1.1f));
            for (int i = 0; i < 155; i++)
                player.MoveForFrame(Vector2.up * 0.5f, 0, false, false, dt);
            Assert.That(player.transform.position.z - start.z, Is.InRange(5.0f, 6.1f));
            Assert.That(player.transform.position.y, Is.GreaterThan(start.y + 1.5f));
            Assert.That(player.ActualVelocity.z, Is.EqualTo(0f).Within(0.15f));
            Assert.That(player.Grounded, Is.True);
        }

        [UnityTest]
        public IEnumerator ForwardGaitsKeepTheRetargetedBodyAlignedWithTravel()
        {
            yield return SceneManager.LoadSceneAsync("LargeWorld", LoadSceneMode.Single);
            yield return null;
            var player = Object.FindFirstObjectByType<ContinentExplorer>();
            player.enabled = false;
            Assert.That(player.EnterAt(new WorldPosition(2500.5, 20000.15, -2500.5)), Is.True);
            Vector3 feet = player.transform.position;
            Cube(feet - Vector3.up * 0.65f, new Vector3(16, 1, 16));
            Physics.SyncTransforms();
            player.Look(37f, 10f);
            const float dt = 1f / 60f;
            for (int i = 0; i < 30; i++)
                player.MoveForFrame(Vector2.up, 0f, false, false, dt);
            Vector3 heading = Quaternion.Euler(0f, 37f, 0f) * Vector3.forward;
            Assert.That(Vector3.Angle(heading, Vector3.ProjectOnPlane(player.ActualVelocity, Vector3.up)), Is.LessThan(0.1f));
            Assert.That(Vector3.Angle(heading, player.transform.forward), Is.LessThan(0.1f));
            Assert.That(player.ActualVelocity.magnitude, Is.EqualTo(ContinentExplorer.JogSpeed).Within(0.05f));

            var visual = player.GetComponentInChildren<Animator>();
            var animator = Object.Instantiate(visual, Vector3.zero, Quaternion.identity);
            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
            animator.runtimeAnimatorController = null;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            try
            {
                foreach (string name in new[] { "Walk_Loop", "Jog_Fwd_Loop", "Sprint_Loop", "Crouch_Fwd_Loop" })
                {
                    AnimationClip clip = System.Array.Find(clips, item => item.name == name);
                    Assert.That(clip, Is.Not.Null, name);
                    var graph = PlayableGraph.Create();
                    try
                    {
                        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                        var playable = AnimationClipPlayable.Create(graph, clip);
                        AnimationPlayableOutput.Create(graph, "Check forward gait", animator).SetSourcePlayable(playable);
                        graph.Play();
                        Transform leftShoulder = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                        Transform rightShoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                        Transform leftHip = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                        Transform rightHip = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                        Vector3 shoulderSum = Vector3.zero, hipSum = Vector3.zero;
                        for (int i = 0; i < 40; i++)
                        {
                            playable.SetTime(clip.length * (i / 40d));
                            graph.Evaluate(0f);
                            shoulderSum += Vector3.ProjectOnPlane(rightShoulder.position - leftShoulder.position, Vector3.up).normalized;
                            hipSum += Vector3.ProjectOnPlane(rightHip.position - leftHip.position, Vector3.up).normalized;
                        }
                        Assert.That(Vector3.Angle(shoulderSum, Vector3.right), Is.LessThan(8f), name + " shoulders");
                        Assert.That(Vector3.Angle(hipSum, Vector3.right), Is.LessThan(8f), name + " hips");
                    }
                    finally
                    {
                        graph.Destroy();
                    }
                }
            }
            finally
            {
                Object.Destroy(animator.gameObject);
            }
        }

        private static GameObject Cube(Vector3 position, Vector3 scale)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetPositionAndRotation(position, Quaternion.identity);
            cube.transform.localScale = scale;
            return cube;
        }
    }
}
