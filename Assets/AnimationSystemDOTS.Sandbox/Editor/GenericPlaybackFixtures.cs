using System;
using AnimationSystemDOTS.Authoring;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace AnimationSystemDOTS.Sandbox.Editor
{
    public static class GenericPlaybackFixtures
    {
        static readonly string Root = (SandboxPaths.Root + "");
        public static readonly string ScenePath = Root + "Scenes/01_GenericPlayback.unity";

        [MenuItem("Tools/Animation System DOTS/Create LOD Acceptance Fixture")]
        public static void CreateLodFixture()
        {
            var original = SceneManager.GetActiveScene();
            if (original.isDirty || string.IsNullOrEmpty(original.path)) throw new InvalidOperationException("Save the active scene before creating the LOD fixture.");
            var path = Root + "Fixtures/Stage3LodRig.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                var rig = PrefabUtility.LoadPrefabContents(Root + "Fixtures/Stage3GenericRig.prefab");
                try
                {
                    rig.name = "Stage3LodRig";
                    var renderers = rig.GetComponentsInChildren<SkinnedMeshRenderer>();
                    var group = rig.AddComponent<LODGroup>(); group.fadeMode = LODFadeMode.None;
                    group.SetLODs(new[] { new LOD(.3f, new Renderer[] { renderers[0] }), new LOD(.01f, new Renderer[] { renderers[1] }) });
                    group.RecalculateBounds();
                    prefab = PrefabUtility.SaveAsPrefabAsset(rig, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(rig); }
            }
            var contentPath = Root + "Scenes/01_GenericPlaybackContent.unity";
            var scene = SceneManager.GetSceneByPath(contentPath); var alreadyLoaded = scene.IsValid() && scene.isLoaded;
            if (alreadyLoaded && scene.isDirty) throw new InvalidOperationException("Save the sandbox content scene before adding the LOD reference.");
            if (!alreadyLoaded) scene = EditorSceneManager.OpenScene(contentPath, OpenSceneMode.Additive);
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    var demo = root.GetComponent<GenericPlaybackDemo>(); if (demo == null) continue;
                    demo.LodRigPrefab = prefab; EditorUtility.SetDirty(demo);
                }
                EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            }
            finally { if (!alreadyLoaded) EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(original); }
        }

        [MenuItem("Tools/Animation System DOTS/Create Generic Playback Scene")]
        public static void Create()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) return;
            var original = SceneManager.GetActiveScene();
            if (original.isDirty || string.IsNullOrEmpty(original.path)) throw new InvalidOperationException("Save the active scene before creating the sandbox.");
            if (!AssetDatabase.IsValidFolder(Root + "Scenes")) AssetDatabase.CreateFolder(Root.TrimEnd('/'), "Scenes");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "Fixtures/Materials/ComputeDeformation.shadergraph");
            if (shader == null) throw new InvalidOperationException("Compute Deformation Shader Graph is missing.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var rig = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Fixtures/GenericRig.prefab"));
                rig.name = "Stage3GenericRig"; UnityEngine.Object.DestroyImmediate(rig.GetComponent<Animator>());
                var materials = new[] { Material(shader, new Color(.9f, .12f, .04f), 0), Material(shader, new Color(.04f, .4f, .9f), 1) };
                var renderers = rig.GetComponentsInChildren<SkinnedMeshRenderer>();
                for (var i = 0; i < renderers.Length; i++)
                {
                    var renderer = renderers[i]; renderer.transform.localPosition = new Vector3(i == 0 ? -1.1f : 1.1f, 0, 0);
                    var mesh = UnityEngine.Object.Instantiate(renderer.sharedMesh); mesh.name = "Stage3Mesh" + i;
                    var vertices = mesh.vertices; for (var v = 0; v < vertices.Length; v++) vertices[v] *= .75f; mesh.vertices = vertices;
                    var bindposes = new Matrix4x4[renderer.bones.Length];
                    for (var j = 0; j < bindposes.Length; j++) bindposes[j] = renderer.bones[j].worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                    mesh.bindposes = bindposes; mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, Root + "Fixtures/Stage3Mesh" + i + ".asset");
                    renderer.sharedMesh = mesh; renderer.sharedMaterials = materials;
                    renderer.rootBone = rig.transform.Find(i == 0 ? "Skeleton/Left/Joint" : "Skeleton/Right");
                    renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                }
                var clip = new AnimationClip { name = "Stage3Move", frameRate = 30 };
                Curve(clip, "Skeleton", "m_LocalPosition.x", .2f, .65f);
                Curve(clip, "Skeleton/Left/Joint", "m_LocalPosition.x", 0, .8f);
                Curve(clip, "Skeleton/Right", "m_LocalPosition.y", 1, 1.7f);
                Curve(clip, "Skeleton/Right/Joint", "m_LocalPosition.x", 0, -.5f);
                var clipSettings = AnimationUtility.GetAnimationClipSettings(clip); clipSettings.loopTime = true; AnimationUtility.SetAnimationClipSettings(clip, clipSettings);
                AssetDatabase.CreateAsset(clip, Root + "Fixtures/Stage3Move.anim");
                rig.AddComponent<AnimationRigAuthoring>().Settings = AssetDatabase.LoadAssetAtPath<AnimationBakeSettings>(Root + "Settings/AnimationBakeSettings.asset");
                rig.AddComponent<AnimationLibraryAuthoring>().Clips = new[] { clip };
                var prefab = PrefabUtility.SaveAsPrefabAsset(rig, Root + "Fixtures/Stage3GenericRig.prefab"); UnityEngine.Object.DestroyImmediate(rig);
                new GameObject("ECS Prefab Spawner").AddComponent<GenericPlaybackDemo>().RigPrefab = prefab;
                EditorSceneManager.SaveScene(scene, Root + "Scenes/01_GenericPlaybackContent.unity");
                EditorSceneManager.CloseScene(scene, true);
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
                var camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>(); camera.tag = "MainCamera";
                camera.gameObject.AddComponent<GenericPlaybackCapture>();
                camera.transform.position = new Vector3(0, 3, -12); camera.transform.LookAt(new Vector3(0, 1, 0)); camera.fieldOfView = 45;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .08f, .11f);
                var light = new GameObject("Directional Light", typeof(Light)).GetComponent<Light>(); light.type = LightType.Directional;
                light.intensity = 2; light.shadows = LightShadows.Soft; light.transform.rotation = Quaternion.Euler(45, -30, 0);
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Shadow Receiver";
                floor.transform.position = new Vector3(0, -.12f, 1); floor.transform.localScale = new Vector3(18, .1f, 12);
                var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); floorMaterial.SetColor("_BaseColor", new Color(.35f, .38f, .42f));
                AssetDatabase.CreateAsset(floorMaterial, Root + "Fixtures/Materials/Stage3Floor.mat"); floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
                var subScene = new GameObject("Animation Content").AddComponent<SubScene>();
                subScene.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(Root + "Scenes/01_GenericPlaybackContent.unity"); subScene.AutoLoadScene = true;
                EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            }
        }
        static void Curve(AnimationClip clip, string path, string property, float first, float middle)
            => clip.SetCurve(path, typeof(Transform), property, new AnimationCurve(
                new Keyframe(0, first, middle - first, middle - first),
                new Keyframe(1, middle, middle - first, first - middle),
                new Keyframe(2, first, first - middle, first - middle)));
        static Material Material(Shader shader, Color color, int index)
        {
            var material = new Material(shader);
            if (!material.HasProperty("_BaseColor")) throw new InvalidOperationException("Sample Shader Graph has no base color.");
            material.SetVector("_BaseColor", color);
            AssetDatabase.CreateAsset(material, Root + "Fixtures/Materials/Stage3" + index + ".mat"); return material;
        }
    }
}
