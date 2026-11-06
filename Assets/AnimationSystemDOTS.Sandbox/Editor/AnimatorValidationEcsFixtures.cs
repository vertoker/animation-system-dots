using System;
using System.Linq;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimationSystemDOTS.Sandbox.Editor
{
    public static class AnimatorValidationEcsFixtures
    {
        [MenuItem("Tools/Animation System DOTS/Create Animator Validation ECS Scenes")]
        public static void Create()
        {
            var original = SceneManager.GetActiveScene();
            if (original.isDirty || string.IsNullOrEmpty(original.path)) throw new InvalidOperationException("Save the current scene first.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AnimatorValidationFixtures.EcsPrefabPath);
            var settings = AssetDatabase.LoadAssetAtPath<AnimatorValidationSettings>(AnimatorValidationFixtures.SettingsPath);
            if (prefab == null || settings == null) throw new InvalidOperationException("Generate the shared Animator validation assets first.");
            try { Build("06_AnimatorECS", 1, false, prefab, settings); Build("07_AnimatorBots100", 100, true, prefab, settings); }
            finally { if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original); }
            AssetDatabase.SaveAssets();
        }
        static void Build(string name, int count, bool bots, GameObject prefab, AnimatorValidationSettings settings)
        {
            var contentPath = SandboxPaths.Root + "Scenes/" + name + "Content.unity";
            var path = SandboxPaths.Root + "Scenes/" + name + ".unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(contentPath) == null)
            {
                var content = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(content);
                try
                {
                    var spawn = new GameObject("Shared Animator ECS Spawner", typeof(AnimatorValidationSpawner)).GetComponent<AnimatorValidationSpawner>();
                    spawn.Prefab = prefab; spawn.Settings = settings; spawn.Count = count; spawn.Bots = bots;
                    EditorSceneManager.SaveScene(content, contentPath);
                }
                finally { EditorSceneManager.CloseScene(content, true); }
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            try
            {
                var sub = new GameObject("ECS Characters", typeof(SubScene)).GetComponent<SubScene>(); sub.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(contentPath); sub.AutoLoadScene = true;
                var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(AnimatorValidationOrbit), typeof(EcsCharacterView)).GetComponent<Camera>();
                camera.tag = "MainCamera"; camera.nearClipPlane = .03f; camera.farClipPlane = 200; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .09f, .13f);
                var orbit = camera.GetComponent<AnimatorValidationOrbit>(); orbit.Pitch = bots ? 48 : 18; orbit.Distance = bots ? 42 : 4.5f;
                camera.transform.SetPositionAndRotation(bots ? new Vector3(0, 32, -28) : new Vector3(0, 2.4f, -4.5f), Quaternion.Euler(orbit.Pitch, 0, 0));
                var view = camera.GetComponent<EcsCharacterView>(); view.Orbit = orbit; view.Bots = bots;
                var adapter = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("AnimationSystemDOTS.Sandbox.Input.EcsAnimatorPlayer")).FirstOrDefault(t => t != null);
                if (adapter != null)
                { var input = camera.gameObject.AddComponent(adapter); var so = new SerializedObject(input); so.FindProperty("Character").objectReferenceValue = view; so.FindProperty("Orbit").objectReferenceValue = orbit; so.ApplyModifiedPropertiesWithoutUndo(); }
                var light = new GameObject("Directional Light", typeof(Light)).GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 2; light.shadows = LightShadows.Soft; light.transform.rotation = Quaternion.Euler(45, -30, 0);
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.35f, .38f, .45f);
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Arena analytic floor and bounds +/-24m"; floor.transform.position = new Vector3(0, -.05f, 0); floor.transform.localScale = new Vector3(50, .1f, 50);
                floor.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(SandboxPaths.Root + "Fixtures/Materials/Stage3Floor.mat"); Object.DestroyImmediate(floor.GetComponent<Collider>());
                for (var i = -4; i <= 4; i++)
                { var marker = GameObject.CreatePrimitive(PrimitiveType.Cube); marker.name = "Arena distance marker " + i * 5; marker.transform.position = new Vector3(i * 5, .005f, 0); marker.transform.localScale = new Vector3(.035f, .01f, 48); Object.DestroyImmediate(marker.GetComponent<Collider>()); }
                EditorSceneManager.SaveScene(scene, path);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
        [MenuItem("Tools/Animation System DOTS/Build Animator Validation Player")]
        public static void BuildPlayer()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before building.");
            System.IO.Directory.CreateDirectory("Artifacts/Builds/AnimatorValidation");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { SandboxPaths.Root + "Scenes/06_AnimatorECS.unity", SandboxPaths.Root + "Scenes/07_AnimatorBots100.unity" },
                locationPathName = "Artifacts/Builds/AnimatorValidation/AnimationSystemDOTS.exe", target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
            var result = report.summary.result + " errors=" + report.summary.totalErrors + " warnings=" + report.summary.totalWarnings + " duration=" + report.summary.totalTime;
            System.IO.Directory.CreateDirectory("Artifacts/Verification/AnimatorValidation"); System.IO.File.WriteAllText("Artifacts/Verification/AnimatorValidation/Player-build.txt", result);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new InvalidOperationException(result);
        }
    }
}
