using System;
using System.Linq;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using AnimationSystemDOTS.Editor.Baking;
using AnimationSystemDOTS.Editor.Verification;

namespace AnimationSystemDOTS.Tests
{
    public class RuntimeOracleTests : ClipBakeFixture
    {
        [Test]
        public void DefaultSimulationGroup_ECSPoseMatchesNativeOracleAtSamplesAndBetweenThem()
        {
            clip.SetCurve("Bone", typeof(Transform), "m_LocalPosition.y", AnimationCurve.Constant(0, 1, 4));
            clip.SetCurve("Bone", typeof(Transform), "m_LocalPosition.z", AnimationCurve.Constant(0, 1, 5));
            clip.SetCurve("Bone", typeof(Transform), "m_LocalScale.x", AnimationCurve.Constant(0, 1, -1));
            clip.SetCurve("Bone", typeof(Transform), "m_LocalScale.y", AnimationCurve.Constant(0, 1, 2));
            clip.SetCurve("Bone", typeof(Transform), "m_LocalScale.z", AnimationCurve.Constant(0, 1, 1));
            for (var axis = 0; axis < 4; axis++)
                clip.SetCurve("Bone", typeof(Transform), "m_LocalRotation." + "xyzw"[axis], AnimationCurve.Constant(0, 1, axis == 3 ? 1 : 0));
            var times = new[] { 0, 0.017, 0.353, 0.5, 0.987, 1 };
            var oracle = UnityAnimationOracle.Capture(new OracleCaptureRequest { Prefab = source, Clip = clip, Times = times });
            var baked = ClipBakePipeline.Bake(Request());
            Assert.That(baked.Success, Is.True, string.Join("\n", baked.Diagnostics));
            var library = default(BlobAssetReference<AnimationLibraryBlob>);
            try
            {
                var result = LibraryBakePipeline.Build(rig, new[] { baked.Clip }); library = result.Library;
                Assert.That(result.Success, Is.True);
                using (var testWorld = new World("AnimationSystemDOTS Runtime Oracle"))
                {
                    var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default)
                        .Where(type => type.Assembly == typeof(ScriptedPlaybackSystem).Assembly
                            || type.Assembly.GetName().Name == "Unity.Entities" || type.Assembly.GetName().Name == "Unity.Transforms").ToArray();
                    DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(testWorld, systems);
                    var stepEntity = testWorld.EntityManager.CreateEntity(typeof(AnimationStepContext));
                    var entity = testWorld.EntityManager.CreateEntity(typeof(AnimationRig), typeof(AnimationClock), typeof(AnimationPlaybackMode), typeof(ClipPlaybackState));
                    testWorld.EntityManager.SetComponentData(entity, new AnimationRig { Rig = rig, Library = library });
                    testWorld.EntityManager.SetComponentData(entity, ClipPlaybackState.Initial);
                    testWorld.EntityManager.AddBuffer<ScriptedPlaybackCommand>(entity);
                    testWorld.EntityManager.AddBuffer<AnimationClipRequest>(entity);
                    testWorld.EntityManager.AddBuffer<SampledBonePose>(entity);
                    testWorld.EntityManager.AddBuffer<BoneLocalPose>(entity);
                    testWorld.EntityManager.AddBuffer<BoneModelPose>(entity);
                    float maximum = 0;
                    for (var frame = 0; frame < times.Length; frame++)
                    {
                        var command = frame == 0 ? ScriptedPlaybackCommand.Play(new ClipId(0), 1, times[frame]) : ScriptedPlaybackCommand.Seek(times[frame], (ulong)frame + 1);
                        testWorld.EntityManager.GetBuffer<ScriptedPlaybackCommand>(entity).Add(command);
                        testWorld.EntityManager.SetComponentData(stepEntity, new AnimationStepContext { Tick = (ulong)frame + 1, Mode = AnimationStepMode.Seek, Revision = (uint)frame });
                        testWorld.Update(); testWorld.EntityManager.CompleteAllTrackedJobs();
                        var model = testWorld.EntityManager.GetBuffer<BoneModelPose>(entity);
                        Assert.That(model.Length, Is.EqualTo(rig.Value.RestLocalPoses.Length), "Default group must schedule sampling, blending and model propagation.");
                        for (var bone = 0; bone < model.Length; bone++)
                        {
                            var path = rig.Value.Paths[bone].ToString();
                            var expected = oracle.Frames[frame].WorldMatrices[Array.IndexOf(oracle.BonePaths, path)];
                            var actual = model[bone].Matrix;
                            var matrix = new float4x4(new float4(actual.c0, 0), new float4(actual.c1, 0), new float4(actual.c2, 0), new float4(actual.c3, 1));
                            for (var row = 0; row < 4; row++) for (var column = 0; column < 4; column++)
                                maximum = math.max(maximum, math.abs(matrix[column][row] - expected[row, column]));
                        }
                    }
                    TestContext.WriteLine("Maximum ECS/oracle matrix element error: " + maximum.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    Assert.That(maximum, Is.LessThanOrEqualTo(settings.PositionTolerance));
                }
            }
            finally { if (library.IsCreated) library.Dispose(); if (baked.Clip.IsCreated) baked.Clip.Dispose(); }
        }
    }
}
