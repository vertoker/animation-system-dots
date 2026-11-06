using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace AnimationSystemDOTS.Tests
{
    public abstract class RuntimeAnimationFixture
    {
        protected World world;
        protected Entity rigEntity;
        protected Entity stepEntity;
        protected BlobAssetReference<RigDefinitionBlob> rig;
        protected BlobAssetReference<AnimationLibraryBlob> library;
        protected ScriptedPlaybackSystem playback;

        [SetUp]
        public void SetUp()
        {
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<RigDefinitionBlob>();
                root.Version = AnimationBakeVersion.Schema; root.ContentKey = new FixedString128Bytes("testRig");
                var parents = builder.Allocate(ref root.Parents, 2); parents[0] = -1; parents[1] = 0;
                var rest = builder.Allocate(ref root.RestLocalPoses, 2); rest[0] = rest[1] = BoneLocalPose.Identity;
                rest[1] = Pose(1);
                rig = builder.CreateBlobAssetReference<RigDefinitionBlob>(Allocator.Persistent);
            }
            var clipBuilder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref var root = ref clipBuilder.ConstructRoot<AnimationLibraryBlob>();
                root.Version = AnimationBakeVersion.Schema; root.RigKey = rig.Value.ContentKey;
                var clips = clipBuilder.Allocate(ref root.Clips, 3);
                BuildClip(ref clipBuilder, ref clips[0], 0, 1, true, 0, 10);
                BuildClip(ref clipBuilder, ref clips[1], 1, 2, false, 20, 40);
                BuildClip(ref clipBuilder, ref clips[2], 2, 0, false, 7, 7);
                library = clipBuilder.CreateBlobAssetReference<AnimationLibraryBlob>(Allocator.Persistent);
            }
            finally { clipBuilder.Dispose(); }
            world = new World("AnimationSystemDOTS Runtime Test");
            stepEntity = world.EntityManager.CreateEntity(typeof(AnimationStepContext));
            rigEntity = CreateRig(world);
            playback = world.GetOrCreateSystemManaged<ScriptedPlaybackSystem>();
        }

        protected Entity CreateRig(World target)
        {
            var entity = target.EntityManager.CreateEntity(typeof(AnimationRig), typeof(AnimationClock), typeof(AnimationPlaybackMode), typeof(ClipPlaybackState));
            target.EntityManager.SetComponentData(entity, new AnimationRig { Rig = rig, Library = library });
            target.EntityManager.SetComponentData(entity, ClipPlaybackState.Initial);
            target.EntityManager.AddBuffer<ScriptedPlaybackCommand>(entity);
            target.EntityManager.AddBuffer<AnimationClipRequest>(entity);
            return entity;
        }
        static void BuildClip(ref BlobBuilder builder, ref AnimationClipBlob clip, int id, double duration, bool loop, float first, float last)
        {
            clip.Version = AnimationBakeVersion.Schema; clip.ContentKey = new FixedString128Bytes("clip" + id); clip.RigKey = new FixedString128Bytes("testRig");
            clip.Duration = duration; clip.SampleStep = duration; clip.SampleCount = duration > 0 ? 2 : 1; clip.IsLooping = loop ? (byte)1 : (byte)0;
            var reference = builder.Allocate(ref clip.ReferencePose, 2); reference[0] = reference[1] = BoneLocalPose.Identity;
            var tracks = builder.Allocate(ref clip.Tracks, 2);
            var root = builder.Allocate(ref tracks[0].Samples, 1); root[0] = BoneLocalPose.Identity;
            var bone = builder.Allocate(ref tracks[1].Samples, clip.SampleCount); bone[0] = Pose(first);
            if (bone.Length > 1) bone[1] = Pose(last);
        }
        protected static BoneLocalPose Pose(float x) => new BoneLocalPose { Position = new float3(x, 0, 0), Rotation = quaternion.identity, Scale = new float3(1) };
        protected void Command(ScriptedPlaybackCommand command) => world.EntityManager.GetBuffer<ScriptedPlaybackCommand>(rigEntity).Add(command);
        protected ClipPlaybackState State => world.EntityManager.GetComponentData<ClipPlaybackState>(rigEntity);
        protected void Step(ulong tick, double dt, uint revision = 0, AnimationStepMode mode = AnimationStepMode.Normal)
        {
            world.EntityManager.SetComponentData(stepEntity, new AnimationStepContext { Tick = tick, DeltaTime = dt, Revision = revision, Mode = mode });
            playback.Update(); world.EntityManager.CompleteAllTrackedJobs();
        }
        [TearDown]
        public void TearDown()
        {
            if (world != null && world.IsCreated) world.Dispose();
            if (library.IsCreated) library.Dispose();
            if (rig.IsCreated) rig.Dispose();
        }
    }
}
