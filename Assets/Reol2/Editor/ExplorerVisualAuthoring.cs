using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Reol2.World;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Reol2.Editor
{
    internal static class ExplorerVisualAuthoring
    {
        private const string Source = "Assets/Reol2/Characters/ThirdParty/Quaternius/";
        private const string Output = "Assets/Reol2/Characters/Player/";
        private const string PrefabPath = Output + "Explorer Body.prefab";
        private const string ModelPath = Source + "Superhero_Male_FullBody.fbx";
        private const string AnimationPath = Source + "UAL1_Standard.fbx";

        private static readonly (string Name, float Speed, float Rate)[] GroundGait =
        {
            ("Idle_Loop", 0f, 1f), ("Walk_Loop", ContinentExplorer.WalkSpeed, 1.30f),
            ("Jog_Fwd_Loop", ContinentExplorer.JogSpeed, 1.12f), ("Sprint_Loop", ContinentExplorer.SprintSpeed, 1.35f)
        };
        private static readonly (string Name, float Speed, float Rate)[] CrouchGait =
        {
            ("Crouch_Idle_Loop", 0f, 1f), ("Crouch_Fwd_Loop", ContinentExplorer.CrouchSpeed, 1.60f)
        };

        private static readonly string[] Takes =
        {
            "Idle_Loop", "Walk_Loop", "Jog_Fwd_Loop", "Sprint_Loop", "Crouch_Idle_Loop", "Crouch_Fwd_Loop",
            "Jump_Start", "Jump_Loop", "Jump_Land", "Swim_Idle_Loop", "Swim_Fwd_Loop", "Spell_Simple_Idle_Loop"
        };

        internal static void Attach(GameObject player)
        {
            var existing = player.GetComponentInChildren<PlayerAnimatorDriver>(true);
            if (existing != null)
            {
                existing.transform.localRotation = Quaternion.identity;
                PrefabUtility.RecordPrefabInstancePropertyModifications(existing.transform);
                return;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
                prefab = Create();
            Transform placeholder = player.transform.Find("Travel robes");
            if (placeholder != null)
                Object.DestroyImmediate(placeholder.gameObject);
            PrefabUtility.InstantiatePrefab(prefab, player.transform);
        }

        internal static void RefreshLocomotion()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Output + "Explorer.controller");
            if (controller == null)
                return;
            ImportHumanoid(AnimationPath, true);
            var states = controller.layers[0].stateMachine.states;
            var clips = LoadClips();
            foreach (var state in states)
            {
                if (state.state.name == "Ground")
                    ConfigureBlend((BlendTree)state.state.motion, clips, GroundGait);
                else if (state.state.name == "Crouch")
                    ConfigureBlend((BlendTree)state.state.motion, clips, CrouchGait);
            }
            AssetDatabase.SaveAssets();
        }

        private static GameObject Create()
        {
            Directory.CreateDirectory(Output);
            AssetDatabase.Refresh();
            ImportHumanoid(ModelPath, false);
            ImportHumanoid(AnimationPath, true);
            var controller = CreateController();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var body = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            body.name = "Explorer Body";

            Material clothing = Material("Traveller clothing", "Reol2/Explorer Clothing",
                "T_Superhero_Male_Dark.png", Color.white);
            Material eyes = Material("Traveller eyes", "Universal Render Pipeline/Lit", "T_Eye_Brown.png", Color.white);
            Material eyebrows = Material("Traveller eyebrows", "Universal Render Pipeline/Lit", null,
                new Color(0.20f, 0.11f, 0.08f));
            foreach (SkinnedMeshRenderer renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (renderer.name == "SuperHero_Male")
                {
                    renderer.sharedMesh = ColorClothing(renderer);
                    renderer.sharedMaterial = clothing;
                }
                else
                    renderer.sharedMaterial = renderer.name == "Eyes" ? eyes : eyebrows;
            }

            var animator = body.GetComponent<Animator>();
            if (animator == null)
                animator = body.AddComponent<Animator>();
            animator.avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().Single();
            if (!animator.avatar.isHuman || !animator.avatar.isValid)
                throw new InvalidOperationException("Explorer requires a valid Humanoid Avatar.");
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            body.AddComponent<PlayerAnimatorDriver>();
            body.transform.localScale = Vector3.one * 0.92f;
            body.transform.localRotation = Quaternion.identity;
            foreach (Transform child in body.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
            var prefab = PrefabUtility.SaveAsPrefabAsset(body, PrefabPath);
            Object.DestroyImmediate(body);
            AssetDatabase.SaveAssets();
            return prefab;
        }

        private static void ImportHumanoid(string path, bool hasClips)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = hasClips;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            if (hasClips)
            {
                var clips = new List<ModelImporterClipAnimation>();
                foreach (string name in Takes)
                {
                    ModelImporterClipAnimation clip = importer.defaultClipAnimations.Single(item => item.name.EndsWith("|" + name, StringComparison.Ordinal));
                    clip.name = name;
                    clip.loopTime = name.EndsWith("_Loop", StringComparison.Ordinal);
                    clip.loopPose = clip.loopTime;
                    clip.lockRootHeightY = true;
                    clip.lockRootPositionXZ = true;
                    clip.lockRootRotation = true;
                    // Remove the mean heading of each retargeted forward cycle. Rotation stays baked.
                    clip.rotationOffset = name switch
                    {
                        "Walk_Loop" => 7f,
                        "Jog_Fwd_Loop" => 28f,
                        "Sprint_Loop" => 20f,
                        _ => 0f
                    };
                    clips.Add(clip);
                }
                importer.clipAnimations = clips.ToArray();
            }
            importer.SaveAndReimport();
        }

        private static AnimatorController CreateController()
        {
            var clips = LoadClips();
            var controller = AnimatorController.CreateAnimatorControllerAtPath(Output + "Explorer.controller");
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var layers = controller.layers;
            layers[0].iKPass = true;
            controller.layers = layers;
            AnimatorStateMachine machine = layers[0].stateMachine;
            machine.defaultState = AddBlend("Ground", GroundGait);
            AddBlend("Crouch", CrouchGait);
            AddBlend("Swim", new[] { ("Swim_Idle_Loop", 0f, 1f), ("Swim_Fwd_Loop", 4f, 1f) });
            AddState("Takeoff", "Jump_Start", 3f);
            AddState("Air", "Jump_Loop", 1f);
            AddState("Land", "Jump_Land", 2.7f);
            AddState("Flight", "Spell_Simple_Idle_Loop", 1f);
            return controller;

            AnimatorState AddBlend(string name, (string Name, float Speed, float Rate)[] blend)
            {
                var tree = new BlendTree { name = name, blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false };
                AssetDatabase.AddObjectToAsset(tree, controller);
                ConfigureBlend(tree, clips, blend);
                var state = machine.AddState(name);
                state.motion = tree;
                return state;
            }

            void AddState(string name, string clip, float speed)
            {
                var state = machine.AddState(name);
                state.motion = clips[clip];
                state.speed = speed;
            }
        }

        private static Dictionary<string, AnimationClip> LoadClips() =>
            AssetDatabase.LoadAllAssetsAtPath(AnimationPath).OfType<AnimationClip>().ToDictionary(clip => clip.name);

        private static void ConfigureBlend(BlendTree tree, Dictionary<string, AnimationClip> clips,
            (string Name, float Speed, float Rate)[] gait)
        {
            tree.children = gait.Select(item => new ChildMotion
            {
                motion = clips[item.Name],
                threshold = item.Speed,
                timeScale = item.Rate
            }).ToArray();
            EditorUtility.SetDirty(tree);
        }

        private static Material Material(string name, string shader, string texture, Color color)
        {
            var material = new Material(Shader.Find(shader));
            material.SetColor("_BaseColor", color);
            if (texture != null)
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Source + texture));
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.19f);
            AssetDatabase.CreateAsset(material, Output + name + ".mat");
            return material;
        }

        private static Mesh ColorClothing(SkinnedMeshRenderer renderer)
        {
            var posed = new Mesh();
            renderer.BakeMesh(posed);
            Vector3[] vertices = posed.vertices;
            Object.DestroyImmediate(posed);
            var colors = new Color32[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = renderer.transform.TransformPoint(vertices[i]);
                float x = Mathf.Abs(p.x);
                float collar = 1.52f + Mathf.Clamp01(x / 0.26f) * 0.14f;
                Color32 tint;
                if (p.y > collar || x > 0.76f)
                    tint = new Color32(255, 255, 255, 0);
                else if (x > 0.70f)
                    tint = new Color32(130, 101, 62, 255);
                else if (p.y < 0.31f)
                    tint = new Color32(58, 52, 49, 255);
                else if (p.y < 0.88f)
                    tint = new Color32(90, 91, 79, 255);
                else if (p.y < 1.02f || x < 0.045f && p.y > 1.23f)
                    tint = new Color32(151, 115, 66, 255);
                else
                    tint = new Color32(64, 107, 110, 255);
                colors[i] = tint;
            }
            var mesh = Object.Instantiate(renderer.sharedMesh);
            mesh.name = "Traveller garment colours";
            mesh.colors32 = colors;
            AssetDatabase.CreateAsset(mesh, Output + "Traveller garment colours.asset");
            return mesh;
        }
    }
}
