using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace AnimationSystemDOTS.Tests
{
    public class ControllerMotionTests : RuntimeAnimationFixture
    {
        BlobAssetReference<AnimatorControllerBlob> graph;
        ControllerAdvanceSystem system;

        void Configure(bool direct = false)
        {
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<AnimatorControllerBlob>(); root.Version = AnimatorControllerBlob.CurrentVersion;
                root.LibraryKey = library.Value.ContentKey;
                var parameters = builder.Allocate(ref root.Parameters, 2);
                parameters[0].Type = parameters[1].Type = AnimationParameterType.Float;
                var states = builder.Allocate(ref root.States, 1);
                states[0] = new ControllerStateDefinition { Motion = 2, Speed = 1, SpeedParameter = -1 };
                var motions = builder.Allocate(ref root.Motions, 3);
                motions[0] = new ControllerMotionDefinition { Clip = new ClipId(0) };
                motions[1] = new ControllerMotionDefinition { Clip = new ClipId(1) };
                motions[2] = new ControllerMotionDefinition { Type = direct ? ControllerMotionType.Direct : ControllerMotionType.Simple1D,
                    ParameterX = 0, ChildCount = 2, NormalizeDirect = 1 };
                var children = builder.Allocate(ref root.MotionChildren, 2);
                children[0] = new ControllerMotionChild { Motion = 0, TimeScale = 1, DirectParameter = 0 };
                children[1] = new ControllerMotionChild { Motion = 1, TimeScale = 1, Threshold = 1, DirectParameter = 1 };
                graph = builder.CreateBlobAssetReference<AnimatorControllerBlob>(Allocator.Persistent);
            }
            var em = world.EntityManager;
            em.SetComponentData(rigEntity, new AnimationPlaybackMode { Value = PlaybackDriver.Controller });
            em.AddComponentData(rigEntity, new AnimationController { Definition = graph }); em.AddComponentData(rigEntity, new ControllerLayerState());
            var values = em.AddBuffer<AnimationParameterValue>(rigEntity); values.Add(new AnimationParameterValue { Float = .5f }); values.Add(new AnimationParameterValue { Float = .5f });
            system = world.GetOrCreateSystemManaged<ControllerAdvanceSystem>();
        }

        void Tick(ulong tick, double dt, uint revision = 0, AnimationStepMode mode = AnimationStepMode.Normal)
        {
            world.EntityManager.SetComponentData(stepEntity, new AnimationStepContext { Tick = tick, DeltaTime = dt, Revision = revision, Mode = mode });
            system.Update(); world.EntityManager.CompleteAllTrackedJobs();
        }

        [Test]
        public void MotionClock_RepeatSeekReplayAndParameterChangesPreserveTickContract()
        {
            Configure(); var em = world.EntityManager;
            Tick(1, .2); var first = em.GetComponentData<ControllerLayerState>(rigEntity);
            Assert.That(first.NormalizedTime, Is.EqualTo(.2 / 1.5).Within(1e-8));
            var requests = em.GetBuffer<AnimationClipRequest>(rigEntity);
            Assert.That(requests.Length, Is.EqualTo(2)); Assert.That(requests[0].Weight, Is.EqualTo(.5f));
            var values = em.GetBuffer<AnimationParameterValue>(rigEntity); values[0] = new AnimationParameterValue { Float = 1 };
            Tick(1, 10); requests = em.GetBuffer<AnimationClipRequest>(rigEntity);
            Assert.That(requests.Length, Is.EqualTo(2), "Repeated tick neither changes weights nor advances.");
            Assert.That(em.GetComponentData<ControllerLayerState>(rigEntity).NormalizedTime, Is.EqualTo(first.NormalizedTime));
            Tick(1, 10, 2, AnimationStepMode.Seek);
            var seek = em.GetComponentData<ControllerLayerState>(rigEntity);
            Assert.That(seek.NormalizedTime, Is.EqualTo(first.NormalizedTime));
            requests = em.GetBuffer<AnimationClipRequest>(rigEntity);
            Assert.That(requests.Length, Is.EqualTo(1)); Assert.That(requests[0].Clip.Value, Is.EqualTo(1));
            Tick(2, .2); var advanced = em.GetComponentData<ControllerLayerState>(rigEntity);
            Assert.That(advanced.NormalizedTime, Is.EqualTo(first.NormalizedTime + .1).Within(1e-8));
            em.SetComponentData(rigEntity, seek); Tick(2, .2, 3, AnimationStepMode.Replay);
            Assert.That(em.GetComponentData<ControllerLayerState>(rigEntity).NormalizedTime, Is.EqualTo(advanced.NormalizedTime));
            Assert.That(em.GetBuffer<ControllerMotionValue>(rigEntity).Length, Is.EqualTo(3));
        }

        [Test]
        public void DirectZeroWeights_RetainPoseUntilScriptedDriverTakesOwnership()
        {
            Configure(true); var em = world.EntityManager;
            em.AddBuffer<SampledBonePose>(rigEntity); em.AddBuffer<BoneLocalPose>(rigEntity);
            var sample = world.GetOrCreateSystemManaged<AnimationSampleSystem>(); var blend = world.GetOrCreateSystemManaged<AnimationBlendSystem>();
            Tick(1, .2); sample.Update(); blend.Update(); em.CompleteAllTrackedJobs();
            var pose = em.GetBuffer<BoneLocalPose>(rigEntity)[1];
            var values = em.GetBuffer<AnimationParameterValue>(rigEntity); values[0] = values[1] = default;
            Tick(2, .2); sample.Update(); blend.Update(); em.CompleteAllTrackedJobs();
            Assert.That(em.GetBuffer<AnimationClipRequest>(rigEntity).Length, Is.Zero);
            Assert.That(em.GetComponentData<ControllerLayerState>(rigEntity).RetainPose, Is.EqualTo(1));
            Assert.That(em.GetBuffer<BoneLocalPose>(rigEntity)[1].Position, Is.EqualTo(pose.Position));
            em.SetComponentData(rigEntity, new AnimationPlaybackMode { Value = PlaybackDriver.Scripted });
            blend.Update(); em.CompleteAllTrackedJobs();
            Assert.That(em.GetBuffer<BoneLocalPose>(rigEntity)[1].Position, Is.EqualTo(rig.Value.RestLocalPoses[1].Position));
        }

        [TearDown]
        public void DisposeGraph() { if (graph.IsCreated) graph.Dispose(); }
    }
}
