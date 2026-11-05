using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AnimationSystemDOTS.Tests
{
    public class OracleFixtureTests
    {
        const string Root = "Assets/AnimationSystemDOTS.Sandbox/Fixtures/";

        [Test]
        public void Sandbox_HasPersistentAsymmetricTwoBoneMultiSlotMeshAndDuplicateNames()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "GenericRig.prefab");
            Assert.That(prefab, Is.Not.Null, "Stage 0 fixture must be a persistent, inspectable asset.");
            Assert.That(prefab.transform.Find("Skeleton/Left/Joint"), Is.Not.Null);
            Assert.That(prefab.transform.Find("Skeleton/Right/Joint"), Is.Not.Null);
            var renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.That(renderers, Has.Length.EqualTo(2));
            var renderer = renderers[0];
            Assert.That(renderer.bones, Has.Length.EqualTo(2));
            Assert.That(renderer.sharedMesh.bindposes, Has.Length.EqualTo(2));
            Assert.That(renderer.sharedMesh.subMeshCount, Is.EqualTo(2));
            Assert.That(renderer.sharedMaterials, Has.Length.EqualTo(2));
            Assert.That(renderer.sharedMaterials.All(m => m != null), Is.True);
            Assert.That(renderer.sharedMesh.vertices.Select(v => v.x).Max(), Is.EqualTo(1.7f).Within(1e-6));
            Assert.That(renderer.sharedMesh.boneWeights.All(w => Mathf.Approximately(w.weight0 + w.weight1, 1)), Is.True);
            Assert.That(AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "GenericMove.anim"), Is.Not.Null);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Root + "TwoState.controller");
            Assert.That(controller.layers[0].stateMachine.states, Has.Length.EqualTo(2));
            Assert.That(AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/AnimationSystemDOTS.Sandbox/Settings/AnimationValidationSettings.asset"), Is.Not.Null);
        }
    }
}
