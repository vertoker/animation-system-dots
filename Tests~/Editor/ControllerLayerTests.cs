using System;
using System.Collections.Generic;
using AnimationSystemDOTS.Authoring;
using AnimationSystemDOTS.Editor.Baking;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace AnimationSystemDOTS.Tests
{
    public class ControllerLayerTests
    {
        [TestCase("multiway")]
        [TestCase("nestedQuaternions")]
        [TestCase("override0")]
        [TestCase("overridePartial")]
        [TestCase("override1")]
        [TestCase("mask")]
        [TestCase("additive")]
        [TestCase("maskedAdditive")]
        [TestCase("additiveTree")]
        [TestCase("overrideTree")]
        [TestCase("partialLayerTree")]
        [TestCase("zeroLayerTree")]
        [TestCase("partialAdditiveTree")]
        [TestCase("scaledReference")]
        [TestCase("twoAdditives")]
        [TestCase("missingReference")]
        [TestCase("threeLayers")]
        [TestCase("synced")]
        [TestCase("syncedTiming")]
        [TestCase("syncedPair")]
        [TestCase("syncedChain")]
        [TestCase("sharedTrigger")]
        public void LayeredController_ActualECSPosesAndClocksMatchNative(string scenario)
        {
            var folder = "Assets/__LayerTests_" + Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(folder));
            var scene = EditorSceneManager.NewPreviewScene(); var go = new GameObject("Layer Oracle", typeof(Animator));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            var a = new GameObject("BoneA").transform; a.SetParent(go.transform, false);
            var b = new GameObject("BoneB").transform; b.SetParent(go.transform, false);
            var child = new GameObject("Child").transform; child.SetParent(a, false); child.localPosition = Vector3.up;
            var settings = ScriptableObject.CreateInstance<AnimationBakeSettings>(); settings.SampleRate = 120;
            var rig = default(BlobAssetReference<RigDefinitionBlob>); var library = default(BlobAssetReference<AnimationLibraryBlob>);
            var converted = default(ControllerBakeResult); var native = default(PlayableGraph); World world = null; AvatarMask mask = null;
            var baked = new List<BlobAssetReference<AnimationClipBlob>>();
            try
            {
                var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Layers.controller");
                for (var i = 0; i < 3; i++) controller.AddParameter("W" + i, AnimatorControllerParameterType.Float);
                if (scenario == "sharedTrigger") controller.AddParameter("Go", AnimatorControllerParameterType.Trigger);
                var defaults = controller.parameters; defaults[0].defaultFloat = .2f; defaults[1].defaultFloat = .3f; defaults[2].defaultFloat = scenario.StartsWith("partial", StringComparison.Ordinal) ? .05f : .5f; controller.parameters = defaults;
                var clips = new AnimationClip[3];
                for (var i = 0; i < 3; i++)
                {
                    clips[i] = new AnimationClip { name = "Layer" + i };
                    foreach (var path in new[] { "BoneA", "BoneA/Child", "BoneB" })
                    {
                        var rotation = Quaternion.Euler(i == 0 ? new Vector3(70, 0, 0) : i == 1 ? new Vector3(0, -85, 20) : new Vector3(10, 25, 100));
                        for (var c = 0; c < 4; c++) clips[i].SetCurve(path, typeof(Transform), "m_LocalRotation." + "xyzw"[c], AnimationCurve.Constant(0, i + 1, rotation[c]));
                        clips[i].SetCurve(path, typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, i * .3f, i + 1, i * .3f + .2f));
                        clips[i].SetCurve(path, typeof(Transform), "m_LocalPosition.y", AnimationCurve.Constant(0, i + 1, path.EndsWith("Child") ? 1 : .1f * i));
                        clips[i].SetCurve(path, typeof(Transform), "m_LocalPosition.z", AnimationCurve.Constant(0, i + 1, 0));
                        for (var c = 0; c < 3; c++) clips[i].SetCurve(path, typeof(Transform), "m_LocalScale." + "xyz"[c], AnimationCurve.Constant(0, i + 1, 1 + i * .1f));
                    }
                    var clipSettings = AnimationUtility.GetAnimationClipSettings(clips[i]); clipSettings.loopTime = true;
                    clipSettings.hasAdditiveReferencePose = true; clipSettings.additiveReferencePoseClip = clips[0]; clipSettings.additiveReferencePoseTime = .3f;
                    if (scenario == "scaledReference" && i == 2) clipSettings.additiveReferencePoseClip = clips[1];
                    if (scenario == "missingReference" && i == 2) clipSettings.hasAdditiveReferencePose = false;
                    AnimationUtility.SetAnimationClipSettings(clips[i], clipSettings); AssetDatabase.CreateAsset(clips[i], folder + "/Layer" + i + ".anim");
                }
                var tree = new BlendTree { name = "Quaternion Mix", blendType = BlendTreeType.Direct };
                AssetDatabase.AddObjectToAsset(tree, controller);
                tree.children = new[] { new ChildMotion { motion = clips[0], timeScale = 1, directBlendParameter = "W0" },
                    new ChildMotion { motion = clips[1], timeScale = 1, directBlendParameter = "W1" }, new ChildMotion { motion = clips[2], timeScale = 1, directBlendParameter = "W2" } };
                var serialized = new SerializedObject(tree); serialized.FindProperty("m_NormalizedBlendValues").boolValue = true; serialized.ApplyModifiedPropertiesWithoutUndo();
                var multiway = scenario == "multiway" || scenario == "nestedQuaternions";
                Motion baseMotion = multiway ? (Motion)tree : clips[0];
                if (scenario == "nestedQuaternions")
                {
                    var nested = new BlendTree { name = "Nested Quaternion Mix", blendType = BlendTreeType.Simple1D, blendParameter = "W0", useAutomaticThresholds = false };
                    AssetDatabase.AddObjectToAsset(nested, controller);
                    nested.children = new[] { new ChildMotion { motion = tree, timeScale = 1 }, new ChildMotion { motion = clips[1], timeScale = 1, threshold = 1 } };
                    baseMotion = nested;
                }
                var first = controller.layers[0].stateMachine.AddState("Base"); first.motion = baseMotion; first.writeDefaultValues = false;
                controller.layers[0].stateMachine.defaultState = first;
                if (scenario == "sharedTrigger")
                {
                    var next = controller.layers[0].stateMachine.AddState("Next"); next.motion = clips[1]; next.writeDefaultValues = false;
                    var edge = first.AddTransition(next); edge.hasExitTime = false; edge.hasFixedDuration = true; edge.duration = .3f; edge.AddCondition(AnimatorConditionMode.If, 0, "Go");
                }
                if (!multiway)
                {
                    controller.AddLayer("Upper"); var layers = controller.layers;
                    var upper = layers[1]; upper.defaultWeight = scenario == "override0" ? 0 : scenario == "override1" ? 1 : .35f;
                    upper.blendingMode = scenario.IndexOf("additive", StringComparison.OrdinalIgnoreCase) >= 0 || scenario == "missingReference" || scenario == "scaledReference" ? AnimatorLayerBlendingMode.Additive : AnimatorLayerBlendingMode.Override;
                    if (scenario.StartsWith("synced")) { upper.syncedLayerIndex = 0; upper.syncedLayerAffectsTiming = scenario != "synced"; upper.SetOverrideMotion(first, clips[2]); }
                    else { var state = upper.stateMachine.AddState("Upper"); state.motion = scenario.EndsWith("Tree", StringComparison.Ordinal) ? (Motion)tree : clips[2]; state.writeDefaultValues = false; upper.stateMachine.defaultState = state; }
                    if (scenario == "sharedTrigger")
                    {
                        var next = upper.stateMachine.AddState("Next"); next.motion = clips[0]; next.writeDefaultValues = false;
                        var edge = upper.stateMachine.defaultState.AddTransition(next); edge.hasExitTime = false; edge.hasFixedDuration = true; edge.duration = .3f; edge.AddCondition(AnimatorConditionMode.If, 0, "Go");
                    }
                    if (scenario == "mask" || scenario == "maskedAdditive" || scenario == "threeLayers")
                    {
                        mask = new AvatarMask { name = "BoneB only", transformCount = 4 };
                        var paths = new[] { "", "BoneA", "BoneA/Child", "BoneB" };
                        for (var i = 0; i < paths.Length; i++) { mask.SetTransformPath(i, paths[i]); mask.SetTransformActive(i, paths[i] == "BoneB"); }
                        AssetDatabase.CreateAsset(mask, folder + "/Upper.mask"); upper.avatarMask = mask;
                    }
                    layers[1] = upper; controller.layers = layers;
                    if (scenario == "syncedPair" || scenario == "syncedChain")
                    {
                        controller.AddLayer("Sync2"); layers = controller.layers; layers[2].defaultWeight = .4f;
                        layers[2].syncedLayerIndex = scenario == "syncedPair" ? 0 : 1; layers[2].syncedLayerAffectsTiming = true;
                        layers[2].SetOverrideMotion(first, clips[1]); controller.layers = layers;
                        if (scenario == "syncedChain") Assert.That(controller.layers[2].syncedLayerIndex, Is.Zero, "Unity canonicalizes a chained sync target to its source layer.");
                    }
                    if (scenario == "threeLayers" || scenario == "twoAdditives")
                    {
                        controller.AddLayer("Top"); layers = controller.layers; layers[2].defaultWeight = .4f;
                        if (scenario == "twoAdditives") layers[2].blendingMode = AnimatorLayerBlendingMode.Additive;
                        var state = layers[2].stateMachine.AddState("Top"); state.motion = clips[1]; state.writeDefaultValues = false; layers[2].stateMachine.defaultState = state; controller.layers = layers;
                    }
                }
                var rigResult = RigBakePipeline.Bake(go); Assert.That(rigResult.Success, Is.True); rig = rigResult.Rig;
                foreach (var clip in clips)
                {
                    var result = ClipBakePipeline.Bake(new ClipBakeRequest { Source = go, Clip = clip, Rig = rig, Settings = settings });
                    Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics)); baked.Add(result.Clip);
                }
                library = LibraryBakePipeline.Build(rig, baked).Library;
                converted = ControllerBakePipeline.Build(controller, clips, library, go, rig);
                if (scenario == "missingReference")
                {
                    Assert.That(converted.Success, Is.False);
                    Assert.That(string.Join("\n", converted.Diagnostics), Does.Contain(folder).And.Contain("/Upper").And.Contain("Additive Reference"));
                    return;
                }
                Assert.That(converted.Success, Is.True, string.Join("\n", converted.Diagnostics));
                native = PlayableGraph.Create("AnimationSystemDOTS Layer Oracle"); native.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimatorControllerPlayable.Create(native, controller); AnimationPlayableOutput.Create(native, "Native", go.GetComponent<Animator>()).SetSourcePlayable(playable); native.Play();
                for (var i = 1; i < controller.layers.Length; i++) playable.SetLayerWeight(i, controller.layers[i].defaultWeight);
                for (var i = 0; i < 3; i++) playable.SetFloat("W" + i, defaults[i].defaultFloat);
                world = new World("AnimationSystemDOTS Layer Poses"); var em = world.EntityManager;
                var actor = em.CreateEntity(typeof(AnimationRig), typeof(AnimationController), typeof(ControllerLayerState), typeof(AnimationClock), typeof(AnimationPlaybackMode));
                em.SetComponentData(actor, new AnimationRig { Rig = rig, Library = library }); em.SetComponentData(actor, new AnimationController { Definition = converted.Controller });
                em.SetComponentData(actor, new AnimationPlaybackMode { Value = PlaybackDriver.Controller });
                var values = em.AddBuffer<AnimationParameterValue>(actor); foreach (var parameter in defaults) values.Add(new AnimationParameterValue { Float = parameter.defaultFloat });
                em.AddBuffer<AnimationClipRequest>(actor); em.AddBuffer<SampledBonePose>(actor); em.AddBuffer<BoneLocalPose>(actor);
                var step = em.CreateEntity(typeof(AnimationStepContext)); var advance = world.GetOrCreateSystemManaged<ControllerAdvanceSystem>();
                var sample = world.GetOrCreateSystemManaged<AnimationSampleSystem>(); var blend = world.GetOrCreateSystemManaged<AnimationBlendSystem>();
                var transforms = new[] { go.transform, a, child, b }; float maxPosition = 0, maxRotation = 0, maxScale = 0; double maxClock = 0; var worst = "";
                for (var tick = 0; tick < 120; tick++)
                {
                    if (scenario == "sharedTrigger" && tick == 10)
                    {
                        playable.SetTrigger("Go");
                        Assert.That(AnimationApi.SetTrigger(new AnimationController { Definition = converted.Controller }, em.GetBuffer<AnimationParameterValue>(actor), new AnimationParameterId(3)), Is.EqualTo(AnimationParameterResult.Success));
                    }
                    if (scenario == "zeroLayerTree" && (tick == 40 || tick == 80))
                    {
                        var parameters = em.GetBuffer<AnimationParameterValue>(actor);
                        for (var p = 0; p < 3; p++)
                        {
                            var value = tick == 40 ? 0 : defaults[p].defaultFloat;
                            playable.SetFloat("W" + p, value); parameters[p] = new AnimationParameterValue { Float = value };
                        }
                    }
                    if ((scenario == "syncedTiming" || scenario == "overridePartial") && (tick == 40 || tick == 80))
                    {
                        var weight = tick == 40 ? .7f : .15f; playable.SetLayerWeight(1, weight);
                        Assert.That(AnimationApi.SetLayerWeight(new AnimationController { Definition = converted.Controller }, em.GetBuffer<ControllerLayerRuntime>(actor), 1, weight), Is.EqualTo(AnimationLayerResult.Success));
                    }
                    // Keep steady-state comparisons away from an exactly coincident
                    // discontinuous loop seam; loop endpoints have dedicated tests.
                    var dt = tick == 0 ? 0 : .019f; native.Evaluate(dt);
                    em.SetComponentData(step, new AnimationStepContext { Tick = (ulong)tick + 1, DeltaTime = dt });
                    advance.Update(); sample.Update(); blend.Update(); em.CompleteAllTrackedJobs();
                    var local = em.GetBuffer<BoneLocalPose>(actor);
                    maxClock = Math.Max(maxClock, Math.Abs(em.GetComponentData<ControllerLayerState>(actor).NormalizedTime - playable.GetCurrentAnimatorStateInfo(0).normalizedTime));
                    for (var i = 1; i < transforms.Length; i++)
                    {
                        var error = math.distance(local[i].Position, transforms[i].localPosition);
                        if (error > maxPosition) { maxPosition = error; worst = "tick=" + tick + " bone=" + i + " native=" + transforms[i].localPosition.ToString("R") + " ecs=" + local[i].Position + " nativeClock=" + playable.GetCurrentAnimatorStateInfo(0).normalizedTime + " ecsClock=" + em.GetComponentData<ControllerLayerState>(actor).NormalizedTime; }
                        maxRotation = math.max(maxRotation, Quaternion.Angle(local[i].Rotation, transforms[i].localRotation));
                        maxScale = math.max(maxScale, math.distance(local[i].Scale, transforms[i].localScale));
                    }
                }
                var report = scenario + " position=" + maxPosition + " rotation=" + maxRotation + " scale=" + maxScale + " clock=" + maxClock + " " + worst;
                System.IO.Directory.CreateDirectory("Artifacts/Verification/AnimatorValidation");
                System.IO.File.WriteAllText("Artifacts/Verification/AnimatorValidation/Layer-" + scenario + ".txt", report);
                Debug.Log(report); Assert.That(maxPosition <= 1e-4f && maxRotation <= .05f && maxScale <= 1e-4f && maxClock <= 2e-5, Is.True, report);
            }
            finally
            {
                if (native.IsValid()) native.Destroy(); if (world != null && world.IsCreated) world.Dispose();
                if (converted.Controller.IsCreated) converted.Controller.Dispose(); if (library.IsCreated) library.Dispose(); foreach (var clip in baked) if (clip.IsCreated) clip.Dispose();
                if (rig.IsCreated) rig.Dispose(); Object.DestroyImmediate(go); Object.DestroyImmediate(settings); EditorSceneManager.ClosePreviewScene(scene); AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
