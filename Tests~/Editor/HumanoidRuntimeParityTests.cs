using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimationSystemDOTS.Authoring;
using AnimationSystemDOTS.Editor.Baking;
using AnimationSystemDOTS.Humanoid;
using NUnit.Framework;
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
        public void PreciseMuscleBlending_ActualECSWorldMatchesNativeMixamoGraph(int inputCount)
        {
            var root = Sandbox.Editor.SandboxPaths.Root + "Humanoid/";
            var scene = EditorSceneManager.NewPreviewScene();
            var graph = default(PlayableGraph);
            var rig = default(BlobAssetReference<RigDefinitionBlob>);
            var library = default(BlobAssetReference<AnimationLibraryBlob>);
            var baked = new List<BlobAssetReference<AnimationClipBlob>>();
            var settings = ScriptableObject.CreateInstance<AnimationBakeSettings>();
            settings.HumanoidPoseMode = HumanoidPoseMode.PreciseMuscles;
            settings.SampleRate = 960; settings.PositionTolerance = .002f; settings.AngleToleranceDegrees = .25f;
            GameObject source = null;
            try
            {
                source = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(root + "Model/BaldMale.fbx"), new InstantiateParameters { scene = scene });
                var result = RigBakePipeline.Bake(source); Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics)); rig = result.Rig;
                var clips = new[] { "Y_Bot_Idle_", "Y_Bot_Running_", "Y_Bot_Waving_" }.Take(inputCount).Select(prefix =>
                    AssetDatabase.FindAssets("t:Model", new[] { root + "Mixamo" }).Select(AssetDatabase.GUIDToAssetPath)
                        .Where(p => Path.GetFileName(p).StartsWith(prefix, StringComparison.Ordinal)).OrderBy(p => p.Length).ThenBy(p => p)
                        .SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"))).ToArray();
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
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.fireEvents = false;
                graph = PlayableGraph.Create("AnimationSystemDOTS Precise Humanoid Runtime Oracle"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var mixer = AnimationMixerPlayable.Create(graph, inputCount, true); var inputs = new AnimationClipPlayable[inputCount];
                for (var i = 0; i < inputCount; i++)
                { inputs[i] = AnimationClipPlayable.Create(graph, clips[i]); inputs[i].SetApplyFootIK(false); inputs[i].SetApplyPlayableIK(false); graph.Connect(inputs[i], 0, mixer, i); }
                AnimationPlayableOutput.Create(graph, "Native oracle", animator).SetSourcePlayable(mixer); graph.Play();
                float maxPosition = 0, maxAngle = 0;
                using (var world = new World("AnimationSystemDOTS Precise Humanoid Runtime Parity"))
                {
                    var em = world.EntityManager;
                    em.CreateEntity(typeof(AnimationStepContext));
                    var entity = em.CreateEntity(typeof(AnimationRig), typeof(PreciseHumanoidPose), typeof(HumanoidRigBinding), typeof(HumanoidBodyPose));
                    em.SetComponentData(entity, new AnimationRig { Rig = rig, Library = library });
                    em.SetComponentData(entity, new HumanoidRigBinding { Avatar = animator.avatar });
                    em.AddBuffer<AnimationClipRequest>(entity); em.AddBuffer<HumanoidMuscleValue>(entity);
                    em.AddBuffer<BoneLocalPose>(entity); em.AddBuffer<BoneModelPose>(entity);
                    var blendSystem = world.GetOrCreateSystemManaged<HumanoidBlendSystem>();
                    var retargetSystem = world.GetOrCreateSystemManaged<HumanoidRetargetSystem>();
                    var modelSystem = world.GetOrCreateSystemManaged<ModelPoseSystem>();
                    var animatorCount = Resources.FindObjectsOfTypeAll<Animator>().Length;
                    foreach (var phase in new[] { .07, .23, .47, .71, .93 })
                    foreach (var blend in new[] { .2f, .5f, .8f })
                    {
                        var weights = inputCount == 1 ? new[] { 1f } : inputCount == 2 ? new[] { 1 - blend, blend } : new[] { (1 - blend) * .6f, (1 - blend) * .4f, blend };
                        for (var b = 0; b < transforms.Length; b++)
                        { transforms[b].localPosition = rest[b].Position; transforms[b].localRotation = rest[b].Rotation; transforms[b].localScale = rest[b].Scale; }
                        var requests = em.GetBuffer<AnimationClipRequest>(entity); requests.Clear();
                        for (var i = 0; i < inputCount; i++)
                        {
                            inputs[i].SetTime(phase * clips[i].length); inputs[i].SetDone(false); mixer.SetInputWeight(i, weights[i]);
                            requests.Add(new AnimationClipRequest { Clip = new ClipId(i), Time = phase * clips[i].length, Weight = weights[i] });
                        }
                        graph.Evaluate(0);
                        blendSystem.Update(); retargetSystem.Update(); modelSystem.Update(); em.CompleteAllTrackedJobs();
                        Assert.That(em.GetComponentData<HumanoidBodyPose>(entity).Result, Is.EqualTo(HumanoidEvaluationResult.Success));
                        var local = em.GetBuffer<BoneLocalPose>(entity); var model = em.GetBuffer<BoneModelPose>(entity);
                        Assert.That(local.Length, Is.EqualTo(transforms.Length)); Assert.That(model.Length, Is.EqualTo(transforms.Length));
                        for (var b = 1; b < transforms.Length; b++)
                        {
                            maxPosition = math.max(maxPosition, Vector3.Distance(model[b].Matrix.c3, source.transform.InverseTransformPoint(transforms[b].position)));
                            maxAngle = math.max(maxAngle, Quaternion.Angle(local[b].Rotation, transforms[b].localRotation));
                        }
                    }
                    Assert.That(Resources.FindObjectsOfTypeAll<Animator>().Length, Is.EqualTo(animatorCount), "The ECS runtime must not instantiate Animators.");
                    var requestsToClear = em.GetBuffer<AnimationClipRequest>(entity); requestsToClear.Clear();
                    blendSystem.Update(); retargetSystem.Update(); em.CompleteAllTrackedJobs();
                    Assert.That(em.GetComponentData<HumanoidBodyPose>(entity).Result, Is.EqualTo(HumanoidEvaluationResult.NoRequests));
                    var restored = em.GetBuffer<BoneLocalPose>(entity);
                    for (var b = 0; b < restored.Length; b++) Assert.That(restored[b].Position, Is.EqualTo(rig.Value.RestLocalPoses[b].Position));
                }
                var report = "Actual ECS runtime: inputs=" + inputCount + " maxJointMeters=" + maxPosition.ToString("R") + " maxLocalDegrees=" + maxAngle.ToString("R");
                TestContext.WriteLine(report);
                Directory.CreateDirectory("Artifacts/Verification/AnimatorValidation");
                File.WriteAllText("Artifacts/Verification/AnimatorValidation/RuntimeHumanoid" + inputCount + ".txt", report);
                Assert.That(maxPosition, Is.LessThanOrEqualTo(.02f), report); Assert.That(maxAngle, Is.LessThanOrEqualTo(2f), report);
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                if (library.IsCreated) library.Dispose(); foreach (var clip in baked) if (clip.IsCreated) clip.Dispose();
                if (rig.IsCreated) rig.Dispose();
                if (source != null) UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(settings);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
