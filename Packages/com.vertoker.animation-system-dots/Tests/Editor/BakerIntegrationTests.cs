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
            if (authoringScene.IsValid() && authoringScene.isLoaded) EditorSceneManager.CloseScene(authoringScene, true);
            if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
            var absolute = Path.GetFullPath(folder);
            var prefix = Path.GetFullPath("Assets") + Path.DirectorySeparatorChar + "__AnimationSystemDOTS_Tests_";
            Assert.That(absolute.StartsWith(prefix, StringComparison.OrdinalIgnoreCase), Is.True);
            AssetDatabase.DeleteAsset(folder);
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

        void CreateWorld(bool live)
        {
            world = new World("AnimationSystemDOTS Baker Integration");
            var allowed = new[] { "Unity.Entities", "Unity.Transforms", "Unity.Scenes", "Unity.Scenes.Hybrid" };
            var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default)
                .Where(type => allowed.Contains(type.Assembly.GetName().Name) || live && type.Assembly.GetName().Name == "Unity.Scenes.Editor").ToArray();
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
