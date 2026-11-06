using System.Linq;
using NUnit.Framework;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AnimationSystemDOTS.Sandbox.Tests
{
    public class AnimatorValidationEcsSceneTests
    {
        static string Root => AssetDatabase.GUIDToAssetPath("90e73d0213914c0caa70529fcbcb2f13").Replace("SandboxMarker.txt", "");
        [TestCase("06_AnimatorECS", 1, false)]
        [TestCase("07_AnimatorBots100", 100, true)]
        public void EcsScenes_ReferenceTheSharedRigAndHaveNoNativeCharacters(string name, int count, bool bots)
        {
            var path = Root + "Scenes/" + name + ".unity";
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(path), Is.Not.Null, "ECS validation scene missing.");
            var scene = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                var roots = scene.GetRootGameObjects();
                Assert.That(roots.SelectMany(r => r.GetComponentsInChildren<Animator>(true)).Count(), Is.Zero);
                Assert.That(roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Count(), Is.EqualTo(1));
                Assert.That(roots.SelectMany(r => r.GetComponentsInChildren<Light>(true)).Any(), Is.True);
                var sub = roots.SelectMany(r => r.GetComponentsInChildren<SubScene>(true)).Single();
                var content = EditorSceneManager.OpenPreviewScene(AssetDatabase.GetAssetPath(sub.SceneAsset));
                try
                {
                    var spawner = content.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)).SingleOrDefault(m => m != null && m.GetType().Name == "AnimatorValidationSpawner");
                    Assert.That(spawner, Is.Not.Null); var serialized = new SerializedObject(spawner);
                    Assert.That(serialized.FindProperty("Count").intValue, Is.EqualTo(count)); Assert.That(serialized.FindProperty("Bots").boolValue, Is.EqualTo(bots));
                    Assert.That(AssetDatabase.GetAssetPath(serialized.FindProperty("Prefab").objectReferenceValue), Is.EqualTo(Root + "Fixtures/AnimatorValidationECS.prefab"));
                }
                finally { EditorSceneManager.ClosePreviewScene(content); }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
