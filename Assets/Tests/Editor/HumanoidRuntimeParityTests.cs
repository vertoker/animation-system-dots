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
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace AnimationSystemDOTS.Tests
{
    public class HumanoidRuntimeParityTests
    {
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(2, 1)]
        [TestCase(2, 2)]
        [TestCase(2, 3)]
        [TestCase(2, 4)]
        [TestCase(3, 0, true)]
        [TestCase(2, 1, true)]
        [TestCase(2, 2, true)]
        [TestCase(2, 3, true)]
        [TestCase(2, 0, false, true)]
        [TestCase(2, 5)]
        [TestCase(2, 6)]
        [TestCase(2, 7)]
        [TestCase(2, 8)]
        [TestCase(2, 9)]
        public void PreciseMuscleBlending_ActualECSWorldMatchesNativeMixamoGraph(int inputCount, int layerMode = 0, bool dense = false, bool transformedSource = false)
        {
            var root = Sandbox.Editor.SandboxPaths.Root + "Humanoid/";
            var additive = layerMode == 2 || layerMode == 3;
            var scene = EditorSceneManager.NewPreviewScene();
            var graph = default(PlayableGraph);
            var rig = default(BlobAssetReference<RigDefinitionBlob>);
            var library = default(BlobAssetReference<AnimationLibraryBlob>);
            var maskBlob = default(BlobAssetReference<BoneMaskBlob>);
            AvatarMask nativeMask = null;
            AnimationClip additiveClip = null;
            HumanPoseHandler nativeBodyReader = null;
            var baked = new List<BlobAssetReference<AnimationClipBlob>>();
            var settings = ScriptableObject.CreateInstance<AnimationBakeSettings>();
            settings.HumanoidPoseMode = HumanoidPoseMode.PreciseMuscles;
            settings.SampleRate = 960; settings.PositionTolerance = .002f; settings.AngleToleranceDegrees = .25f;
            GameObject source = null;
            try
            {
                source = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(root + "Model/BaldMale.fbx"), new InstantiateParameters { scene = scene });
                if (transformedSource)
                {
                    source.transform.position = new Vector3(7, 2, -3); source.transform.rotation = Quaternion.Euler(0, 37, 0);
                    source.transform.localScale = Vector3.one * 1.3f;
                }
                var result = RigBakePipeline.Bake(source); Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics)); rig = result.Rig;
                var prefixes = layerMode == 0 ? new[] { "Y_Bot_Idle_", "Y_Bot_Running_", "Y_Bot_Waving_" } : new[] { "Y_Bot_Running_", "Y_Bot_Waving_" };
                var clips = prefixes.Take(inputCount).Select(prefix =>
                    AssetDatabase.FindAssets("t:Model", new[] { root + "Mixamo" }).Select(AssetDatabase.GUIDToAssetPath)
                        .Where(p => Path.GetFileName(p).StartsWith(prefix, StringComparison.Ordinal)).OrderBy(p => p.Length).ThenBy(p => p)
                        .SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"))).ToArray();
                if (additive)
                {
                    additiveClip = UnityEngine.Object.Instantiate(clips[1]); additiveClip.name += " Additive Ref Run0";
                    var reference = AnimationUtility.GetAnimationClipSettings(additiveClip);
                    reference.hasAdditiveReferencePose = true; reference.additiveReferencePoseClip = clips[0]; reference.additiveReferencePoseTime = 0;
                    AnimationUtility.SetAnimationClipSettings(additiveClip, reference); clips[1] = additiveClip;
                }
                if (layerMode == 1 || layerMode == 2 || layerMode >= 5)
                {
                    nativeMask = new AvatarMask();
                    for (var part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++) nativeMask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)part, false);
                    var parts = layerMode == 5 ? new[] { AvatarMaskBodyPart.Root }
                        : layerMode == 6 ? new[] { AvatarMaskBodyPart.Head }
                        : layerMode == 7 ? new[] { AvatarMaskBodyPart.RightArm }
                        : layerMode == 8 ? new[] { AvatarMaskBodyPart.RightFingers }
                        : layerMode == 9 ? new[] { AvatarMaskBodyPart.Root, AvatarMaskBodyPart.Body }
                        : new[] { AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm,
                            AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers };
                    foreach (var part in parts) nativeMask.SetHumanoidBodyPartActive(part, true);
                    maskBlob = AvatarMaskBakePipeline.Build(nativeMask, source, rig);
                }
                foreach (var clip in clips)
                {
                    var sample = ClipBakePipeline.Bake(new ClipBakeRequest { Source = source, Clip = clip, Rig = rig, Settings = settings });
                    Assert.That(sample.Success, Is.True, string.Join("\n", sample.Diagnostics)); baked.Add(sample.Clip);
                    Assert.That(sample.Clip.Value.HumanMuscles.Length, Is.EqualTo(HumanTrait.MuscleCount));
                }
                var libraryResult = LibraryBakePipeline.Build(rig, baked); Assert.That(libraryResult.Success, Is.True); library = libraryResult.Library;
                var transforms = Enumerable.Range(0, rig.Value.Paths.Length).Select(i => i == 0 ? source.transform : source.transform.Find(rig.Value.Paths[i].ToString())).ToArray();
                var rest = transforms.Select(t => new BoneLocalPose { Position = t.localPosition, Rotation = t.localRotation, Scale = t.localScale }).ToArray();
                var animator = source.GetComponent<Animator>(); animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
                nativeBodyReader = new HumanPoseHandler(animator.avatar, source.transform);
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.fireEvents = false;
                graph = PlayableGraph.Create("AnimationSystemDOTS Precise Humanoid Runtime Oracle"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var mixer = AnimationMixerPlayable.Create(graph, inputCount, true); var inputs = new AnimationClipPlayable[inputCount];
                var layerMixer = layerMode == 0 ? default(AnimationLayerMixerPlayable) : AnimationLayerMixerPlayable.Create(graph, inputCount);
                for (var i = 0; i < inputCount; i++)
                {
                    inputs[i] = AnimationClipPlayable.Create(graph, clips[i]); inputs[i].SetApplyFootIK(false); inputs[i].SetApplyPlayableIK(false);
                    if (layerMode == 0) graph.Connect(inputs[i], 0, mixer, i); else graph.Connect(inputs[i], 0, layerMixer, i);
                }
                if (layerMode != 0)
                {
                    layerMixer.SetLayerAdditive(1, additive);
                    if (nativeMask != null) layerMixer.SetLayerMaskFromAvatarMask(1, nativeMask);
                }
                var output = AnimationPlayableOutput.Create(graph, "Native oracle", animator);
                if (layerMode == 0) output.SetSourcePlayable(mixer); else output.SetSourcePlayable(layerMixer);
                graph.Play();
                float maxPosition = 0, maxAngle = 0, maxBodyPosition = 0, maxBodyAngle = 0, maxNativeHipFromBase = 0, maxNativeHipAngleFromBase = 0;
                float maxNonHipAngle = 0, maxLocalPosition = 0;
                string positionBone = "", angleBone = "";
                var bodyTrace = new List<string>();
                using (var world = new World("AnimationSystemDOTS Precise Humanoid Runtime Parity"))
                {
                    var em = world.EntityManager;
                    em.CreateEntity(typeof(AnimationStepContext));
                    var entity = em.CreateEntity(typeof(AnimationRig), typeof(PreciseHumanoidPose), typeof(HumanoidRigBinding), typeof(HumanoidBodyPose));
                    em.SetComponentData(entity, new AnimationRig { Rig = rig, Library = library });
                    em.SetComponentData(entity, new HumanoidRigBinding { Avatar = animator.avatar });
                    em.AddBuffer<AnimationClipRequest>(entity); em.AddBuffer<HumanoidMuscleValue>(entity);
                    em.AddBuffer<HumanoidRootMuscleValue>(entity);
                    em.AddBuffer<BoneLocalPose>(entity); em.AddBuffer<BoneModelPose>(entity);
                    var blendSystem = world.GetOrCreateSystemManaged<HumanoidBlendSystem>();
                    var retargetSystem = world.GetOrCreateSystemManaged<HumanoidRetargetSystem>();
                    var modelSystem = world.GetOrCreateSystemManaged<ModelPoseSystem>();
                    var animatorCount = Resources.FindObjectsOfTypeAll<Animator>().Length;
                    var phases = dense ? Enumerable.Range(0, 240).Select(i => i / 240.0).ToArray() : new[] { .07, .23, .47, .71, .93 };
                    foreach (var phase in phases)
                    foreach (var blend in new[] { .2f, .5f, .8f })
                    {
                        var weights = inputCount == 1 ? new[] { 1f } : inputCount == 2 ? new[] { 1 - blend, blend } : new[] { (1 - blend) * .6f, (1 - blend) * .4f, blend };
                        if (layerMode != 0) weights[0] = 1;
                        for (var b = 0; b < transforms.Length; b++)
                        { transforms[b].localPosition = rest[b].Position; transforms[b].localRotation = rest[b].Rotation; transforms[b].localScale = rest[b].Scale; }
                        var requests = em.GetBuffer<AnimationClipRequest>(entity); requests.Clear();
                        for (var i = 0; i < inputCount; i++)
                        {
                            inputs[i].SetTime(phase * clips[i].length); inputs[i].SetDone(false);
                            if (layerMode == 0) mixer.SetInputWeight(i, weights[i]); else layerMixer.SetInputWeight(i, weights[i]);
                            requests.Add(new AnimationClipRequest { Clip = new ClipId(i), Time = phase * clips[i].length, Weight = weights[i],
                                Layer = layerMode != 0 ? i : 0, Mask = i == 1 ? maskBlob : default,
                                BlendMode = i == 1 && additive ? PoseBlendMode.Additive : PoseBlendMode.Override });
                        }
                        graph.Evaluate(0);
                        var nativeBody = new HumanPose(); nativeBodyReader.GetHumanPose(ref nativeBody);
                        blendSystem.Update(); retargetSystem.Update(); modelSystem.Update(); em.CompleteAllTrackedJobs();
                        Assert.That(em.GetComponentData<HumanoidBodyPose>(entity).Result, Is.EqualTo(HumanoidEvaluationResult.Success));
                        var actualBody = em.GetComponentData<HumanoidBodyPose>(entity);
                        maxBodyPosition = math.max(maxBodyPosition, Vector3.Distance(actualBody.Position, nativeBody.bodyPosition));
                        maxBodyAngle = math.max(maxBodyAngle, Quaternion.Angle(actualBody.Rotation, nativeBody.bodyRotation));
                        bodyTrace.Add("phase=" + phase + " weight=" + blend + " nativeP=" + nativeBody.bodyPosition.ToString("R")
                            + " actualP=" + ((Vector3)actualBody.Position).ToString("R") + " nativeQ=" + nativeBody.bodyRotation.ToString("R")
                            + " actualQ=" + ((Quaternion)actualBody.Rotation).ToString("R"));
                        var local = em.GetBuffer<BoneLocalPose>(entity); var model = em.GetBuffer<BoneModelPose>(entity);
                        Assert.That(local.Length, Is.EqualTo(transforms.Length)); Assert.That(model.Length, Is.EqualTo(transforms.Length));
                        for (var b = 1; b < transforms.Length; b++)
                        {
                            var isHip = rig.Value.Paths[b].ToString().EndsWith("/pelvis", StringComparison.Ordinal);
                            var distance = Vector3.Distance(model[b].Matrix.c3, source.transform.InverseTransformPoint(transforms[b].position));
                            var angle = Quaternion.Angle(local[b].Rotation, transforms[b].localRotation);
                            if (!isHip) maxNonHipAngle = math.max(maxNonHipAngle, angle);
                            maxLocalPosition = math.max(maxLocalPosition, Vector3.Distance(local[b].Position, transforms[b].localPosition));
                            if (isHip && layerMode != 0)
                            {
                                var baseHip = ClipSampler.SampleBone(ref library.Value.Clips[0], b, phase * clips[0].length);
                                maxNativeHipFromBase = math.max(maxNativeHipFromBase, Vector3.Distance(baseHip.Position, transforms[b].localPosition));
                                maxNativeHipAngleFromBase = math.max(maxNativeHipAngleFromBase, Quaternion.Angle(baseHip.Rotation, transforms[b].localRotation));
                            }
                            if (distance > maxPosition) { maxPosition = distance; positionBone = rig.Value.Paths[b].ToString(); }
                            if (angle > maxAngle) { maxAngle = angle; angleBone = rig.Value.Paths[b].ToString(); }
                        }
                    }
                    Assert.That(Resources.FindObjectsOfTypeAll<Animator>().Length, Is.EqualTo(animatorCount), "The ECS runtime must not instantiate Animators.");
                    var requestsToClear = em.GetBuffer<AnimationClipRequest>(entity); requestsToClear.Clear();
                    blendSystem.Update(); retargetSystem.Update(); em.CompleteAllTrackedJobs();
                    Assert.That(em.GetComponentData<HumanoidBodyPose>(entity).Result, Is.EqualTo(HumanoidEvaluationResult.NoRequests));
                    var restored = em.GetBuffer<BoneLocalPose>(entity);
                    for (var b = 0; b < restored.Length; b++) Assert.That(restored[b].Position, Is.EqualTo(rig.Value.RestLocalPoses[b].Position));
                }
                var report = "Actual ECS runtime: inputs=" + inputCount + " layerMode=" + layerMode + " dense=" + dense + " transformed=" + transformedSource + " maxJointMeters=" + maxPosition.ToString("R") + " maxLocalDegrees=" + maxAngle.ToString("R")
                    + " bodyMeters=" + maxBodyPosition + " bodyDegrees=" + maxBodyAngle + " positionBone=" + positionBone + " angleBone=" + angleBone;
                report += " nativeHipVsBaseMeters=" + maxNativeHipFromBase + " nativeHipVsBaseDegrees=" + maxNativeHipAngleFromBase
                    + " nonHipDegrees=" + maxNonHipAngle + " localMeters=" + maxLocalPosition;
                TestContext.WriteLine(report);
                Directory.CreateDirectory("Artifacts/Verification/AnimatorValidation");
                File.WriteAllText("Artifacts/Verification/AnimatorValidation/RuntimeHumanoid" + inputCount + "Layer" + layerMode + (dense ? "Dense" : "") + (transformedSource ? "Transformed" : "") + ".txt", report);
                File.WriteAllLines("Artifacts/Verification/AnimatorValidation/RuntimeBodyLayer" + layerMode + (dense ? "Dense" : "") + ".txt", bodyTrace);
                Assert.That(maxPosition, Is.LessThanOrEqualTo(.02f), report); Assert.That(maxAngle, Is.LessThanOrEqualTo(2f), report);
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                nativeBodyReader?.Dispose();
                if (maskBlob.IsCreated) maskBlob.Dispose();
                if (library.IsCreated) library.Dispose(); foreach (var clip in baked) if (clip.IsCreated) clip.Dispose();
                if (rig.IsCreated) rig.Dispose();
                if (source != null) UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(settings);
                if (nativeMask != null) UnityEngine.Object.DestroyImmediate(nativeMask);
                if (additiveClip != null) UnityEngine.Object.DestroyImmediate(additiveClip);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
