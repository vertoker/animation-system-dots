using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace AnimationSystemDOTS.Tests
{
    public class PoseOwnershipTests : RuntimeAnimationFixture
    {
        [MaximumChunkCapacity(1)]
        public struct SingleRigChunk : IComponentData { }
        struct PendingReader : IJob
        {
            public BlobAssetReference<AnimationLibraryBlob> Library;
            public NativeArray<float> Result;
            public void Execute()
            {
                System.Threading.Thread.Sleep(100);
                Result[0] = ClipSampler.SampleBone(ref Library.Value.Clips[0], 1, 0.5).Position.x;
            }
        }
        [Test]
        public void WorldOwner_TransfersRuntimeBlobsAndWaitsForExternalReadersBeforeTeardown()
        {
            var owner = world.GetOrCreateSystemManaged<RuntimeAnimationBlobOwnerSystem>();
            var borrowed = library;
            owner.Adopt(ref rig, ref library);
            Assert.That(rig.IsCreated || library.IsCreated, Is.False, "Adoption transfers allocation ownership and clears caller handles.");
            using (var result = new NativeArray<float>(1, Allocator.Persistent))
            {
                var reader = new PendingReader { Library = borrowed, Result = result }.Schedule();
                owner.RegisterReader(reader);
                var wasPending = !reader.IsCompleted;
                world.Dispose(); world = null;
                Assert.That(wasPending, Is.True, "Fixture must hand teardown an unfinished reader.");
                Assert.That(reader.IsCompleted, Is.True);
                Assert.That(result[0], Is.EqualTo(5).Within(1e-5));
            }
        }
        static void Buffers(World target, Entity entity)
        {
            target.EntityManager.AddBuffer<SampledBonePose>(entity); target.EntityManager.AddBuffer<BoneLocalPose>(entity); target.EntityManager.AddBuffer<BoneModelPose>(entity);
        }
        static void Evaluate(World target)
        {
            target.GetOrCreateSystemManaged<AnimationSampleSystem>().Update(); target.GetOrCreateSystemManaged<AnimationBlendSystem>().Update(); target.GetOrCreateSystemManaged<ModelPoseSystem>().Update();
        }
        [Test]
        public void EntityPrefab_InstantiatesIndependentBuffersAndDoesNotEvaluateThePrefab()
        {
            Buffers(world, rigEntity); world.EntityManager.AddComponent<Prefab>(rigEntity);
            var first = world.EntityManager.Instantiate(rigEntity); var second = world.EntityManager.Instantiate(rigEntity);
            world.EntityManager.GetBuffer<ScriptedPlaybackCommand>(first).Add(ScriptedPlaybackCommand.Play(new ClipId(0), 1, 0.2));
            world.EntityManager.GetBuffer<ScriptedPlaybackCommand>(second).Add(ScriptedPlaybackCommand.Play(new ClipId(0), 1, 0.8));
            Step(1, 0); Evaluate(world); world.EntityManager.CompleteAllTrackedJobs();
            Assert.That(world.EntityManager.HasComponent<Prefab>(first), Is.False);
            Assert.That(world.EntityManager.GetComponentData<ClipPlaybackState>(rigEntity).Clip.Value, Is.EqualTo(-1));
            Assert.That(world.EntityManager.GetBuffer<BoneLocalPose>(first)[1].Position.x, Is.EqualTo(2).Within(1e-5));
            Assert.That(world.EntityManager.GetBuffer<BoneLocalPose>(second)[1].Position.x, Is.EqualTo(8).Within(1e-5));
            world.EntityManager.DestroyEntity(first);
            Evaluate(world); world.EntityManager.CompleteAllTrackedJobs();
            Assert.That(world.EntityManager.GetBuffer<BoneLocalPose>(second)[1].Position.x, Is.EqualTo(8).Within(1e-5));
        }

        [Test]
        public void TwoWorlds_SharingImmutableBlobsDoNotSharePlaybackOrPoseState()
        {
            using (var other = new World("AnimationSystemDOTS Other World"))
            {
                var otherStep = other.EntityManager.CreateEntity(typeof(AnimationStepContext));
                var otherRig = CreateRig(other); Buffers(other, otherRig); Buffers(world, rigEntity);
                other.EntityManager.SetComponentData(otherStep, new AnimationStepContext { Tick = 1, DeltaTime = 0.75 });
                other.EntityManager.GetBuffer<ScriptedPlaybackCommand>(otherRig).Add(ScriptedPlaybackCommand.Play(new ClipId(0), 1));
                Command(ScriptedPlaybackCommand.Play(new ClipId(0), 1)); Step(1, 0.25);
                other.GetOrCreateSystemManaged<ScriptedPlaybackSystem>().Update(); Evaluate(other); Evaluate(world);
                other.EntityManager.CompleteAllTrackedJobs(); world.EntityManager.CompleteAllTrackedJobs();
                Assert.That(world.EntityManager.GetBuffer<BoneLocalPose>(rigEntity)[1].Position.x, Is.EqualTo(2.5f).Within(1e-5));
                Assert.That(other.EntityManager.GetBuffer<BoneLocalPose>(otherRig)[1].Position.x, Is.EqualTo(7.5f).Within(1e-5));
            }
        }
        [Test]
        public void JobScheduling_RecomputesTheSameStateAndPosesAtDifferentWorkerCountsAndChunkSizes()
        {
            var previous = JobsUtility.JobWorkerCount;
            var entities = new Entity[64]; entities[0] = rigEntity;
            try
            {
                for (var i = 0; i < entities.Length; i++)
                {
                    if (i != 0) entities[i] = CreateRig(world);
                    Buffers(world, entities[i]);
                    PrepareCommands(entities[i], i);
                }
                JobsUtility.JobWorkerCount = 1; Step(1, 0.25); Evaluate(world); world.EntityManager.CompleteAllTrackedJobs();
                var poses = new float[entities.Length]; var states = new ClipPlaybackState[entities.Length];
                for (var i = 0; i < entities.Length; i++)
                {
                    poses[i] = world.EntityManager.GetBuffer<BoneLocalPose>(entities[i])[1].Position.x;
                    states[i] = world.EntityManager.GetComponentData<ClipPlaybackState>(entities[i]);
                }
                using (var query = world.EntityManager.CreateEntityQuery(typeof(AnimationRig)))
                {
                    var previousChunks = query.CalculateChunkCount();
                    for (var i = 0; i < entities.Length; i++)
                    {
                        world.EntityManager.AddComponent<SingleRigChunk>(entities[i]);
                        world.EntityManager.SetComponentData(entities[i], ClipPlaybackState.Initial);
                        world.EntityManager.SetComponentData(entities[i], new AnimationClock());
                        PrepareCommands(entities[i], i);
                    }
                    Assert.That(query.CalculateChunkCount(), Is.GreaterThan(previousChunks), "Second pass must use a different per-rig job partition.");
                    Assert.That(query.CalculateChunkCount(), Is.EqualTo(entities.Length));
                }
                JobsUtility.JobWorkerCount = System.Math.Min(4, JobsUtility.JobWorkerMaximumCount);
                Step(1, 0.25); Evaluate(world); world.EntityManager.CompleteAllTrackedJobs();
                for (var i = 0; i < entities.Length; i++)
                {
                    Assert.That(world.EntityManager.GetBuffer<BoneLocalPose>(entities[i])[1].Position.x, Is.EqualTo(poses[i]).Within(1e-6));
                    var actual = world.EntityManager.GetComponentData<ClipPlaybackState>(entities[i]);
                    Assert.That(actual.Time, Is.EqualTo(states[i].Time));
                    Assert.That(actual.Clip.Value, Is.EqualTo(states[i].Clip.Value));
                    Assert.That(actual.FadeSource.Value, Is.EqualTo(states[i].FadeSource.Value));
                    Assert.That(actual.FadeSourceTime, Is.EqualTo(states[i].FadeSourceTime));
                    Assert.That(actual.FadeProgress, Is.EqualTo(states[i].FadeProgress));
                    Assert.That(actual.InstanceSequence, Is.EqualTo(states[i].InstanceSequence));
                    Assert.That(actual.LastCommandSequence, Is.EqualTo(states[i].LastCommandSequence));
                }
            }
            finally { world.EntityManager.CompleteAllTrackedJobs(); JobsUtility.JobWorkerCount = previous; }
        }
        void PrepareCommands(Entity entity, int index)
        {
            var commands = world.EntityManager.GetBuffer<ScriptedPlaybackCommand>(entity);
            commands.Add(ScriptedPlaybackCommand.Play(new ClipId(0), 1, 0.1 + index * 0.001));
            if (index % 2 != 0) commands.Add(ScriptedPlaybackCommand.CrossFade(new ClipId(1), 1, 2, 0.05 + index * 0.001));
        }
    }
}
