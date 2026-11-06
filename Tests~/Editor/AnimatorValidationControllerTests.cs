using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimationSystemDOTS.Authoring;
using AnimationSystemDOTS.Editor.Baking;
using AnimationSystemDOTS.Sandbox.Editor;
using NUnit.Framework;
using Unity.Entities;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AnimationSystemDOTS.Tests
{
    public class AnimatorValidationControllerTests
    {
        [Test]
        public void SharedMixamoGraph_BakesItsActualClipsAndConvertsEveryRequiredFeature()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SandboxPaths.Root + "Fixtures/AnimatorValidationECS.prefab");
            Assert.That(prefab, Is.Not.Null, "Prepare the ECS variant of the shared validation rig.");
            var scene = EditorSceneManager.NewPreviewScene(); GameObject source = null;
            var rig = default(BlobAssetReference<RigDefinitionBlob>); var library = default(BlobAssetReference<AnimationLibraryBlob>);
            var graph = default(BlobAssetReference<AnimatorControllerBlob>); var clips = new List<BlobAssetReference<AnimationClipBlob>>();
            try
            {
                source = Object.Instantiate(prefab, new InstantiateParameters { scene = scene });
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorValidationFixtures.ControllerPath);
                var settings = AssetDatabase.LoadAssetAtPath<AnimationBakeSettings>(AnimatorValidationFixtures.BakeSettingsPath);
                Assert.That(settings.HumanoidPoseMode, Is.EqualTo(HumanoidPoseMode.PreciseMuscles));
                var inputs = controller.animationClips.Distinct().ToArray(); Assert.That(inputs.Length, Is.InRange(25, 40));
                var rigResult = RigBakePipeline.Bake(source); Assert.That(rigResult.Success, Is.True, string.Join("\n", rigResult.Diagnostics)); rig = rigResult.Rig;
                foreach (var clip in inputs)
                {
                    var result = ClipBakePipeline.Bake(new ClipBakeRequest { Source = source, Clip = clip, Rig = rig, Settings = settings,
                        CacheDirectory = Path.GetFullPath("Library/AnimationSystemDOTS/ClipCache") });
                    Assert.That(result.Success, Is.True, clip.name + "\n" + string.Join("\n", result.Diagnostics)); clips.Add(result.Clip);
                    Assert.That(result.Clip.Value.HumanMuscles.Length, Is.EqualTo(95), clip.name);
                }
                var libraryResult = LibraryBakePipeline.Build(rig, clips); Assert.That(libraryResult.Success, Is.True, string.Join("\n", libraryResult.Diagnostics)); library = libraryResult.Library;
                var converted = ControllerBakePipeline.Build(controller, inputs, library, source, rig);
                Assert.That(converted.Success, Is.True, string.Join("\n", converted.Diagnostics)); graph = converted.Controller;
                Assert.That(graph.Value.Layers.Length, Is.EqualTo(3)); Assert.That(graph.Value.Machines.Length, Is.GreaterThanOrEqualTo(6));
                var types = new HashSet<ControllerMotionType>(); var timed = 0; var cycle = 0; var interrupted = 0;
                for (var i = 0; i < graph.Value.Motions.Length; i++) types.Add(graph.Value.Motions[i].Type);
                foreach (ControllerMotionType type in Enum.GetValues(typeof(ControllerMotionType))) Assert.That(types.Contains(type), Is.True, type.ToString());
                for (var i = 0; i < graph.Value.States.Length; i++) { timed += graph.Value.States[i].HasTimeParameter; cycle += graph.Value.States[i].HasCycleParameter; }
                for (var i = 0; i < graph.Value.Transitions.Length; i++) if (graph.Value.Transitions[i].Interruption != ControllerInterruptionSource.None) interrupted++;
                Assert.That(timed, Is.GreaterThanOrEqualTo(1)); Assert.That(cycle, Is.GreaterThanOrEqualTo(5)); Assert.That(interrupted, Is.GreaterThan(0));
                Directory.CreateDirectory("Artifacts/Verification/AnimatorValidation");
                File.WriteAllText("Artifacts/Verification/AnimatorValidation/SharedController-bake.txt", "clips=" + inputs.Length + " states=" + graph.Value.States.Length + " machines=" + graph.Value.Machines.Length + " layers=" + graph.Value.Layers.Length + " motions=" + graph.Value.Motions.Length);
            }
            finally
            {
                if (graph.IsCreated) graph.Dispose(); if (library.IsCreated) library.Dispose(); foreach (var clip in clips) if (clip.IsCreated) clip.Dispose(); if (rig.IsCreated) rig.Dispose();
                if (source != null) Object.DestroyImmediate(source); EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
