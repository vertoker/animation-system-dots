using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimationSystemDOTS.Authoring;
using AnimationSystemDOTS.Editor.Baking;
using AnimationSystemDOTS.Humanoid;
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
    public class HumanoidControllerTreeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        [TestCase(false, 1)]
        [TestCase(false, 2)]
        [TestCase(false, 3)]
        [TestCase(false, 4)]
        [TestCase(false, 5)]
        [TestCase(false, 6)]
        [TestCase(false, 7)]
        [TestCase(false, 0, 1)]
        [TestCase(false, 0, 2)]
        [TestCase(false, 1, 1)]
        public void RealHumanoidController_TreePartialAndZeroWeightsMatchNative(bool direct, int layerMode = 0, int graphMode = 0)
        {
            var folder = "Assets/__HumanControllerTree_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            var scene = EditorSceneManager.NewPreviewScene(); var native = default(PlayableGraph);
            GameObject source = null; World world = null;
            var rig = default(BlobAssetReference<RigDefinitionBlob>); var library = default(BlobAssetReference<AnimationLibraryBlob>);
            var controllerBlob = default(BlobAssetReference<AnimatorControllerBlob>);
            var baked = new List<BlobAssetReference<AnimationClipBlob>>();
            var settings = ScriptableObject.CreateInstance<AnimationBakeSettings>();
            settings.HumanoidPoseMode = HumanoidPoseMode.PreciseMuscles; settings.SampleRate = 960;
            settings.PositionTolerance = .002f; settings.AngleToleranceDegrees = .25f;
            try
            {
                var root = Sandbox.Editor.SandboxPaths.Root + "Humanoid/";
                source = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(root + "Model/BaldMale.fbx"), new InstantiateParameters { scene = scene });
                var rigResult = RigBakePipeline.Bake(source); Assert.That(rigResult.Success, Is.True); rig = rigResult.Rig;
                var clips = new[] { "Y_Bot_Running_", "Y_Bot_Waving_" }.Select(prefix =>
                    AssetDatabase.FindAssets("t:Model", new[] { root + "Mixamo" }).Select(AssetDatabase.GUIDToAssetPath)
                        .Where(p => Path.GetFileName(p).StartsWith(prefix, StringComparison.Ordinal)).OrderBy(p => p.Length).ThenBy(p => p)
                        .SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"))).ToArray();
                if (layerMode == 6 || layerMode == 7)
                {
                    var original = clips[0]; clips[0] = Object.Instantiate(original); clips[0].name = "Run additive reference Run0";
                    var reference = AnimationUtility.GetAnimationClipSettings(clips[0]); reference.hasAdditiveReferencePose = true;
                    reference.additiveReferencePoseClip = original; reference.additiveReferencePoseTime = 0;
                    AnimationUtility.SetAnimationClipSettings(clips[0], reference); AssetDatabase.CreateAsset(clips[0], folder + "/Run.anim");
                }
                if (layerMode == 2 || layerMode == 3 || layerMode == 6 || layerMode == 7)
                {
                    clips[1] = Object.Instantiate(clips[1]); clips[1].name = "Wave additive reference Run0";
                    var reference = AnimationUtility.GetAnimationClipSettings(clips[1]); reference.hasAdditiveReferencePose = true;
                    reference.additiveReferencePoseClip = clips[0]; reference.additiveReferencePoseTime = 0;
                    AnimationUtility.SetAnimationClipSettings(clips[1], reference); AssetDatabase.CreateAsset(clips[1], folder + "/Wave.anim");
                }
                foreach (var clip in clips)
                {
                    var result = ClipBakePipeline.Bake(new ClipBakeRequest { Source = source, Clip = clip, Rig = rig, Settings = settings });
                    Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics)); baked.Add(result.Clip);
                }
                library = LibraryBakePipeline.Build(rig, baked).Library;
                var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Human.controller");
                controller.AddParameter("A", AnimatorControllerParameterType.Float); controller.AddParameter("B", AnimatorControllerParameterType.Float);
                var tree = new BlendTree { name = "Human Tree", blendType = direct ? BlendTreeType.Direct : BlendTreeType.Simple1D, blendParameter = "A", useAutomaticThresholds = false };
                AssetDatabase.AddObjectToAsset(tree, controller);
                tree.children = new[] { new ChildMotion { motion = clips[0], timeScale = 1, directBlendParameter = "A" },
                    new ChildMotion { motion = clips[1], threshold = 1, timeScale = 1, directBlendParameter = "B" } };
                var serialized = new SerializedObject(tree); serialized.FindProperty("m_NormalizedBlendValues").boolValue = true; serialized.ApplyModifiedPropertiesWithoutUndo();
                var state = controller.layers[0].stateMachine.AddState("Human"); state.motion = layerMode > 0 ? (Motion)clips[0] : tree; state.writeDefaultValues = false;
                controller.layers[0].stateMachine.defaultState = state;
                if (graphMode > 0)
                {
                    foreach (var name in new[] { "Go", "Other", "Again" }) controller.AddParameter(name, AnimatorControllerParameterType.Trigger);
                    var machine = controller.layers[0].stateMachine;
                    var b = machine.AddState("WaveTarget"); b.motion = clips[1]; b.writeDefaultValues = false;
                    var c = machine.AddState("RunTarget"); c.motion = clips[0]; c.writeDefaultValues = false;
                    var first = ControllerGraphOracle.Edge(state, b, "Go", .8f);
                    first.interruptionSource = graphMode == 1 ? TransitionInterruptionSource.Source : TransitionInterruptionSource.Destination;
                    first.orderedInterruption = false;
                    var second = ControllerGraphOracle.Edge(graphMode == 1 ? state : b, c, "Other", .7f);
                    second.interruptionSource = graphMode == 1 ? TransitionInterruptionSource.Source : TransitionInterruptionSource.Destination;
                    second.orderedInterruption = false;
                    ControllerGraphOracle.Edge(graphMode == 1 ? state : c, b, "Again", .6f);
                }
                if (layerMode > 0)
                {
                    controller.AddLayer("Upper"); var layers = controller.layers; var upper = layers[1]; upper.defaultWeight = .35f;
                    upper.blendingMode = layerMode == 2 || layerMode == 3 || layerMode == 6 || layerMode == 7 ? AnimatorLayerBlendingMode.Additive : AnimatorLayerBlendingMode.Override;
                    var upperState = upper.stateMachine.AddState("Wave"); upperState.writeDefaultValues = false;
                    if (layerMode == 5 || layerMode == 6 || layerMode == 7)
                    {
                        tree.blendType = BlendTreeType.Direct; upperState.motion = tree;
                    }
                    else upperState.motion = clips[1];
                    upper.stateMachine.defaultState = upperState;
                    if (layerMode != 3 && layerMode != 7)
                    {
                        var mask = new AvatarMask();
                        for (var part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)part, false);
                        foreach (var part in new[] { AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm,
                            AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers }) mask.SetHumanoidBodyPartActive(part, true);
                        AssetDatabase.CreateAsset(mask, folder + "/Upper.mask"); upper.avatarMask = mask;
                    }
                    layers[1] = upper; controller.layers = layers;
                    if (layerMode == 4)
                    {
                        controller.AddLayer("Root and Body"); layers = controller.layers; layers[2].defaultWeight = .4f;
                        var rootState = layers[2].stateMachine.AddState("Root"); rootState.motion = clips[1]; rootState.writeDefaultValues = false; layers[2].stateMachine.defaultState = rootState;
                        var mask = new AvatarMask();
                        for (var part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)part, false);
                        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, true); mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
                        AssetDatabase.CreateAsset(mask, folder + "/Root.mask"); layers[2].avatarMask = mask; controller.layers = layers;
                    }
                }
                var converted = ControllerBakePipeline.Build(controller, clips, library, source, rig);
                if (layerMode == 7)
                {
                    Assert.That(converted.Success, Is.False);
                    Assert.That(string.Join("\n", converted.Diagnostics), Does.Contain(folder).And.Contain("/Upper").And.Contain("root motion"));
                    if (converted.Controller.IsCreated) converted.Controller.Dispose();
                    return;
                }
                Assert.That(converted.Success, Is.True, string.Join("\n", converted.Diagnostics)); controllerBlob = converted.Controller;
                var animator = source.GetComponent<Animator>(); animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                native = PlayableGraph.Create("AnimationSystemDOTS Human Controller Oracle"); native.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimatorControllerPlayable.Create(native, controller);
                AnimationPlayableOutput.Create(native, "Native", animator).SetSourcePlayable(playable); native.Play();
                for (var l = 1; l < controller.layers.Length; l++) playable.SetLayerWeight(l, controller.layers[l].defaultWeight);
                var transforms = Enumerable.Range(0, rig.Value.Paths.Length).Select(i => i == 0 ? source.transform : source.transform.Find(rig.Value.Paths[i].ToString())).ToArray();
                world = new World("AnimationSystemDOTS Human Controller Tree"); var em = world.EntityManager;
                var step = em.CreateEntity(typeof(AnimationStepContext));
                var actor = em.CreateEntity(typeof(AnimationRig), typeof(AnimationController), typeof(ControllerLayerState), typeof(AnimationClock), typeof(AnimationPlaybackMode),
                    typeof(PreciseHumanoidPose), typeof(HumanoidRigBinding), typeof(HumanoidBodyPose));
                em.SetComponentData(actor, new AnimationRig { Rig = rig, Library = library }); em.SetComponentData(actor, new AnimationController { Definition = controllerBlob });
                em.SetComponentData(actor, new AnimationPlaybackMode { Value = PlaybackDriver.Controller }); em.SetComponentData(actor, new HumanoidRigBinding { Avatar = animator.avatar });
                em.AddBuffer<AnimationParameterValue>(actor).Resize(controllerBlob.Value.Parameters.Length, NativeArrayOptions.ClearMemory); em.AddBuffer<AnimationClipRequest>(actor);
                em.AddBuffer<HumanoidMuscleValue>(actor); em.AddBuffer<HumanoidRootMuscleValue>(actor); em.AddBuffer<BoneLocalPose>(actor); em.AddBuffer<BoneModelPose>(actor);
                var advance = world.GetOrCreateSystemManaged<ControllerAdvanceSystem>(); var blend = world.GetOrCreateSystemManaged<HumanoidBlendSystem>();
                var retarget = world.GetOrCreateSystemManaged<HumanoidRetargetSystem>(); var model = world.GetOrCreateSystemManaged<ModelPoseSystem>();
                var inputs = new[] { new float2(1, 0), new float2(.125f, 0), new float2(.2f, .3f), new float2(0, 0),
                    new float2(.25f, .75f), new float2(3, 5), new float2(0, 0), new float2(.5f, .5f) };
                float maxPosition = 0, maxRotation = 0; var worst = "";
                for (var tick = 0; tick < 80; tick++)
                {
                    var input = inputs[tick / 10]; var parameters = em.GetBuffer<AnimationParameterValue>(actor);
                    parameters[0] = new AnimationParameterValue { Float = input.x }; parameters[1] = new AnimationParameterValue { Float = input.y };
                    playable.SetFloat("A", input.x); playable.SetFloat("B", input.y);
                    if (graphMode > 0 && (tick == 2 || tick == 6 || tick == 9))
                    {
                        var trigger = tick == 2 ? "Go" : tick == 6 ? "Other" : "Again"; playable.SetTrigger(trigger);
                        var definition = new AnimationController { Definition = controllerBlob };
                        AnimationApi.SetTrigger(definition, parameters, AnimationApi.FindParameter(definition, trigger));
                    }
                    var dt = tick == 0 ? 0 : .037f; native.Evaluate(dt);
                    em.SetComponentData(step, new AnimationStepContext { Tick = (ulong)tick + 1, DeltaTime = dt });
                    advance.Update(); blend.Update(); retarget.Update(); model.Update(); em.CompleteAllTrackedJobs();
                    var local = em.GetBuffer<BoneLocalPose>(actor); var poses = em.GetBuffer<BoneModelPose>(actor);
                    Assert.That(local.Length, Is.EqualTo(transforms.Length), "tick=" + tick + " result=" + em.GetComponentData<HumanoidBodyPose>(actor).Result);
                    for (var b = 1; b < transforms.Length; b++)
                    {
                        var position = source.transform.InverseTransformPoint(transforms[b].position);
                        var error = math.distance(poses[b].Matrix.c3, position);
                        if (error > maxPosition) { maxPosition = error; worst = "tick=" + tick + " bone=" + rig.Value.Paths[b].ToString() + " input=" + input; }
                        maxRotation = math.max(maxRotation, Quaternion.Angle(local[b].Rotation, transforms[b].localRotation));
                    }
                }
                var report = "Human controller " + (direct ? "Direct" : "1D") + " layerMode=" + layerMode + " graphMode=" + graphMode + " max=" + maxPosition + "m/" + maxRotation + "deg " + worst;
                Directory.CreateDirectory("Artifacts/Verification/AnimatorValidation");
                File.WriteAllText("Artifacts/Verification/AnimatorValidation/HumanController-" + direct + "-" + layerMode + "-" + graphMode + ".txt", report); Debug.Log(report);
                Assert.That(maxPosition, Is.LessThanOrEqualTo(.02f), worst);
                Assert.That(maxRotation, Is.LessThanOrEqualTo(2));
            }
            finally
            {
                if (native.IsValid()) native.Destroy(); if (world != null && world.IsCreated) world.Dispose();
                if (controllerBlob.IsCreated) controllerBlob.Dispose(); if (library.IsCreated) library.Dispose();
                foreach (var clip in baked) if (clip.IsCreated) clip.Dispose(); if (rig.IsCreated) rig.Dispose();
                if (source != null) Object.DestroyImmediate(source); Object.DestroyImmediate(settings);
                EditorSceneManager.ClosePreviewScene(scene); AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
