using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Entities;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using AnimationSystemDOTS.Authoring;

namespace AnimationSystemDOTS.Tests
{
    public class BakerIntegrationTests
    {
        string folder;
        Scene authoringScene;
        Scene originalScene;
        World world;
        bool liveConversionWasEnabled;

        [SetUp]
        public void SetUp()
        {
            originalScene = SceneManager.GetActiveScene();
            liveConversionWasEnabled = Unity.Scenes.Editor.LiveConversionEditorSettings.LiveConversionEnabled;
            folder = "Assets/__AnimationSystemDOTS_Tests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            // Unity's EditMode runner starts in a clean, untitled scene; additive creation
            // is forbidden until that scene has a path. Never close unsaved user work.
            if (string.IsNullOrEmpty(originalScene.path))
            {
                Assert.That(originalScene.isDirty, Is.False, "Integration test requires a clean initial scene.");
                authoringScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            else authoringScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(authoringScene);
            new GameObject("Camera", typeof(Camera));
            new GameObject("Light", typeof(Light)).GetComponent<Light>().type = LightType.Directional;
        }

        [TearDown]
        public void TearDown()
        {
            if (world != null && world.IsCreated) world.Dispose();
            world = null;
            Unity.Scenes.Editor.LiveConversionEditorSettings.LiveConversionEnabled = liveConversionWasEnabled;
            if (authoringScene.IsValid() && authoringScene.isLoaded)
            {
                if (SceneManager.sceneCount == 1) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                EditorSceneManager.CloseScene(authoringScene, true);
            }
            if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
            var absolute = Path.GetFullPath(folder);
            var prefix = Path.GetFullPath("Assets") + Path.DirectorySeparatorChar + "__AnimationSystemDOTS_Tests_";
            Assert.That(absolute.StartsWith(prefix, StringComparison.OrdinalIgnoreCase), Is.True);
            AssetDatabase.DeleteAsset(folder);
        }

        GameObject CreateSkinFixture()
        {
            var root = new GameObject("SkinRig"); root.transform.position = new Vector3(10, 0, 0);
            var parent = new GameObject("MovingParent").transform; parent.SetParent(root.transform, false);
            var bone = new GameObject("RootBone").transform; bone.SetParent(parent, false); bone.localPosition = new Vector3(0, 1, 0);
            var tip = new GameObject("Tip").transform; tip.SetParent(bone, false); tip.localPosition = new Vector3(1, 0, 0);
            var settings = ScriptableObject.CreateInstance<AnimationBakeSettings>();
            AssetDatabase.CreateAsset(settings, folder + "/Settings.asset");
            var clip = new AnimationClip();
            clip.SetCurve("MovingParent", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 0, 1, 2));
            clip.SetCurve("MovingParent/RootBone", typeof(Transform), "m_LocalPosition.y", AnimationCurve.Linear(0, 1, 1, 3));
            AssetDatabase.CreateAsset(clip, folder + "/SkinMove.anim");
            root.AddComponent<AnimationRigAuthoring>().Settings = settings;
            root.AddComponent<AnimationLibraryAuthoring>().Clips = new[] { clip };
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/AnimationSystemDOTS.Sandbox/Fixtures/Materials/ComputeDeformation.shadergraph");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader); AssetDatabase.CreateAsset(material, folder + "/Skin.mat");
            for (var i = 0; i < 2; i++)
            {
                var meshObject = new GameObject("Mesh" + i); meshObject.transform.SetParent(root.transform, false);
                meshObject.transform.localPosition = new Vector3(i * 2, -1, 0);
                var mesh = new Mesh { name = "AsymmetricSkin" + i };
                mesh.vertices = new[] { Vector3.zero, new Vector3(0, 2, 0), new Vector3(1, 0, 0), new Vector3(2, 2, 0) };
                mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.right, Vector2.one };
                mesh.subMeshCount = 2; mesh.SetTriangles(new[] { 0, 1, 2 }, 0); mesh.SetTriangles(new[] { 2, 1, 3 }, 1);
                mesh.bindposes = new[] { tip.worldToLocalMatrix * meshObject.transform.localToWorldMatrix, bone.worldToLocalMatrix * meshObject.transform.localToWorldMatrix };
                mesh.boneWeights = new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1 }, new BoneWeight { boneIndex0 = 0, weight0 = .25f, boneIndex1 = 1, weight1 = .75f }, new BoneWeight { boneIndex0 = 1, weight0 = 1 }, new BoneWeight { boneIndex0 = 0, weight0 = 1 } };
                mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, folder + "/Mesh" + i + ".asset");
                var renderer = meshObject.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = mesh;
                renderer.bones = new[] { tip, bone }; renderer.rootBone = i == 0 ? bone : parent;
                renderer.sharedMaterials = new[] { material, material };
            }
            return root;
        }

        [UnityTest]
        public IEnumerator SubScene_BindsStockSkinBuffersAndEveryMaterialToOneSharedPose()
        {
            CreateSkinFixture();
            var path = folder + "/Skin.unity";
            Assert.That(EditorSceneManager.SaveScene(authoringScene, path), Is.True);
            EditorSceneManager.CloseScene(authoringScene, true);
            if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
            CreateWorld(false, true);
            var scene = SceneSystem.LoadSceneAsync(world.Unmanaged, new Unity.Entities.Hash128(AssetDatabase.AssetPathToGUID(path)),
                new SceneSystem.LoadParameters { Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn });
            var deadline = EditorApplication.timeSinceStartup + 60;
            while (!SceneSystem.IsSceneLoaded(world.Unmanaged, scene) && EditorApplication.timeSinceStartup < deadline) { world.Update(); yield return null; }
            Assert.That(SceneSystem.IsSceneLoaded(world.Unmanaged, scene), Is.True);
            using (var query = world.EntityManager.CreateEntityQuery(typeof(AnimationSystemDOTS.Rendering.SkinnedMeshBinding), typeof(Unity.Deformations.SkinMatrix)))
            {
                Assert.That(query.CalculateEntityCount(), Is.EqualTo(2), "Bindings must attach to the stock deformation entities, without creating another palette.");
                using (var meshes = query.ToEntityArray(Unity.Collections.Allocator.Temp))
                {
                    var a = world.EntityManager.GetComponentData<AnimationSystemDOTS.Rendering.SkinnedMeshBinding>(meshes[0]);
                    var b = world.EntityManager.GetComponentData<AnimationSystemDOTS.Rendering.SkinnedMeshBinding>(meshes[1]);
                    Assert.That(a.Rig, Is.EqualTo(b.Rig)); Assert.That(a.DeformationRoot, Is.Not.EqualTo(b.DeformationRoot));
                    Assert.That(a.Binding.Value.BoneRemap[0], Is.EqualTo(3)); Assert.That(a.Binding.Value.BoneRemap[1], Is.EqualTo(2));
                }
            }
            using (var query = world.EntityManager.CreateEntityQuery(typeof(BoneExportBinding))) Assert.That(query.CalculateEntityCount(), Is.EqualTo(2));
            using (var query = world.EntityManager.CreateEntityQuery(typeof(AnimationSystemDOTS.Rendering.AnimatedRenderBinding), typeof(Unity.Rendering.RenderBounds))) Assert.That(query.CalculateEntityCount(), Is.EqualTo(4), "Every stock material entity needs its own bounds binding.");
            using (var query = world.EntityManager.CreateEntityQuery(typeof(AnimationRig)))
            {
                var rig = query.GetSingletonEntity();
                world.EntityManager.GetBuffer<ScriptedPlaybackCommand>(rig).Add(ScriptedPlaybackCommand.Play(new ClipId(0), 1, .5));
                var step = world.EntityManager.CreateEntity(typeof(AnimationStepContext));
                world.EntityManager.SetComponentData(step, new AnimationStepContext { Tick = 1 });
                world.Update(); world.EntityManager.CompleteAllTrackedJobs();
                using (var skins = world.EntityManager.CreateEntityQuery(typeof(AnimationSystemDOTS.Rendering.SkinnedMeshBinding)))
                using (var meshes = skins.ToEntityArray(Unity.Collections.Allocator.Temp))
                foreach (var mesh in meshes)
                {
                    var data = world.EntityManager.GetComponentData<AnimationSystemDOTS.Rendering.SkinnedMeshBinding>(mesh);
                    var matrix = world.EntityManager.GetComponentData<Unity.Transforms.LocalToWorld>(data.DeformationRoot).Value;
                    var palette = world.EntityManager.GetBuffer<Unity.Deformations.SkinMatrix>(mesh);
                    var position = Unity.Mathematics.math.transform(matrix, palette[0].Value.c3);
                    var expected = new Unity.Mathematics.float3(data.Binding.Value.DeformationRootBone == 2 ? 11 : 13, 0, 0);
                    Assert.That(Unity.Mathematics.math.distance(position, expected), Is.LessThan(1e-4), "The imported palette and animated export must use the same step and actual deformation root.");
                }
            }
            SceneSystem.UnloadScene(world.Unmanaged, scene, SceneSystem.UnloadParameters.DestroyMetaEntities);
            world.Update();
            Assert.That(SkinCount() + RenderCount() + ExportCount(), Is.Zero, "SubScene unload retained animation bindings.");
            scene = SceneSystem.LoadSceneAsync(world.Unmanaged, new Unity.Entities.Hash128(AssetDatabase.AssetPathToGUID(path)),
                new SceneSystem.LoadParameters { Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn });
            yield return WaitUntil(() => SceneSystem.IsSceneLoaded(world.Unmanaged, scene) && SkinCount() == 2 && RenderCount() == 4 && ExportCount() == 2, "SubScene reload failed to restore skinned bindings.");
        }

        [UnityTest]
        public IEnumerator SubScene_LiveSkinRebakeRetractsChangedRootsMaterialsMeshesAndRemovedRig()
        {
            var source = CreateSkinFixture();
            var renderers = source.GetComponentsInChildren<SkinnedMeshRenderer>();
            var path = folder + "/LiveSkin.unity";
            Assert.That(EditorSceneManager.SaveScene(authoringScene, path), Is.True);
            Unity.Scenes.Editor.LiveConversionEditorSettings.LiveConversionEnabled = true;
            CreateWorld(true);
            SceneSystem.LoadSceneAsync(world.Unmanaged, new Unity.Entities.Hash128(AssetDatabase.AssetPathToGUID(path)));
            yield return WaitUntil(() => SkinCount() == 2 && RenderCount() == 4, "Initial live skin binding was not published.");
            var oldKey = MeshBindingKey(2);
            var replacement = UnityEngine.Object.Instantiate(renderers[0].sharedMesh); replacement.subMeshCount = 1;
            var vertices = replacement.vertices; vertices[3].x += 20; replacement.vertices = vertices; replacement.RecalculateBounds();
            AssetDatabase.CreateAsset(replacement, folder + "/Replacement.asset");
            var serialized = new SerializedObject(renderers[0]);
            serialized.FindProperty("m_RootBone").objectReferenceValue = source.transform.Find("MovingParent/RootBone/Tip");
            serialized.FindProperty("m_Mesh").objectReferenceValue = replacement;
            serialized.FindProperty("m_Materials").arraySize = 1;
            serialized.ApplyModifiedProperties();
            yield return WaitUntil(() => SkinCount() == 2 && RenderCount() == 3 && MeshBindingKey(3) != null && MeshBindingKey(3) != oldKey,
                "Root/mesh/material rebake retained stale skin or material bindings.");
            Assert.That(MeshBindingKey(2), Is.Null, "Former deformation root binding survived rebake.");
            Undo.DestroyObjectImmediate(renderers[1]);
            yield return WaitUntil(() => SkinCount() == 1 && RenderCount() == 1 && ExportCount() == 1, "Removed skinned renderer retained derived links.");
            Undo.DestroyObjectImmediate(source.GetComponent<AnimationLibraryAuthoring>());
            yield return null;
            Undo.DestroyObjectImmediate(source.GetComponent<AnimationRigAuthoring>());
            yield return WaitUntil(() => SkinCount() == 0 && RenderCount() == 0 && ExportCount() == 0, "Removed rig retained skin, bounds or root export bindings.");
        }

        int SkinCount() { using (var q = world.EntityManager.CreateEntityQuery(typeof(AnimationSystemDOTS.Rendering.SkinnedMeshBinding))) return q.CalculateEntityCount(); }
        int RenderCount() { using (var q = world.EntityManager.CreateEntityQuery(typeof(AnimationSystemDOTS.Rendering.AnimatedRenderBinding))) return q.CalculateEntityCount(); }
        int ExportCount() { using (var q = world.EntityManager.CreateEntityQuery(typeof(BoneExportBinding))) return q.CalculateEntityCount(); }
        string MeshBindingKey(int rootIndex)
        {
            using (var q = world.EntityManager.CreateEntityQuery(typeof(AnimationSystemDOTS.Rendering.SkinnedMeshBinding)))
            using (var bindings = q.ToComponentDataArray<AnimationSystemDOTS.Rendering.SkinnedMeshBinding>(Unity.Collections.Allocator.Temp))
                foreach (var binding in bindings) if (binding.Binding.Value.DeformationRootBone == rootIndex) return binding.Binding.Value.ContentKey.ToString();
            return null;
        }

        [UnityTest]
        public IEnumerator SubScene_LoadsSelfContainedLibraryThroughPublicSceneSystem()
        {
            var root = new GameObject("Rig");
            var bone = new GameObject("Bone"); bone.transform.SetParent(root.transform, false);
            var settings = ScriptableObject.CreateInstance<AnimationBakeSettings>();
            AssetDatabase.CreateAsset(settings, folder + "/Settings.asset");
            var clip = new AnimationClip();
            clip.SetCurve("Bone", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 2, 1, 6));
            AssetDatabase.CreateAsset(clip, folder + "/Move.anim");
            root.AddComponent<AnimationRigAuthoring>().Settings = settings;
            root.AddComponent<AnimationLibraryAuthoring>().Clips = new[] { clip };
            var path = folder + "/Rig.unity";
            Assert.That(EditorSceneManager.SaveScene(authoringScene, path), Is.True);
            EditorSceneManager.CloseScene(authoringScene, true);
            if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);

            CreateWorld(false);
            var scene = SceneSystem.LoadSceneAsync(world.Unmanaged, new Unity.Entities.Hash128(AssetDatabase.AssetPathToGUID(path)),
                new SceneSystem.LoadParameters { Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn });
            var deadline = EditorApplication.timeSinceStartup + 60;
            while (!SceneSystem.IsSceneLoaded(world.Unmanaged, scene) && EditorApplication.timeSinceStartup < deadline)
            {
                world.Update();
                yield return null;
            }
            Assert.That(SceneSystem.IsSceneLoaded(world.Unmanaged, scene), Is.True, "Public scene import did not complete.");
            using (var query = world.EntityManager.CreateEntityQuery(typeof(AnimationRig)))
            {
                Assert.That(query.CalculateEntityCount(), Is.EqualTo(1), "RigBaker must publish exactly one complete rig/library component.");
                var data = query.GetSingleton<AnimationRig>();
                Assert.That(data.Rig.Value.Paths.Length, Is.EqualTo(2));
                Assert.That(data.Library.Value.Clips.Length, Is.EqualTo(1));
                Assert.That(ClipSampler.SampleBone(ref data.Library.Value.Clips[0], 1, 0.5).Position.x, Is.EqualTo(4).Within(1e-5));
            }
        }

        [UnityTest]
        public IEnumerator SubScene_LiveRebakeReplacesClipsAndRetractsRemovedAuthoring()
        {
            var root = new GameObject("LiveRig");
            new GameObject("Bone").transform.SetParent(root.transform, false);
            var settings = ScriptableObject.CreateInstance<AnimationBakeSettings>();
            AssetDatabase.CreateAsset(settings, folder + "/Settings.asset");
            var clip = new AnimationClip();
            clip.SetCurve("Bone", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 2, 1, 6));
            AssetDatabase.CreateAsset(clip, folder + "/Move.anim");
            var rigAuthoring = root.AddComponent<AnimationRigAuthoring>(); rigAuthoring.Settings = settings;
            var libraryAuthoring = root.AddComponent<AnimationLibraryAuthoring>(); libraryAuthoring.Clips = new[] { clip };
            var path = folder + "/LiveRig.unity";
            Assert.That(EditorSceneManager.SaveScene(authoringScene, path), Is.True);
            Unity.Scenes.Editor.LiveConversionEditorSettings.LiveConversionEnabled = true;
            CreateWorld(true);
            SceneSystem.LoadSceneAsync(world.Unmanaged, new Unity.Entities.Hash128(AssetDatabase.AssetPathToGUID(path)));
            using (var query = world.EntityManager.CreateEntityQuery(typeof(AnimationRig)))
            {
                yield return WaitUntil(() => query.CalculateEntityCount() == 1, "Initial live bake did not publish the rig.");
                var initial = query.GetSingleton<AnimationRig>();
                Assert.That(initial.Library.Value.Clips.Length, Is.EqualTo(1));
                var oldKey = initial.Library.Value.ContentKey.ToString();
                var serialized = new SerializedObject(libraryAuthoring);
                serialized.FindProperty("Clips").arraySize = 0;
                serialized.ApplyModifiedProperties();
                yield return WaitUntil(() => query.CalculateEntityCount() == 1 && query.GetSingleton<AnimationRig>().Library.Value.Clips.Length == 0,
                    "Incremental rebake retained the removed clip binding.");
                Assert.That(query.GetSingleton<AnimationRig>().Library.Value.ContentKey.ToString(), Is.Not.EqualTo(oldKey));
                Undo.DestroyObjectImmediate(libraryAuthoring);
                yield return null;
                Undo.DestroyObjectImmediate(rigAuthoring);
                yield return WaitUntil(() => query.CalculateEntityCount() == 0, "Removed authoring left an obsolete AnimationRig component.");
            }
        }

        void CreateWorld(bool live, bool animation = false)
        {
            world = new World("AnimationSystemDOTS Baker Integration");
            var allowed = new[] { "Unity.Entities", "Unity.Transforms", "Unity.Scenes", "Unity.Scenes.Hybrid" };
            var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default)
                .Where(type => allowed.Contains(type.Assembly.GetName().Name) || live && type.Assembly.GetName().Name == "Unity.Scenes.Editor"
                    || animation && (type.Assembly.GetName().Name == "AnimationSystemDOTS.Core" || type.Assembly.GetName().Name == "AnimationSystemDOTS.Rendering")).ToArray();
            DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, systems);
        }

        IEnumerator WaitUntil(Func<bool> condition, string message)
        {
            var deadline = EditorApplication.timeSinceStartup + 30;
            while (!condition() && EditorApplication.timeSinceStartup < deadline)
            {
                world.Update();
                yield return null;
            }
            Assert.That(condition(), Is.True, message);
        }
    }
}
