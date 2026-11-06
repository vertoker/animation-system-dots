using System;
using System.Linq;
using AnimationSystemDOTS.Authoring;
using Unity.Entities;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimationSystemDOTS.Sandbox.Editor
{
    public sealed class ControllerCrowdBaker : Baker<ControllerCrowd>
    {
        public override void Bake(ControllerCrowd authoring)
        {
            if (authoring.Prefab == null) return;
            AddComponent(GetEntity(TransformUsageFlags.None), new ControllerCrowdSpawn { Prefab = GetEntity(authoring.Prefab, TransformUsageFlags.Dynamic), Count = authoring.Count });
        }
    }
    public static class ControllerCrowdFixtures
    {
        public static readonly string ScenePath = SandboxPaths.Root + "Scenes/02_Controller.unity";
        [MenuItem("Tools/Animation System DOTS/Create Controller 1000 Scene")]
        public static void Create()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) return;
            var original = SceneManager.GetActiveScene();
            if (original.isDirty || string.IsNullOrEmpty(original.path)) throw new InvalidOperationException("Save the active scene first.");
            var root = SandboxPaths.Root; var controllerPath = root + "Fixtures/Crowd.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                controller.AddParameter("Workout", AnimatorControllerParameterType.Bool);
                controller.AddParameter(new AnimatorControllerParameter { name = "Speed", type = AnimatorControllerParameterType.Float, defaultFloat = 1 });
                var machine = controller.layers[0].stateMachine;
                var walk = machine.AddState("Walk", new Vector3(280, 20)); walk.motion = Motion("Walk");
                var workout = machine.AddState("Workout", new Vector3(280, 160)); workout.motion = Motion("Workout");
                foreach (var state in new[] { walk, workout }) { state.writeDefaultValues = false; state.speedParameterActive = true; state.speedParameter = "Speed"; }
                machine.defaultState = walk;
                Edge(walk, workout, AnimatorConditionMode.If); Edge(workout, walk, AnimatorConditionMode.IfNot);
            }
            var source = PrefabUtility.LoadPrefabContents(HumanoidCrowdAssets.Root + "BaldMaleHumanoid.prefab");
            GameObject prefab;
            try
            {
                source.GetComponent<AnimationLibraryAuthoring>().Clips = Array.Empty<AnimationClip>();
                source.AddComponent<AnimationControllerAuthoring>().Controller = controller;
                prefab = PrefabUtility.SaveAsPrefabAsset(source, root + "Fixtures/ControllerHumanoid.prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(source); }
            var contentPath = root + "Scenes/02_ControllerContent.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            try
            {
                new GameObject("1000 ECS Controllers").AddComponent<ControllerCrowd>().Prefab = prefab;
                EditorSceneManager.SaveScene(scene, contentPath); EditorSceneManager.CloseScene(scene, true);
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
                var camera = new GameObject("Camera", typeof(Camera), typeof(ControllerCrowdView)).GetComponent<Camera>();
                camera.tag = "MainCamera"; camera.farClipPlane = 300; camera.backgroundColor = new Color(.07f, .09f, .13f); camera.clearFlags = CameraClearFlags.SolidColor;
                camera.GetComponent<ControllerCrowdView>().View(false);
                var light = new GameObject("Directional Light", typeof(Light)).GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 2;
                light.shadows = LightShadows.Soft; light.transform.rotation = Quaternion.Euler(50, -30, 0);
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Crowd Ground";
                floor.transform.localScale = new Vector3(110, .1f, 80); floor.transform.position = new Vector3(0, -.075f, 0);
                floor.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(root + "Fixtures/Materials/Stage3Floor.mat");
                var subScene = new GameObject("Controller Content").AddComponent<SubScene>(); subScene.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(contentPath); subScene.AutoLoadScene = true;
                EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            }
        }
        static AnimationClip Motion(string name)
            => AssetDatabase.LoadAllAssetsAtPath(HumanoidCrowdAssets.Root + "Motions/" + name + ".fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
        static void Edge(AnimatorState from, AnimatorState to, AnimatorConditionMode condition)
        {
            var edge = from.AddTransition(to); edge.hasExitTime = false; edge.hasFixedDuration = true; edge.duration = .3f;
            edge.AddCondition(condition, 0, "Workout");
        }
    }
}
