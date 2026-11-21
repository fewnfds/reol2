using System.Collections.Generic;
using System.IO;
using Reol2.Demo;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Reol2.Editor
{
    public static class VoxelDemoAuthoring
    {
        public const string ScenePath = "Assets/Scenes/VoxelDemo.unity";
        private const string Visuals = "Assets/Reol2/Visuals/";

        [MenuItem("Reol 2/Open Voxel Demo")]
        public static void OpenOrCreate()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                EditorSceneManager.OpenScene(ScenePath);
            else
                Create();
        }

        [MenuItem("Reol 2/Build Windows Demo")]
        public static void BuildWindowsDemo()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Create();

            const string directory = "Builds/VoxelDemo";
            Directory.CreateDirectory(directory);
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = directory + "/Reol2VoxelDemo.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new System.InvalidOperationException($"Voxel Demo build {report.summary.result}.");

            File.Copy("THIRD_PARTY_NOTICES.md", directory + "/THIRD_PARTY_NOTICES.md", overwrite: true);
        }

        public static void Create()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Color sky = new(0.68f, 0.77f, 0.76f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = sky;
            RenderSettings.fogStartDistance = 16f;
            RenderSettings.fogEndDistance = 55f;

            var cameraRoot = new GameObject("Main Camera", typeof(Camera));
            cameraRoot.tag = "MainCamera";
            Camera camera = cameraRoot.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = sky;
            camera.fieldOfView = 56f;
            camera.nearClipPlane = 0.08f;
            camera.farClipPlane = 80f;

            var sun = new GameObject("Afternoon sun", typeof(Light)).GetComponent<Light>();
            sun.transform.rotation = Quaternion.Euler(42f, 124f, 0f);
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.76f);
            sun.intensity = 1.18f;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;

            Shader voxelShader = AssetDatabase.LoadAssetAtPath<Shader>(Visuals + "VoxelSurface.shader");
            Material surface = GetMaterial("Voxel Surface", voxelShader, Color.white);
            Material gold = GetMaterial("Dig Outline", Shader.Find("Universal Render Pipeline/Unlit"), new Color(1f, 0.78f, 0.39f));
            Material jade = GetMaterial("Fill Outline", Shader.Find("Universal Render Pipeline/Unlit"), new Color(0.43f, 0.94f, 0.80f));
            var terrain = new GameObject("Editable terrain", typeof(VoxelDemoTerrain), typeof(VoxelDemoBenchmark))
                .GetComponent<VoxelDemoTerrain>();
            var terrainSettings = new SerializedObject(terrain);
            Reference(terrainSettings, "surfaceMaterial", surface);
            terrainSettings.ApplyModifiedPropertiesWithoutUndo();

            var player = new GameObject("Wanderer", typeof(CharacterController), typeof(VoxelDemoPlayer));
            player.layer = LayerMask.NameToLayer("Ignore Raycast");
            player.transform.SetPositionAndRotation(VoxelDemoTerrain.Trailhead, Quaternion.Euler(0f, 90f, 0f));
            var controller = player.GetComponent<CharacterController>();
            controller.height = 1.68f;
            controller.radius = 0.27f;
            controller.center = Vector3.up * (controller.height * 0.5f);
            controller.skinWidth = 0.032f;
            controller.stepOffset = 0.13f;
            controller.slopeLimit = 46f;
            controller.minMoveDistance = 0f;

            var robes = new Geometry();
            CreateWanderer(robes);
            MeshObject("Travel robes", robes.Asset("Travel Robes"), surface, player.transform);

            var details = new Geometry();
            CreateValleyDetails(details);
            MeshObject("Pines and trail lanterns", details.Asset("Valley Details"), surface, null);

            var distant = new Geometry();
            distant.Prism(new Vector3(11f, -6.1f, 13f), 9f, 10f, 4f, 5, new Color(0.43f, 0.55f, 0.50f));
            distant.Prism(new Vector3(23f, -8f, 0f), 13f, 11f, 4f, 6, new Color(0.49f, 0.62f, 0.57f));
            distant.Prism(new Vector3(14f, -6.7f, -14f), 10f, 9f, 2f, 5, new Color(0.47f, 0.57f, 0.52f));
            distant.Prism(new Vector3(0f, -8f, 20f), 16f, 10f, 3.6f, 6, new Color(0.45f, 0.56f, 0.52f));
            MeshObject("Distant peaks", distant.Asset("Distant Peaks"), surface, null);

            var playerSettings = new SerializedObject(player.GetComponent<VoxelDemoPlayer>());
            Reference(playerSettings, "terrain", terrain);
            Reference(playerSettings, "view", camera);
            Reference(playerSettings, "actions", AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions"));
            Reference(playerSettings, "digOutline", Outline("Dig range", gold, 0.512f));
            Reference(playerSettings, "fillOutline", Outline("Fill range", jade, 0.525f));
            Reference(playerSettings, "hint", CreateHud());
            playerSettings.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            buildScenes.RemoveAll(entry => entry.path == "Assets/Scenes/SampleScene.unity" || entry.path == ScenePath);
            buildScenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
            AssetDatabase.SaveAssets();
        }

        private static void CreateWanderer(Geometry g)
        {
            Color ink = new(0.16f, 0.27f, 0.25f);
            Color jade = new(0.28f, 0.49f, 0.43f);
            Color linen = new(0.83f, 0.77f, 0.59f);
            Color ochre = new(0.75f, 0.48f, 0.23f);
            g.Prism(new Vector3(-0.105f, 0.04f, 0f), 0.40f, 0.084f, 0.07f, 5, ink);
            g.Prism(new Vector3(0.105f, 0.04f, 0f), 0.40f, 0.084f, 0.07f, 5, ink);
            g.Prism(new Vector3(0f, 0.30f, 0f), 0.51f, 0.29f, 0.137f, 7, jade);
            g.Prism(new Vector3(0f, 0.80f, 0f), 0.10f, 0.154f, 0.15f, 7, ochre);
            g.Prism(new Vector3(0f, 0.90f, 0f), 0.39f, 0.155f, 0.206f, 6, jade);
            g.Prism(new Vector3(-0.19f, 0.79f, 0f), 0.43f, 0.086f, 0.080f, 5, ink);
            g.Prism(new Vector3(0.19f, 0.79f, 0f), 0.43f, 0.086f, 0.080f, 5, ink);
            g.Prism(new Vector3(0f, 1.26f, 0.015f), 0.25f, 0.112f, 0.102f, 6, new Color(0.60f, 0.37f, 0.24f));
            g.Prism(new Vector3(0f, 1.46f, 0.015f), 0.043f, 0.28f, 0.27f, 10, linen);
            g.Prism(new Vector3(0f, 1.50f, 0.015f), 0.19f, 0.27f, 0.048f, 10, ochre);
            g.Prism(new Vector3(-0.11f, 0.89f, -0.17f), 0.30f, 0.072f, 0.064f, 5, linen);
        }

        private static void CreateValleyDetails(Geometry g)
        {
            float[,] pines = { { -6.5f, 2.0f, 2.15f }, { -4.5f, 6.55f, 2.8f },
                               { -3.1f, 1.55f, 2.35f }, { -1.75f, 6.4f, 1.95f },
                               { 2.6f, 2.2f, 1.85f }, { 6.6f, 6.56f, 2.45f } };
            for (int i = 0; i < pines.GetLength(0); i++)
            {
                float x = pines[i, 0];
                float z = pines[i, 1];
                float height = pines[i, 2];
                Vector3 foot = new(x, VoxelDemoTerrain.GroundHeight(x, z) - 0.035f, z);
                g.Prism(foot, 0.67f * height, 0.10f, 0.056f, 6, new Color(0.39f, 0.29f, 0.22f));
                g.Prism(foot + Vector3.up * (height * 0.25f), height * 0.45f, height * 0.25f, 0.045f, 6, new Color(0.18f, 0.36f, 0.30f));
                g.Prism(foot + Vector3.up * (height * 0.55f), height * 0.45f, height * 0.19f, 0.035f, 6, new Color(0.25f, 0.46f, 0.35f));
            }

            foreach (Vector2 position in new[] { new Vector2(-3.6f, 3.18f), new Vector2(-0.28f, 3.62f), new Vector2(-0.28f, 5.38f) })
            {
                Vector3 foot = new(position.x, VoxelDemoTerrain.GroundHeight(position.x, position.y) - 0.055f, position.y);
                g.Prism(foot, 0.62f, 0.09f, 0.068f, 4, new Color(0.33f, 0.31f, 0.25f));
                g.Prism(foot + Vector3.up * 0.60f, 0.22f, 0.15f, 0.14f, 4, new Color(1f, 0.73f, 0.30f));
                g.Prism(foot + Vector3.up * 0.82f, 0.11f, 0.22f, 0.035f, 4, new Color(0.25f, 0.32f, 0.29f));
            }
        }

        private static Material GetMaterial(string name, Shader shader, Color color)
        {
            var path = Visuals + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;
            material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static LineRenderer Outline(string name, Material material, float half)
        {
            var line = new GameObject(name, typeof(LineRenderer)).GetComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.widthMultiplier = 0.016f;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            int[] path = { 0, 1, 3, 2, 0, 4, 5, 1, 5, 7, 3, 7, 6, 2, 6, 4 };
            var points = new Vector3[path.Length];
            for (int i = 0; i < points.Length; i++)
            {
                int c = path[i];
                points[i] = new Vector3((c & 1) == 0 ? -half : half,
                    (c & 2) == 0 ? -half : half, (c & 4) == 0 ? -half : half);
            }
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.enabled = false;
            return line;
        }

        private static Text CreateHud()
        {
            var root = new GameObject("Play controls", typeof(Canvas), typeof(CanvasScaler));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scale = root.GetComponent<CanvasScaler>();
            scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scale.referenceResolution = new Vector2(1280, 720);
            scale.matchWidthOrHeight = 0.5f;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Color ink = new(0.06f, 0.10f, 0.09f, 0.84f);
            Color paper = new(0.95f, 0.94f, 0.84f);

            Image panel = new GameObject("Control panel", typeof(Image)).GetComponent<Image>();
            panel.raycastTarget = false;
            panel.color = ink;
            Place(panel.rectTransform, root.transform, Vector2.zero, Vector2.zero, new Vector2(25f, 24f), new Vector2(462f, 84f));
            Label(panel.transform, font, "Reol 2", 20, new Vector2(15f, 51f), new Vector2(400f, 27f), paper);
            Label(panel.transform, font, "WASD  Move     Mouse  Look     R  Reset     Esc  Release", 14,
                new Vector2(16f, 4f), new Vector2(430f, 28f), paper);
            Text hint = Label(panel.transform, font, "Follow the lanterns up the slope", 15,
                new Vector2(16f, 28f), new Vector2(430f, 25f), new Color(0.93f, 0.82f, 0.55f));
            Text editKeys = Label(root.transform, font, "LMB  Dig     RMB  Fill", 14,
                Vector2.zero, new Vector2(170f, 20f), paper);
            Place(editKeys.rectTransform, root.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -14f), new Vector2(170f, 20f));
            editKeys.alignment = TextAnchor.UpperCenter;
            editKeys.gameObject.AddComponent<Shadow>().effectColor = new Color(0.03f, 0.08f, 0.07f, 0.92f);

            Image reticle = new GameObject("Reticle", typeof(Image)).GetComponent<Image>();
            reticle.raycastTarget = false;
            reticle.color = paper;
            Place(reticle.rectTransform, root.transform, Vector2.one * 0.5f, Vector2.one * 0.5f,
                Vector2.zero, new Vector2(4f, 4f));
            return hint;
        }

        private static Text Label(Transform parent, Font font, string message, int size, Vector2 at, Vector2 bounds, Color color)
        {
            Text text = new GameObject(message, typeof(Text)).GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.text = message;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            Place(text.rectTransform, parent, Vector2.zero, Vector2.zero, at, bounds);
            return text;
        }

        private static void Place(RectTransform rect, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 at, Vector2 size)
        {
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = at;
            rect.sizeDelta = size;
        }

        private static void Reference(SerializedObject settings, string name, Object value)
        {
            settings.FindProperty(name).objectReferenceValue = value;
        }

        private static void MeshObject(string name, Mesh mesh, Material material, Transform parent)
        {
            var root = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            root.transform.SetParent(parent, false);
            root.GetComponent<MeshFilter>().sharedMesh = mesh;
            root.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private sealed class Geometry
        {
            private readonly List<Vector3> vertices = new();
            private readonly List<Vector3> normals = new();
            private readonly List<Color32> colors = new();
            private readonly List<int> indices = new();

            internal void Prism(Vector3 foot, float height, float bottomRadius, float topRadius, int sides, Color color)
            {
                Vector3 top = foot + Vector3.up * height;
                for (int i = 0; i < sides; i++)
                {
                    Vector3 a = Radial(i, sides);
                    Vector3 b = Radial(i + 1, sides);
                    Vector3 lowerA = foot + a * bottomRadius;
                    Vector3 lowerB = foot + b * bottomRadius;
                    Vector3 upperA = top + a * topRadius;
                    Vector3 upperB = top + b * topRadius;
                    Face(lowerA, upperA, lowerB, color);
                    Face(lowerB, upperA, upperB, color);
                    Face(foot, lowerA, lowerB, color);
                    Face(top, upperB, upperA, color);
                }
            }

            private static Vector3 Radial(int step, int sides)
            {
                float angle = (step % sides + 0.5f) * (2f * Mathf.PI / sides);
                return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            }

            private void Face(Vector3 a, Vector3 b, Vector3 c, Color32 color)
            {
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                indices.Add(vertices.Count);
                indices.Add(vertices.Count + 1);
                indices.Add(vertices.Count + 2);
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                for (int i = 0; i < 3; i++)
                {
                    normals.Add(normal);
                    colors.Add(color);
                }
            }

            internal Mesh Asset(string name)
            {
                string path = Visuals + name + ".asset";
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh != null)
                    return mesh;

                mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetColors(colors);
                mesh.SetTriangles(indices, 0);
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
        }
    }
}
