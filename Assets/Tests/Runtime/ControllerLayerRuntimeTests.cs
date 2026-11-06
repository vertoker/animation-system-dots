using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace AnimationSystemDOTS.Tests
{
    public class ControllerLayerRuntimeTests : RuntimeAnimationFixture
    {
        readonly List<BlobAssetReference<AnimatorControllerBlob>> graphs = new List<BlobAssetReference<AnimatorControllerBlob>>();
        ControllerAdvanceSystem system;
        BlobAssetReference<AnimatorControllerBlob> Graph(bool replacement = false, bool directUpper = false)
        {
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<AnimatorControllerBlob>(); root.Version = AnimatorControllerBlob.CurrentVersion; root.LibraryKey = library.Value.ContentKey;
                var parameters = builder.Allocate(ref root.Parameters, 2);
                parameters[0] = new AnimationParameterDefinition { Name = replacement ? "Flag" : "X", Type = replacement ? AnimationParameterType.Bool : AnimationParameterType.Float,
                    DefaultFloat = .2f, DefaultInt = replacement ? 1 : 0 };
                parameters[1] = new AnimationParameterDefinition { Name = replacement ? "Value" : "Go", Type = replacement ? AnimationParameterType.Float : AnimationParameterType.Trigger, DefaultFloat = .75f };
                var states = builder.Allocate(ref root.States, 2); var layers = builder.Allocate(ref root.Layers, 2);
                for (var i = 0; i < 2; i++)
                {
                    var clip = replacement ? 1 - i : i;
                    states[i] = new ControllerStateDefinition { Clip = new ClipId(clip), Duration = clip + 1, Speed = 1, SpeedParameter = -1 };
                    layers[i] = new ControllerLayerDefinition { Name = i == 0 ? "Base" : "Upper", StateStart = i, StateCount = 1, DefaultState = i,
                        SyncedLayer = -1, Mask = -1, DefaultWeight = i == 0 ? 1 : replacement ? .8f : .35f };
                }
                if (directUpper)
                {
                    states[0].Motion = 0; states[1].Motion = 2;
                    var motions = builder.Allocate(ref root.Motions, 3);
                    motions[0] = new ControllerMotionDefinition { Clip = new ClipId(0) }; motions[1] = new ControllerMotionDefinition { Clip = new ClipId(1) };
                    motions[2] = new ControllerMotionDefinition { Type = ControllerMotionType.Direct, ChildCount = 1, NormalizeDirect = 1 };
                    var children = builder.Allocate(ref root.MotionChildren, 1); children[0] = new ControllerMotionChild { Motion = 1, DirectParameter = 0, TimeScale = 1 };
                }
                var graph = builder.CreateBlobAssetReference<AnimatorControllerBlob>(Allocator.Persistent); graphs.Add(graph); return graph;
            }
        }
        void Configure(bool directUpper = false)
        {
            var em = world.EntityManager; var graph = Graph(false, directUpper);
            em.SetComponentData(rigEntity, new AnimationPlaybackMode { Value = PlaybackDriver.Controller });
            em.AddComponentData(rigEntity, new AnimationController { Definition = graph }); em.AddComponentData(rigEntity, new ControllerLayerState());
            var values = em.AddBuffer<AnimationParameterValue>(rigEntity); values.Add(new AnimationParameterValue { Float = .9f }); values.Add(new AnimationParameterValue { Int = 1 });
            system = world.GetOrCreateSystemManaged<ControllerAdvanceSystem>();
        }
        void Tick(ulong tick, double dt, uint revision = 0, AnimationStepMode mode = AnimationStepMode.Normal)
        {
            world.EntityManager.SetComponentData(stepEntity, new AnimationStepContext { Tick = tick, DeltaTime = dt, Revision = revision, Mode = mode });
            system.Update(); world.EntityManager.CompleteAllTrackedJobs();
        }

        [Test]
        public void LayerAPI_IndependentClocksWeightsRepeatedTicksAndSeek()
        {
            Configure(); var em = world.EntityManager; Tick(1, .2);
            var controller = em.GetComponentData<AnimationController>(rigEntity); var layers = em.GetBuffer<ControllerLayerRuntime>(rigEntity);
            Assert.That(AnimationApi.FindLayer(controller, "Upper"), Is.EqualTo(1)); Assert.That(AnimationApi.FindLayer(controller, "Missing"), Is.EqualTo(-1));
            Assert.That(ControllerStateQuery.TryGet(layers, 1, out var upper), Is.True); Assert.That(upper.NormalizedTime, Is.EqualTo(.1).Within(1e-9));
            Assert.That(AnimationApi.SetLayerWeight(controller, layers, 1, float.NaN), Is.EqualTo(AnimationLayerResult.InvalidWeight));
            Assert.That(AnimationApi.SetLayerWeight(controller, layers, 8, .5f), Is.EqualTo(AnimationLayerResult.InvalidLayer));
            Assert.That(layers[1].Weight, Is.EqualTo(.35f));
            Assert.That(AnimationApi.SetLayerWeight(controller, layers, 1, .7f), Is.EqualTo(AnimationLayerResult.Success));
            Tick(1, 1); Assert.That(em.GetBuffer<AnimationClipRequest>(rigEntity)[1].Weight, Is.EqualTo(.35f));
            Tick(1, 10, 2, AnimationStepMode.Seek);
            Assert.That(em.GetBuffer<AnimationClipRequest>(rigEntity)[1].Weight, Is.EqualTo(.7f));
            Assert.That(em.GetBuffer<ControllerLayerRuntime>(rigEntity)[1].State.NormalizedTime, Is.EqualTo(.1).Within(1e-9));
            Tick(2, .2); Assert.That(em.GetBuffer<ControllerLayerRuntime>(rigEntity)[1].State.NormalizedTime, Is.EqualTo(.2).Within(1e-9));
            Assert.That(em.GetComponentData<ControllerLayerState>(rigEntity).NormalizedTime, Is.EqualTo(.4).Within(1e-9));
        }

        [Test]
        public void DefinitionReplacement_SameParameterCountResetsTypesStatesAndLayerWeights()
        {
            Configure(); var em = world.EntityManager; Tick(1, .2);
            Assert.That(em.GetBuffer<AnimationParameterValue>(rigEntity)[0].Float, Is.EqualTo(.9f), "Setup input survives first initialization.");
            var replacement = Graph(true); em.SetComponentData(rigEntity, new AnimationController { Definition = replacement }); Tick(1, .2);
            var values = em.GetBuffer<AnimationParameterValue>(rigEntity); var state = em.GetComponentData<ControllerLayerState>(rigEntity);
            Assert.That(values[0].Int, Is.EqualTo(1)); Assert.That(values[1].Float, Is.EqualTo(.75f));
            Assert.That(state.Definition, Is.EqualTo(replacement)); Assert.That(state.NormalizedTime, Is.Zero); Assert.That(state.Result, Is.EqualTo(ControllerEvaluationResult.Success));
            Assert.That(em.GetComponentData<AnimationClock>(rigEntity).Time, Is.EqualTo(.2), "Rebinding on an evaluated tick must not advance the clock twice.");
            Assert.That(em.GetBuffer<ControllerLayerRuntime>(rigEntity)[1].Weight, Is.EqualTo(.8f));
            Assert.That(em.GetBuffer<AnimationClipRequest>(rigEntity)[0].Clip.Value, Is.EqualTo(1));
            Assert.That(em.GetBuffer<ControllerCachedRequest>(rigEntity)[0].Request.Time, Is.Zero);
        }

        [Test]
        public void Replay_RestoredLayerAndCachedMotionSnapshotReproducesFrozenUpperPose()
        {
            Configure(true); var em = world.EntityManager;
            em.AddBuffer<SampledBonePose>(rigEntity); em.AddBuffer<BoneLocalPose>(rigEntity);
            var sample = world.GetOrCreateSystemManaged<AnimationSampleSystem>(); var blend = world.GetOrCreateSystemManaged<AnimationBlendSystem>();
            Tick(1, .2); sample.Update(); blend.Update(); em.CompleteAllTrackedJobs();
            var state = em.GetComponentData<ControllerLayerState>(rigEntity); var clock = em.GetComponentData<AnimationClock>(rigEntity);
            using (var layerSnapshot = em.GetBuffer<ControllerLayerRuntime>(rigEntity).ToNativeArray(Allocator.Temp))
            using (var cacheSnapshot = em.GetBuffer<ControllerCachedRequest>(rigEntity).ToNativeArray(Allocator.Temp))
            using (var poseHistorySnapshot = em.GetBuffer<ControllerBoneHistory>(rigEntity).ToNativeArray(Allocator.Temp))
            using (var parameterSnapshot = em.GetBuffer<AnimationParameterValue>(rigEntity).ToNativeArray(Allocator.Temp))
            {
                var values = em.GetBuffer<AnimationParameterValue>(rigEntity); values[0] = default;
                Tick(2, .2); sample.Update(); blend.Update(); em.CompleteAllTrackedJobs();
                var expected = em.GetBuffer<BoneLocalPose>(rigEntity)[1];
                values = em.GetBuffer<AnimationParameterValue>(rigEntity); values[0] = new AnimationParameterValue { Float = 1 };
                Tick(3, .2); sample.Update(); blend.Update(); em.CompleteAllTrackedJobs();
                em.SetComponentData(rigEntity, state); em.SetComponentData(rigEntity, clock);
                em.GetBuffer<ControllerLayerRuntime>(rigEntity).CopyFrom(layerSnapshot);
                em.GetBuffer<ControllerCachedRequest>(rigEntity).CopyFrom(cacheSnapshot);
                em.GetBuffer<ControllerBoneHistory>(rigEntity).CopyFrom(poseHistorySnapshot);
                em.GetBuffer<AnimationParameterValue>(rigEntity).CopyFrom(parameterSnapshot);
                values = em.GetBuffer<AnimationParameterValue>(rigEntity); values[0] = default;
                Tick(2, .2, 3, AnimationStepMode.Replay); sample.Update(); blend.Update(); em.CompleteAllTrackedJobs();
                var actual = em.GetBuffer<BoneLocalPose>(rigEntity)[1];
                Assert.That(actual.Position, Is.EqualTo(expected.Position)); Assert.That(actual.Rotation, Is.EqualTo(expected.Rotation)); Assert.That(actual.Scale, Is.EqualTo(expected.Scale));
                var upper = em.GetBuffer<AnimationClipRequest>(rigEntity)[1];
                Assert.That(upper.Time, Is.EqualTo(.2).Within(1e-9)); Assert.That(upper.PreviousTime, Is.EqualTo(upper.Time), "Held motion must not replay an interval.");
            }
        }

        [Test]
        public void CorruptLayerWeight_FailsWithoutAdvancingThenRecoversThroughValidatedSetter()
        {
            Configure(); var em = world.EntityManager; Tick(1, .2);
            var layers = em.GetBuffer<ControllerLayerRuntime>(rigEntity); var item = layers[1]; item.Weight = float.PositiveInfinity; layers[1] = item;
            Tick(2, .3); Assert.That(em.GetComponentData<ControllerLayerState>(rigEntity).Result, Is.EqualTo(ControllerEvaluationResult.InvalidParameters));
            Assert.That(em.GetBuffer<AnimationClipRequest>(rigEntity).Length, Is.Zero);
            Assert.That(em.GetComponentData<AnimationClock>(rigEntity).Time, Is.EqualTo(.2));
            var controller = em.GetComponentData<AnimationController>(rigEntity);
            Assert.That(AnimationApi.SetLayerWeight(controller, em.GetBuffer<ControllerLayerRuntime>(rigEntity), 1, .5f), Is.EqualTo(AnimationLayerResult.Success));
            Tick(3, .1); Assert.That(em.GetComponentData<ControllerLayerState>(rigEntity).Result, Is.EqualTo(ControllerEvaluationResult.Success));
            Assert.That(em.GetComponentData<AnimationClock>(rigEntity).Time, Is.EqualTo(.3).Within(1e-9));
        }

        [TearDown]
        public void DisposeGraphs() { foreach (var graph in graphs) if (graph.IsCreated) graph.Dispose(); graphs.Clear(); }
    }
}
