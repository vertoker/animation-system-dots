using System;
using System.Linq;
using AnimationSystemDOTS.Authoring;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace AnimationSystemDOTS.Sandbox.Editor
{
    public static class HumanoidCrowdFixtures
    {
        public static readonly string ScenePath = (SandboxPaths.Root + "Scenes/04_HumanoidCrowd1000.unity");
        static readonly string ContentPath = (SandboxPaths.Root + "Scenes/04_HumanoidCrowd1000Content.unity");
        [MenuItem("Tools/Animation System DOTS/Rebuild Humanoid Crowd Prefab")]
        public static void RebuildPrefab()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject rig = null;
            try
            {
                var root = HumanoidCrowdAssets.Root;
                rig = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HumanoidCrowdAssets.ModelPath), new InstantiateParameters { scene = scene });
                rig.name = "BaldMaleHumanoid";
                var animator = rig.GetComponent<Animator>(); animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
                var material = AssetDatabase.LoadAssetAtPath<Material>(root + "Materials/BaldMaleCompute.mat");
                foreach (var renderer in rig.GetComponentsInChildren<SkinnedMeshRenderer>())
                { renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMesh.subMeshCount).ToArray(); renderer.shadowCastingMode = ShadowCastingMode.On; }
                rig.AddComponent<AnimationRigAuthoring>().Settings = AssetDatabase.LoadAssetAtPath<AnimationBakeSettings>(root + "CrowdBakeSettings.asset");
                rig.AddComponent<AnimationLibraryAuthoring>().Clips = HumanoidCrowdAssets.Motions.Select(name =>
                    AssetDatabase.LoadAllAssetsAtPath(root + "Motions/" + name + ".fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"))).ToArray();
                PrefabUtility.SaveAsPrefabAsset(rig, root + "BaldMaleHumanoid.prefab");
            }
            finally { if (rig != null) UnityEngine.Object.DestroyImmediate(rig); EditorSceneManager.ClosePreviewScene(scene); }
        }
        [MenuItem("Tools/Animation System DOTS/Create Humanoid Crowd 1000 Scene")]
        public static void Create()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) return;
            var original = SceneManager.GetActiveScene();
            if (original.isDirty || string.IsNullOrEmpty(original.path)) throw new InvalidOperationException("Save the active scene before creating the humanoid crowd.");
            var root = HumanoidCrowdAssets.Root;
            var settings = ScriptableObject.CreateInstance<AnimationBakeSettings>();
            settings.SampleRate = 240; settings.PositionTolerance = .002f; settings.AngleToleranceDegrees = .25f;
            settings.FixtureRevision = "humanoid-workout-bindpose-facing-2";
            AssetDatabase.CreateAsset(settings, root + "CrowdBakeSettings.asset");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(root + "Materials/HumanoidComputeDeformation.shadergraph");
            if (shader == null) throw new InvalidOperationException("Textured Compute Deformation graph is missing.");
            var material = new Material(shader) { name = "BaldMaleCompute" };
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(root + "Textures/MaleBaseColor.png"));
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(root + "Textures/MaleNormal.png"));
            AssetDatabase.CreateAsset(material, root + "Materials/BaldMaleCompute.mat");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            try
            {
                var rig = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HumanoidCrowdAssets.ModelPath)); rig.name = "BaldMaleHumanoid";
                var animator = rig.GetComponent<Animator>(); animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
                foreach (var renderer in rig.GetComponentsInChildren<SkinnedMeshRenderer>())
                { renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMesh.subMeshCount).ToArray(); renderer.shadowCastingMode = ShadowCastingMode.On; }
                rig.AddComponent<AnimationRigAuthoring>().Settings = settings;
                rig.AddComponent<AnimationLibraryAuthoring>().Clips = HumanoidCrowdAssets.Motions.Select(name =>
                    AssetDatabase.LoadAllAssetsAtPath(root + "Motions/" + name + ".fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"))).ToArray();
                var prefab = PrefabUtility.SaveAsPrefabAsset(rig, root + "BaldMaleHumanoid.prefab"); UnityEngine.Object.DestroyImmediate(rig);
                var spawn = new GameObject("1000 Humanoids — four motions").AddComponent<HumanoidCrowd>(); spawn.Prefab = prefab;
                EditorSceneManager.SaveScene(scene, ContentPath); EditorSceneManager.CloseScene(scene, true);
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
                var camera = new GameObject("Camera", typeof(Camera), typeof(HumanoidCrowdView), typeof(HumanoidCrowdCapture)).GetComponent<Camera>();
                camera.tag = "MainCamera"; camera.farClipPlane = 300; camera.backgroundColor = new Color(.07f, .09f, .13f); camera.clearFlags = CameraClearFlags.SolidColor;
                camera.GetComponent<HumanoidCrowdView>().SetView(false);
                var light = new GameObject("Directional Light", typeof(Light)).GetComponent<Light>();
                light.type = LightType.Directional; light.intensity = 2; light.shadows = LightShadows.Soft; light.transform.rotation = Quaternion.Euler(50, -30, 0);
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Crowd Ground";
                floor.transform.localScale = new Vector3(110, .1f, 80); floor.transform.position = new Vector3(0, -.075f, 0);
                floor.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>((SandboxPaths.Root + "Fixtures/Materials/Stage3Floor.mat"));
                var content = new GameObject("Humanoid Crowd Content").AddComponent<SubScene>();
                content.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ContentPath); content.AutoLoadScene = true;
                EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            }
        }

        [MenuItem("Tools/Animation System DOTS/Build Humanoid Crowd Player")]
        public static void BuildPlayer()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before building the crowd Player.");
            System.IO.Directory.CreateDirectory("Artifacts/Builds/HumanoidCrowd");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = "Artifacts/Builds/HumanoidCrowd/AnimationSystemDOTS.exe",
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            var text = report.summary.result + " errors=" + report.summary.totalErrors + " warnings=" + report.summary.totalWarnings
                + " bytes=" + report.summary.totalSize + " duration=" + report.summary.totalTime;
            System.IO.File.WriteAllText("Artifacts/Verification/humanoid-final-build.txt", text);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new InvalidOperationException(text);
        }
    }
}
