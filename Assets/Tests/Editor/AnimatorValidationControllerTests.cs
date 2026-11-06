using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimationSystemDOTS.Authoring;
using AnimationSystemDOTS.Editor.Baking;
using AnimationSystemDOTS.Humanoid;
using AnimationSystemDOTS.Sandbox;
using AnimationSystemDOTS.Sandbox.Editor;
using NUnit.Framework;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
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
                ValidateEveryClip(source, rig, library, inputs);
                ValidateRichTrace(source, rig, library, graph, controller);
                Directory.CreateDirectory("Artifacts/Verification/AnimatorValidation");
                File.WriteAllText("Artifacts/Verification/AnimatorValidation/SharedController-bake.txt", "clips=" + inputs.Length + " states=" + graph.Value.States.Length + " machines=" + graph.Value.Machines.Length + " layers=" + graph.Value.Layers.Length + " motions=" + graph.Value.Motions.Length);
            }
            finally
            {
                if (graph.IsCreated) graph.Dispose(); if (library.IsCreated) library.Dispose(); foreach (var clip in clips) if (clip.IsCreated) clip.Dispose(); if (rig.IsCreated) rig.Dispose();
                if (source != null) Object.DestroyImmediate(source); EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        static void ValidateRichTrace(GameObject source, BlobAssetReference<RigDefinitionBlob> rig, BlobAssetReference<AnimationLibraryBlob> library,
            BlobAssetReference<AnimatorControllerBlob> graph, AnimatorController controller)
        {
            var animator = source.GetComponent<Animator>(); animator.runtimeAnimatorController = controller; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var driver = source.AddComponent<NativeAnimatorCharacter>(); driver.enabled = false; driver.Animator = animator;
            driver.Settings = AssetDatabase.LoadAssetAtPath<AnimatorValidationSettings>(AnimatorValidationFixtures.SettingsPath); driver.AutomatedTrace = true; driver.ResetCharacter();
            var transforms = Enumerable.Range(0, rig.Value.Paths.Length).Select(i => i == 0 ? source.transform : source.transform.Find(rig.Value.Paths[i].ToString())).ToArray();
            try
            {
                using (var world = new World("Shared graph native/ECS trace"))
                {
                    var em = world.EntityManager; em.CreateEntity(typeof(AnimationStepContext)); var owner = em.CreateEntity(typeof(CharacterSceneControl));
                    em.SetComponentData(owner, new CharacterSceneControl { AutomatedTrace = 1 });
                    var actor = em.CreateEntity(typeof(AnimationRig), typeof(AnimationController), typeof(ControllerLayerState), typeof(AnimationClock), typeof(AnimationPlaybackMode),
                        typeof(PreciseHumanoidPose), typeof(HumanoidRigBinding), typeof(HumanoidBodyPose), typeof(LocalTransform), typeof(CharacterMember), typeof(CharacterBotState), typeof(CharacterParameters));
                    var definition = new AnimationController { Definition = graph };
                    em.SetComponentData(actor, new AnimationRig { Rig = rig, Library = library }); em.SetComponentData(actor, definition);
                    em.SetComponentData(actor, new AnimationPlaybackMode { Value = PlaybackDriver.Controller }); em.SetComponentData(actor, new HumanoidRigBinding { Avatar = animator.avatar });
                    em.SetComponentData(actor, LocalTransform.Identity); em.SetComponentData(actor, CharacterParameters.Resolve(definition));
                    em.SetComponentData(actor, new CharacterMember { Owner = owner, ResetRequested = 1, Weights = driver.Settings.LayerWeights, Durations = driver.Settings.Durations });
                    em.AddBuffer<AnimationParameterValue>(actor); em.AddBuffer<AnimationClipRequest>(actor); em.AddBuffer<ControllerLayerRuntime>(actor); em.AddBuffer<ControllerCachedRequest>(actor);
                    em.AddBuffer<HumanoidMuscleValue>(actor); em.AddBuffer<HumanoidRootMuscleValue>(actor); em.AddBuffer<BoneLocalPose>(actor); em.AddBuffer<BoneModelPose>(actor);
                    var group = world.GetOrCreateSystemManaged<AnimationSimulationSystemGroup>();
                    group.AddSystemToUpdateList(world.GetOrCreateSystemManaged<CharacterClockSystem>()); group.AddSystemToUpdateList(world.GetOrCreateSystemManaged<CharacterPoseResetSystem>());
                    group.AddSystemToUpdateList(world.GetOrCreateSystemManaged<CharacterMotorSystem>()); group.AddSystemToUpdateList(world.GetOrCreateSystemManaged<ControllerAdvanceSystem>());
                    group.AddSystemToUpdateList(world.GetOrCreateSystemManaged<AnimationSampleSystem>()); group.AddSystemToUpdateList(world.GetOrCreateSystemManaged<AnimationBlendSystem>());
                    group.AddSystemToUpdateList(world.GetOrCreateSystemManaged<HumanoidBlendSystem>()); group.AddSystemToUpdateList(world.GetOrCreateSystemManaged<HumanoidRetargetSystem>());
                    group.AddSystemToUpdateList(world.GetOrCreateSystemManaged<AnimationPoseModificationSystemGroup>());
                    group.AddSystemToUpdateList(world.GetOrCreateSystemManaged<ModelPoseSystem>()); group.SortSystems();
                    float maxPosition = 0, maxRotation = 0, maxMotor = 0; double maxClock = 0; var worst = ""; var states = new HashSet<string>(); var clockWorst = ""; var diagnostic = new System.Text.StringBuilder();
                    for (var tick = 0; tick < CharacterTestTrace.TickCount; tick++)
                    {
                        driver.SimulateStep(); animator.Update(AnimatorValidationSettings.Step);
                        group.Update(); em.CompleteAllTrackedJobs();
                        var member = em.GetComponentData<CharacterMember>(actor); maxMotor = math.max(maxMotor, math.distance(driver.Motor.Position, member.Motor.Position));
                        var state = em.GetComponentData<ControllerLayerState>(actor); var nativeState = animator.GetCurrentAnimatorStateInfo(0);
                        var clockError = Math.Abs(nativeState.normalizedTime - state.NormalizedTime);
                        if (clockError > maxClock) { maxClock = clockError; clockWorst = "tick=" + tick + " native=" + nativeState.normalizedTime + " ecs=" + state.NormalizedTime; }
                        var layers = em.GetBuffer<ControllerLayerRuntime>(actor); for (var l = 0; l < layers.Length; l++) states.Add(graph.Value.States[layers[l].State.CurrentState].Path.ToString());
                        var local = em.GetBuffer<BoneLocalPose>(actor); var model = em.GetBuffer<BoneModelPose>(actor);
                        Assert.That(local.Length, Is.EqualTo(transforms.Length), "tick=" + tick + " result=" + em.GetComponentData<HumanoidBodyPose>(actor).Result);
                        float framePosition = 0, frameAngle = 0;
                        for (var bone = 1; bone < transforms.Length; bone++)
                        {
                            var error = math.distance(model[bone].Matrix.c3, source.transform.InverseTransformPoint(transforms[bone].position));
                            if (error > maxPosition) { maxPosition = error; worst = "tick=" + tick + " tree=" + member.Command.TreeMode + " bone=" + rig.Value.Paths[bone].ToString(); }
                            maxRotation = math.max(maxRotation, Quaternion.Angle(local[bone].Rotation, transforms[bone].localRotation));
                            framePosition = math.max(framePosition, error); frameAngle = math.max(frameAngle, Quaternion.Angle(local[bone].Rotation, transforms[bone].localRotation));
                        }
                        if (framePosition > .02f && (diagnostic.Length < 5000 || tick == 1577))
                        {
                            diagnostic.Append("tick=" + tick + " pos=" + framePosition + " angle=" + frameAngle + " actions=" + member.Command.Actions + " grounded=" + member.Motor.Grounded);
                            for (var l = 0; l < layers.Length; l++)
                            { var ls = layers[l].State; var ni = animator.GetCurrentAnimatorStateInfo(l); var nn = animator.GetNextAnimatorStateInfo(l);
                                diagnostic.Append("; L" + l + " native=" + driver.Settings.StateName(ni.shortNameHash) + "/" + ni.normalizedTime + "->" + driver.Settings.StateName(nn.shortNameHash) + "/" + nn.normalizedTime + " transition=" + animator.IsInTransition(l) + " weight=" + animator.GetLayerWeight(l)
                                    + " ecs=" + graph.Value.States[ls.CurrentState].Path.ToString() + "/" + ls.NormalizedTime + "->" + (ls.NextState >= 0 ? graph.Value.States[ls.NextState].Path.ToString() : "none") + "/" + ls.NextNormalizedTime + " fade=" + ls.TransitionTime + "/" + ls.TransitionDuration + " weight=" + layers[l].Weight); }
                            diagnostic.AppendLine();
                        }
                    }
                    Directory.CreateDirectory("Artifacts/Verification/AnimatorValidation");
                    File.WriteAllText("Artifacts/Verification/AnimatorValidation/SharedController-trace.txt", "motor=" + maxMotor + " position=" + maxPosition + " rotation=" + maxRotation + " clock=" + maxClock + " " + worst + " clockWorst=" + clockWorst + "\n" + string.Join("\n", states.OrderBy(s => s)) + "\n" + diagnostic);
                    Assert.That(maxMotor, Is.LessThan(.0001f)); Assert.That(maxPosition, Is.LessThanOrEqualTo(.02f), worst); Assert.That(maxRotation, Is.LessThanOrEqualTo(2)); Assert.That(maxClock, Is.LessThanOrEqualTo(.00002), clockWorst);
                    Assert.That(states.Count, Is.EqualTo(graph.Value.States.Length), "The trace must visit every shared controller state, including all jump/death/action variants.");
                }
            }
            finally { Object.DestroyImmediate(driver); }
        }
        static void ValidateEveryClip(GameObject source, BlobAssetReference<RigDefinitionBlob> rig, BlobAssetReference<AnimationLibraryBlob> library, AnimationClip[] inputs)
        {
            using (var world = new World("Animator validation — every source clip"))
            {
                var em = world.EntityManager;
                var actor = em.CreateEntity(typeof(AnimationRig), typeof(PreciseHumanoidPose), typeof(HumanoidRigBinding), typeof(HumanoidBodyPose));
                em.SetComponentData(actor, new AnimationRig { Rig = rig, Library = library }); em.SetComponentData(actor, new HumanoidRigBinding { Avatar = source.GetComponent<Animator>().avatar });
                var requests = em.AddBuffer<AnimationClipRequest>(actor); em.AddBuffer<HumanoidMuscleValue>(actor); em.AddBuffer<HumanoidRootMuscleValue>(actor);
                em.AddBuffer<BoneLocalPose>(actor); em.AddBuffer<BoneModelPose>(actor); var step = em.CreateEntity(typeof(AnimationStepContext));
                var blend = world.GetOrCreateSystemManaged<HumanoidBlendSystem>(); var retarget = world.GetOrCreateSystemManaged<HumanoidRetargetSystem>(); var model = world.GetOrCreateSystemManaged<ModelPoseSystem>();
                var expected = new float4x4[rig.Value.Parents.Length]; var report = new System.Text.StringBuilder(); ulong tick = 0;
                for (var clipIndex = 0; clipIndex < inputs.Length; clipIndex++)
                {
                    float maxPosition = 0, maxRotation = 0; var worst = "";
                    for (var phase = 0; phase < 16; phase++)
                    {
                        var time = library.Value.Clips[clipIndex].Duration * phase / 16;
                        requests = em.GetBuffer<AnimationClipRequest>(actor); requests.Clear(); requests.Add(new AnimationClipRequest { Clip = new ClipId(clipIndex), Time = time, Weight = 1, LoopBoundary = ClipLoopBoundary.Start });
                        em.SetComponentData(step, new AnimationStepContext { Tick = ++tick }); blend.Update(); retarget.Update(); model.Update(); em.CompleteAllTrackedJobs();
                        Assert.That(em.GetComponentData<HumanoidBodyPose>(actor).Result, Is.EqualTo(HumanoidEvaluationResult.Success), inputs[clipIndex].name);
                        var local = em.GetBuffer<BoneLocalPose>(actor); var actual = em.GetBuffer<BoneModelPose>(actor);
                        for (var bone = 0; bone < expected.Length; bone++)
                        {
                            var reference = ClipSampler.SampleBone(ref library.Value.Clips[clipIndex], bone, time, ClipLoopBoundary.Start);
                            var matrix = float4x4.TRS(reference.Position, reference.Rotation, reference.Scale); var parent = rig.Value.Parents[bone]; expected[bone] = parent < 0 ? matrix : math.mul(expected[parent], matrix);
                            var error = math.distance(actual[bone].Matrix.c3.xyz, expected[bone].c3.xyz);
                            if (error > maxPosition) { maxPosition = error; worst = "bone=" + rig.Value.Paths[bone].ToString() + " phase=" + phase; }
                            maxRotation = math.max(maxRotation, Quaternion.Angle(local[bone].Rotation, reference.Rotation));
                        }
                    }
                    report.AppendLine(inputs[clipIndex].name + ";position=" + maxPosition + ";angle=" + maxRotation + ";" + worst);
                    Directory.CreateDirectory("Artifacts/Verification/AnimatorValidation"); File.WriteAllText("Artifacts/Verification/AnimatorValidation/EveryClip-precise-vs-native.txt", report.ToString());
                    Assert.That(maxPosition, Is.LessThanOrEqualTo(.02f), inputs[clipIndex].name + " " + worst); Assert.That(maxRotation, Is.LessThanOrEqualTo(2), inputs[clipIndex].name);
                }
            }
        }
    }
}
