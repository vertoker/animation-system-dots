using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AnimationSystemDOTS.Sandbox.Tests
{
    public class AnimatorValidationSceneTests
    {
        static string Root => AssetDatabase.GUIDToAssetPath("90e73d0213914c0caa70529fcbcb2f13").Replace("SandboxMarker.txt", "");

        [Test]
        public void NativeValidationScene_UsesOrdinaryAnimatorAndRichEditableController()
        {
            var path = Root + "Scenes/05_AnimatorGO.unity";
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(path), Is.Not.Null, "Native comparison scene has not been generated.");
            var scene = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                var animators = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Animator>(true)).ToArray();
                Assert.That(animators.Length, Is.EqualTo(1));
                Assert.That(animators[0].avatar.isHuman && animators[0].avatar.isValid, Is.True);
                var controller = animators[0].runtimeAnimatorController as AnimatorController;
                Assert.That(controller, Is.Not.Null);
                Assert.That(controller.animationClips.Distinct().Count(), Is.GreaterThanOrEqualTo(25));
                Assert.That(controller.layers.Length, Is.GreaterThanOrEqualTo(3));
                Assert.That(controller.layers.Any(x => x.avatarMask != null && x.blendingMode == AnimatorLayerBlendingMode.Override), Is.True);
                Assert.That(controller.layers.Any(x => x.avatarMask != null && x.blendingMode == AnimatorLayerBlendingMode.Additive), Is.True);
                Assert.That(controller.layers.Any(x => x.stateMachine.stateMachines.Length > 0), Is.True);
                var trees = States(controller.layers[0].stateMachine).SelectMany(x => Trees(x.motion)).ToArray();
                CollectionAssert.IsSubsetOf(new[] { BlendTreeType.Simple1D, BlendTreeType.SimpleDirectional2D, BlendTreeType.FreeformDirectional2D,
                    BlendTreeType.FreeformCartesian2D, BlendTreeType.Direct }, trees.Select(x => x.blendType).Distinct().ToArray());
                Assert.That(trees.Any(x => x.children.Any(c => c.motion is BlendTree)), Is.True, "Nested tree must be exercised.");
                Assert.That(scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>(true)).Any(), Is.True);
                Assert.That(scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Light>(true)).Any(), Is.True);
                Assert.That(scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<MonoBehaviour>(true))
                    .Any(x => x is NativeAnimatorCharacter), Is.True);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
        {
            foreach (var state in machine.states) yield return state.state;
            foreach (var child in machine.stateMachines) foreach (var state in States(child.stateMachine)) yield return state;
        }
        static IEnumerable<BlendTree> Trees(Motion motion)
        {
            if (!(motion is BlendTree tree)) yield break;
            yield return tree;
            foreach (var child in tree.children) foreach (var nested in Trees(child.motion)) yield return nested;
        }
    }
}
