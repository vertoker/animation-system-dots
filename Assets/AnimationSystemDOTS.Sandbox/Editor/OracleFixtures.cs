using AnimationSystemDOTS.Sandbox.Editor;
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using AnimationSystemDOTS.Authoring;

namespace AnimationSystemDOTS.Editor.Verification
{
    public static class OracleFixtures
    {
        public static readonly string Root = (SandboxPaths.Root + "Fixtures/");

        [MenuItem("Tools/Animation System DOTS/Create Missing Validation Fixtures")]
        public static void CreateSandbox()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory((SandboxPaths.Root + "Settings"));
            AssetDatabase.Refresh();
            var settingsPath = (SandboxPaths.Root + "Settings/AnimationValidationSettings.asset");
            if (AssetDatabase.LoadAssetAtPath<AnimationValidationSettings>(settingsPath) == null)
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<AnimationValidationSettings>(), settingsPath);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Root + "GenericRig.prefab") != null) return;
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject rig = null;
            try
            {
                rig = new GameObject("GenericRig");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(rig, scene);
                var skeleton = Bone("Skeleton", rig.transform, new Vector3(0.2f, 0.3f, 0));
                var left = Bone("Left", skeleton, Vector3.zero);
                var right = Bone("Right", skeleton, new Vector3(0, 1, 0));
                var a = Bone("Joint", left, Vector3.zero);
                var b = Bone("Joint", right, Vector3.zero);
                var mesh = new Mesh { name = "AsymmetricTwoBone" };
                mesh.vertices = new[] { new Vector3(-0.4f, 0, 0), new Vector3(1.7f, 0, 0), new Vector3(0.1f, 1, 0), new Vector3(1.2f, 1.5f, 0) };
                mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
                mesh.subMeshCount = 2;
                mesh.SetTriangles(new[] { 0, 2, 1 }, 0);
                mesh.SetTriangles(new[] { 1, 2, 3 }, 1);
                mesh.bindposes = new[] { a.worldToLocalMatrix * rig.transform.localToWorldMatrix, b.worldToLocalMatrix * rig.transform.localToWorldMatrix };
                mesh.boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1 }, new BoneWeight { boneIndex0 = 0, weight0 = 1 },
                    new BoneWeight { boneIndex0 = 0, weight0 = 0.25f, boneIndex1 = 1, weight1 = 0.75f },
                    new BoneWeight { boneIndex0 = 1, weight0 = 1 }
                };
                mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, Root + "AsymmetricTwoBone.asset");
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) throw new InvalidOperationException("Installed URP Unlit shader was not found.");
                var red = new Material(shader) { name = "FixtureRed" }; red.SetColor("_BaseColor", new Color(0.85f, 0.15f, 0.05f));
                var blue = new Material(shader) { name = "FixtureBlue" }; blue.SetColor("_BaseColor", new Color(0.05f, 0.3f, 0.85f));
                AssetDatabase.CreateAsset(red, Root + "FixtureRed.mat");
                AssetDatabase.CreateAsset(blue, Root + "FixtureBlue.mat");
                for (var index = 0; index < 2; index++)
                {
                    var transform = Bone("Mesh" + index, rig.transform, new Vector3(index * 2.5f, 0, 0));
                    var renderer = transform.gameObject.AddComponent<SkinnedMeshRenderer>();
                    renderer.sharedMesh = mesh; renderer.bones = new[] { a, b }; renderer.rootBone = skeleton;
                    renderer.sharedMaterials = new[] { red, blue };
                    renderer.localBounds = new Bounds(new Vector3(0.5f, 1, 0), new Vector3(5, 5, 2));
                }
                var move = new AnimationClip { name = "GenericMove", frameRate = 30 };
                move.SetCurve("Skeleton/Left/Joint", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 0, 1, 2));
                move.SetCurve("Skeleton/Right/Joint", typeof(Transform), "m_LocalPosition.y", AnimationCurve.Linear(0, 0, 1, 1));
                var rest = new AnimationClip { name = "GenericRest", frameRate = 30 };
                rest.SetCurve("Skeleton/Left/Joint", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 0, 1, 0));
                AssetDatabase.CreateAsset(move, Root + "GenericMove.anim");
                AssetDatabase.CreateAsset(rest, Root + "GenericRest.anim");
                var controller = AnimatorController.CreateAnimatorControllerAtPath(Root + "TwoState.controller");
                controller.AddParameter("Move", AnimatorControllerParameterType.Bool);
                var machine = controller.layers[0].stateMachine;
                var idle = machine.AddState("Rest"); idle.motion = rest;
                var moving = machine.AddState("Move"); moving.motion = move;
                machine.defaultState = idle;
                var transition = idle.AddTransition(moving);
                transition.hasExitTime = false; transition.duration = 0.1f;
                transition.AddCondition(AnimatorConditionMode.If, 0, "Move");
                rig.AddComponent<Animator>().runtimeAnimatorController = controller;
                PrefabUtility.SaveAsPrefabAsset(rig, Root + "GenericRig.prefab");
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if (rig != null) UnityEngine.Object.DestroyImmediate(rig);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static Transform Bone(string name, Transform parent, Vector3 localPosition)
        {
            var transform = new GameObject(name).transform;
            transform.SetParent(parent, false); transform.localPosition = localPosition;
            return transform;
        }
    }
}
