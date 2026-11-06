using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace AnimationSystemDOTS.Tests
{
    public class ControllerInterruptionRuntimeTests : RuntimeAnimationFixture
    {
        BlobAssetReference<AnimatorControllerBlob> definition, replacement;
        ControllerAdvanceSystem advance;
        AnimationSampleSystem sample;
        AnimationBlendSystem blend;

        BlobAssetReference<AnimatorControllerBlob> Graph()
        {
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<AnimatorControllerBlob>(); root.Version = AnimatorControllerBlob.CurrentVersion; root.LibraryKey = library.Value.ContentKey;
                var parameters = builder.Allocate(ref root.Parameters, 2);
                parameters[0] = new AnimationParameterDefinition { Name = "Go", Type = AnimationParameterType.Trigger };
                parameters[1] = new AnimationParameterDefinition { Name = "Other", Type = AnimationParameterType.Trigger };
                var states = builder.Allocate(ref root.States, 3);
                for (var i = 0; i < 3; i++) states[i] = new ControllerStateDefinition { Clip = new ClipId(i == 2 ? 0 : i), Duration = i == 1 ? 2 : 1, Speed = i == 2 ? 2 : 1, SpeedParameter = -1 };
                states[0].TransitionCount = 2;
                var edges = builder.Allocate(ref root.Transitions, 2); var conditions = builder.Allocate(ref root.Conditions, 2);
                for (var i = 0; i < 2; i++)
                {
                    edges[i] = new ControllerTransitionDefinition { Destination = i + 1, ConditionStart = i, ConditionCount = 1,
                        FixedDuration = 1, Duration = i == 0 ? .8f : .5f, CanTransitionToSelf = 1, Interruption = ControllerInterruptionSource.Source };
                    conditions[i] = new ControllerCondition { Parameter = i, Mode = ControllerConditionMode.If };
                }
                return builder.CreateBlobAssetReference<AnimatorControllerBlob>(Allocator.Persistent);
            }
        }
        void Configure()
        {
            definition = Graph(); var em = world.EntityManager;
            em.AddComponentData(rigEntity, new AnimationController { Definition = definition }); em.AddComponentData(rigEntity, new ControllerLayerState());
            em.SetComponentData(rigEntity, new AnimationPlaybackMode { Value = PlaybackDriver.Controller });
            em.AddBuffer<AnimationParameterValue>(rigEntity).Resize(2, NativeArrayOptions.ClearMemory);
            em.AddBuffer<SampledBonePose>(rigEntity); em.AddBuffer<BoneLocalPose>(rigEntity);
            advance = world.GetOrCreateSystemManaged<ControllerAdvanceSystem>(); sample = world.GetOrCreateSystemManaged<AnimationSampleSystem>(); blend = world.GetOrCreateSystemManaged<AnimationBlendSystem>();
        }
        void Trigger(int id)
        { var values = world.EntityManager.GetBuffer<AnimationParameterValue>(rigEntity); values[id] = new AnimationParameterValue { Int = 1 }; }
        void Tick(ulong tick, double dt, uint revision = 0, AnimationStepMode mode = AnimationStepMode.Normal)
        {
            world.EntityManager.SetComponentData(stepEntity, new AnimationStepContext { Tick = tick, DeltaTime = dt, Revision = revision, Mode = mode });
            advance.Update(); sample.Update(); blend.Update(); world.EntityManager.CompleteAllTrackedJobs();
        }
        [Test]
        public void InterruptedPose_RepeatedTickAndRestoredHistoryReplayAreStable()
        {
            Configure(); var em = world.EntityManager; Tick(1, .1); Trigger(0); Tick(2, .1); Tick(3, .15);
            var state = em.GetComponentData<ControllerLayerState>(rigEntity); var clock = em.GetComponentData<AnimationClock>(rigEntity);
            using (var layers = em.GetBuffer<ControllerLayerRuntime>(rigEntity).ToNativeArray(Allocator.Temp))
            using (var cache = em.GetBuffer<ControllerCachedRequest>(rigEntity).ToNativeArray(Allocator.Temp))
            using (var history = em.GetBuffer<ControllerBoneHistory>(rigEntity).ToNativeArray(Allocator.Temp))
            using (var parameters = em.GetBuffer<AnimationParameterValue>(rigEntity).ToNativeArray(Allocator.Temp))
            using (var poses = em.GetBuffer<BoneLocalPose>(rigEntity).ToNativeArray(Allocator.Temp))
            {
                Trigger(1); Tick(4, .1);
                var onset = em.GetBuffer<BoneLocalPose>(rigEntity)[1];
                var captured = em.GetBuffer<ControllerBoneHistory>(rigEntity)[1].Frozen;
                Tick(4, .9);
                Assert.That(em.GetBuffer<BoneLocalPose>(rigEntity)[1].Position, Is.EqualTo(onset.Position));
                Assert.That(em.GetBuffer<ControllerBoneHistory>(rigEntity)[1].Frozen.Position, Is.EqualTo(captured.Position));
                Tick(5, .1); var expected = em.GetBuffer<BoneLocalPose>(rigEntity)[1]; var expectedState = em.GetComponentData<ControllerLayerState>(rigEntity);
                Assert.That(expected.Position, Is.Not.EqualTo(onset.Position), "The destination must continue advancing after capture.");
                Tick(6, .3);
                em.SetComponentData(rigEntity, state); em.SetComponentData(rigEntity, clock);
                em.GetBuffer<ControllerLayerRuntime>(rigEntity).CopyFrom(layers); em.GetBuffer<ControllerCachedRequest>(rigEntity).CopyFrom(cache);
                em.GetBuffer<ControllerBoneHistory>(rigEntity).CopyFrom(history); em.GetBuffer<AnimationParameterValue>(rigEntity).CopyFrom(parameters);
                em.GetBuffer<BoneLocalPose>(rigEntity).CopyFrom(poses);
                Trigger(1); Tick(4, .1, 7, AnimationStepMode.Replay); Tick(5, .1, 7, AnimationStepMode.Replay);
                var actual = em.GetBuffer<BoneLocalPose>(rigEntity)[1]; var actualState = em.GetComponentData<ControllerLayerState>(rigEntity);
                Assert.That(actual.Position, Is.EqualTo(expected.Position)); Assert.That(actual.Rotation, Is.EqualTo(expected.Rotation)); Assert.That(actual.Scale, Is.EqualTo(expected.Scale));
                Assert.That(actualState.NormalizedTime, Is.EqualTo(expectedState.NormalizedTime)); Assert.That(actualState.NextNormalizedTime, Is.EqualTo(expectedState.NextNormalizedTime));
            }
        }
        [Test]
        public void DefinitionReplacement_InvalidatesPriorPoseCaptureOnTheSameTick()
        {
            Configure(); var em = world.EntityManager; Tick(1, .1); Trigger(0); Tick(2, .1); Tick(3, .15); Trigger(1); Tick(4, .1);
            Assert.That(em.GetBuffer<ControllerBoneHistory>(rigEntity)[1].FrozenSequence, Is.GreaterThan(0));
            var clock = em.GetComponentData<AnimationClock>(rigEntity); replacement = Graph(); em.SetComponentData(rigEntity, new AnimationController { Definition = replacement }); Tick(4, .9);
            var history = em.GetBuffer<ControllerBoneHistory>(rigEntity)[1];
            Assert.That(history.Definition, Is.EqualTo(replacement)); Assert.That(history.FrozenSequence, Is.Zero);
            Assert.That(em.GetBuffer<BoneLocalPose>(rigEntity)[1].Position.x, Is.Zero);
            Assert.That(em.GetComponentData<AnimationClock>(rigEntity).Time, Is.EqualTo(clock.Time));
        }
        [TearDown]
        public void DisposeGraphs()
        {
            if (replacement.IsCreated) replacement.Dispose(); if (definition.IsCreated) definition.Dispose();
        }
    }
}
