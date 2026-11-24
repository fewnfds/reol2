using System;
using System.Collections.Generic;
using System.IO;
using Reol2.World;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Reol2.Editor
{
    public static class ContinentAuthoring
    {
        private const string ScenePath = "Assets/Scenes/ContinentDemo.unity";
        private const string LargeWorldPath = "Assets/Scenes/LargeWorld.unity";
        private const string OceanPath = "Assets/Reol2/World/Continent Ocean.mat";
        private const string Visuals = "Assets/Reol2/Visuals/";

        [MenuItem("Reol 2/Open Continent Demo")]
        public static void OpenDemo()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Create();
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Reol 2/Open Large World")]
        public static void OpenLargeWorld()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            CreateLargeWorld();
            EditorSceneManager.OpenScene(LargeWorldPath);
        }

        public static void Create() => CreateScene(ScenePath, false);
        public static void CreateLargeWorld() => CreateScene(LargeWorldPath, true);

        public static void UpdateWorldScenes()
        {
            ExplorerVisualAuthoring.RefreshLocomotion();
            UpdateWorldScene(ScenePath);
            UpdateWorldScene(LargeWorldPath);
        }

        private static void UpdateWorldScene(string path)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var player = UnityEngine.Object.FindFirstObjectByType<ContinentExplorer>();
            ConfigureEnvironment(player.gameObject);
            ExplorerVisualAuthoring.Attach(player.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        private static void ConfigureEnvironment(GameObject player)
        {
            if (!player.TryGetComponent(out WorldEnvironment environment))
                environment = player.AddComponent<WorldEnvironment>();
            var setup = new SerializedObject(environment);
            setup.FindProperty("skyTemplate").objectReferenceValue = GetVisualMaterial("WorldSky", "World Sky");
            setup.FindProperty("precipitationMaterial").objectReferenceValue = GetVisualMaterial("Precipitation", "Precipitation");
            setup.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Material GetVisualMaterial(string shaderFile, string materialName)
        {
            string path = Visuals + materialName + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;
            material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Visuals + shaderFile + ".shader"));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void CreateScene(string path, bool large)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.67f, 0.80f, 0.82f);
            RenderSettings.fogStartDistance = 360f;
            RenderSettings.fogEndDistance = 2300f;

            var view = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
            view.gameObject.tag = "MainCamera";
            view.clearFlags = CameraClearFlags.SolidColor;
            view.nearClipPlane = 0.09f;
            view.farClipPlane = 2400f;
            view.fieldOfView = 64f;
            var sun = new GameObject("Afternoon Sun", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(35f, -32f, 0f);
            sun.color = new Color(1f, 0.94f, 0.83f);
            sun.intensity = 1.05f;
            RenderSettings.sun = sun;

            Material terrainMaterial = AssetDatabase.LoadAssetAtPath<Material>(Visuals + "Voxel Surface.mat");
            var terrain = new GameObject("Continent", typeof(ContinentTerrain)).GetComponent<ContinentTerrain>();
            var terrainSetup = new SerializedObject(terrain);
            terrainSetup.FindProperty("terrainMaterial").objectReferenceValue = terrainMaterial;
            terrainSetup.FindProperty("oceanMaterial").objectReferenceValue = GetOcean();
            terrainSetup.FindProperty("largeWorld").boolValue = large;
            terrainSetup.ApplyModifiedPropertiesWithoutUndo();

            var player = new GameObject("Explorer", typeof(CharacterController), typeof(ContinentExplorer));
            ConfigureEnvironment(player);
            player.layer = LayerMask.NameToLayer("Ignore Raycast");
            var motor = player.GetComponent<CharacterController>();
            motor.height = 1.68f;
            motor.radius = 0.27f;
            motor.center = Vector3.up * 0.84f;
            motor.stepOffset = 0.22f;
            motor.skinWidth = 0.032f;
            motor.slopeLimit = 48f;
            motor.minMoveDistance = 0f;
            ExplorerVisualAuthoring.Attach(player);

            var playerSetup = new SerializedObject(player.GetComponent<ContinentExplorer>());
            playerSetup.FindProperty("terrain").objectReferenceValue = terrain;
            playerSetup.FindProperty("view").objectReferenceValue = view;
            playerSetup.FindProperty("selection").objectReferenceValue = MakeSelection();
            playerSetup.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, path);
            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!buildScenes.Exists(item => item.path == path))
                buildScenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Reol 2/Build Windows Continent")]
        public static void BuildWindows()
        {
            Create();
            BuildScene(ScenePath, "Builds/ContinentDemo/Reol2Continent.exe");
        }

        [MenuItem("Reol 2/Build Windows Large World")]
        public static void BuildLargeWorld()
        {
            CreateLargeWorld();
            string executable = BuildScene(LargeWorldPath, "Builds/LargeWorld/Reol2World.exe");
            const string region = "-landforms 64 -world-seed 20261121";
            WriteLauncher(executable, "Play-Terrain-Demo.cmd", region + " -terrain-demo");
            WriteLauncher(executable, "Play-River-Demo.cmd", region + " -river-demo");
        }

        private static void WriteLauncher(string executable, string name, string arguments)
        {
            string play = Path.Combine(Path.GetDirectoryName(executable), name);
            File.WriteAllLines(play, new[]
            {
                "@echo off",
                "pushd \"%~dp0\"",
                "setlocal",
                $"set \"player=%~dp0{Path.GetFileName(executable)}\"",
                $"set \"world={arguments}\"",
                "if \"%~1\"==\"\" (",
                "    start \"\" \"%player%\" %world% -screen-fullscreen 1 -window-mode borderless -reol-native-resolution",
                ") else (",
                "    start \"\" \"%player%\" %world% %*",
                ")",
                "popd"
            });
        }

        private static string BuildScene(string scene, string defaultOutput)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int outputArgument = Array.IndexOf(arguments, "-buildOutput");
            string executable = outputArgument >= 0 && outputArgument + 1 < arguments.Length
                ? arguments[outputArgument + 1] : defaultOutput;
            executable = Path.GetFullPath(executable);
            string directory = Path.GetDirectoryName(executable);
            Directory.CreateDirectory(directory);
            BuildReport report = BuildPipeline.BuildPlayer(WindowsTestBuild.Options(scene, executable));
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Continent build {report.summary.result}.");
            File.Copy("THIRD_PARTY_NOTICES.md", Path.Combine(directory, "THIRD_PARTY_NOTICES.md"), overwrite: true);
            return executable;
        }

        private static Material GetOcean()
        {
            var ocean = AssetDatabase.LoadAssetAtPath<Material>(OceanPath);
            if (ocean != null)
                return ocean;
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Reol2/World/ContinentOcean.shader");
            ocean = new Material(shader);
            AssetDatabase.CreateAsset(ocean, OceanPath);
            return ocean;
        }

        private static LineRenderer MakeSelection()
        {
            const float s = 0.51f;
            Vector3 a = new(-s, -s, -s), b = new(s, -s, -s), c = new(s, -s, s), d = new(-s, -s, s);
            Vector3 rise = Vector3.up * (s * 2f);
            var line = new GameObject("Dig cell").AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.widthMultiplier = 0.018f;
            line.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Visuals + "Dig Outline.mat");
            Vector3[] points = { a, b, c, d, a, a + rise, b + rise, b, b + rise, c + rise, c, c + rise, d + rise, d, d + rise, a + rise };
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.enabled = false;
            return line;
        }
    }
}
