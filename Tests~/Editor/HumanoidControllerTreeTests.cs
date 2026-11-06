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
        public void RealHumanoidController_TreePartialAndZeroWeightsMatchNative(bool direct)
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
                var state = controller.layers[0].stateMachine.AddState("Human"); state.motion = tree; state.writeDefaultValues = false;
                controller.layers[0].stateMachine.defaultState = state;
                var converted = ControllerBakePipeline.Build(controller, clips, library);
                Assert.That(converted.Success, Is.True, string.Join("\n", converted.Diagnostics)); controllerBlob = converted.Controller;
                var animator = source.GetComponent<Animator>(); animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                native = PlayableGraph.Create("AnimationSystemDOTS Human Controller Oracle"); native.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimatorControllerPlayable.Create(native, controller);
                AnimationPlayableOutput.Create(native, "Native", animator).SetSourcePlayable(playable); native.Play();
                var transforms = Enumerable.Range(0, rig.Value.Paths.Length).Select(i => i == 0 ? source.transform : source.transform.Find(rig.Value.Paths[i].ToString())).ToArray();
                world = new World("AnimationSystemDOTS Human Controller Tree"); var em = world.EntityManager;
                var step = em.CreateEntity(typeof(AnimationStepContext));
                var actor = em.CreateEntity(typeof(AnimationRig), typeof(AnimationController), typeof(ControllerLayerState), typeof(AnimationClock), typeof(AnimationPlaybackMode),
                    typeof(PreciseHumanoidPose), typeof(HumanoidRigBinding), typeof(HumanoidBodyPose));
                em.SetComponentData(actor, new AnimationRig { Rig = rig, Library = library }); em.SetComponentData(actor, new AnimationController { Definition = controllerBlob });
                em.SetComponentData(actor, new AnimationPlaybackMode { Value = PlaybackDriver.Controller }); em.SetComponentData(actor, new HumanoidRigBinding { Avatar = animator.avatar });
                em.AddBuffer<AnimationParameterValue>(actor).ResizeUninitialized(2); em.AddBuffer<AnimationClipRequest>(actor);
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
                Debug.Log("Human controller " + (direct ? "Direct" : "1D") + " max=" + maxPosition + "m/" + maxRotation + "deg " + worst);
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
