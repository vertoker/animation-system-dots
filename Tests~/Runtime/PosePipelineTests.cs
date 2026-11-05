using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace AnimationSystemDOTS.Tests
{
    public class PosePipelineTests : RuntimeAnimationFixture
    {
        void AddPoseBuffers(Entity entity)
        {
            world.EntityManager.AddBuffer<SampledBonePose>(entity);
            world.EntityManager.AddBuffer<BoneLocalPose>(entity);
            world.EntityManager.AddBuffer<BoneModelPose>(entity);
        }
        void Evaluate()
        {
            world.GetOrCreateSystemManaged<AnimationSampleSystem>().Update();
            world.GetOrCreateSystemManaged<AnimationBlendSystem>().Update();
            world.GetOrCreateSystemManaged<ModelPoseSystem>().Update();
            world.EntityManager.CompleteAllTrackedJobs();
        }
        void Request(int clip, double time, float weight, int layer = 0, PoseBlendMode mode = PoseBlendMode.Override,
            BlobAssetReference<BoneMaskBlob> mask = default)
            => world.EntityManager.GetBuffer<AnimationClipRequest>(rigEntity).Add(new AnimationClipRequest
                { Clip = new ClipId(clip), Time = time, Weight = weight, Layer = layer, BlendMode = mode, Mask = mask });
        float X(Entity entity) => world.EntityManager.GetBuffer<BoneLocalPose>(entity)[1].Position.x;

        [Test]
        public void Pipeline_SamplesAndBlendsOverrideRequestsWithRestRemainder()
        {
            AddPoseBuffers(rigEntity); Request(0, 0.2, 0.25f); Request(1, 0.5, 0.75f); Evaluate();
            Assert.That(X(rigEntity), Is.EqualTo(19.25f).Within(1e-5));
            Assert.That(world.EntityManager.GetBuffer<BoneModelPose>(rigEntity)[1].Matrix.c3.x, Is.EqualTo(19.25f).Within(1e-5));
            world.EntityManager.GetBuffer<AnimationClipRequest>(rigEntity).Clear(); Request(0, 0.2, 0.25f); Evaluate();
            Assert.That(X(rigEntity), Is.EqualTo(1.25f).Within(1e-5));
        }
        [Test]
        public void Pipeline_ZeroWeightsAndInvalidRequestsReturnRestPose()
        {
            AddPoseBuffers(rigEntity); Request(0, 0.5, 0); Request(-1, 0, 1); Request(1, double.NaN, 1); Evaluate();
            Assert.That(X(rigEntity), Is.EqualTo(1));
            world.EntityManager.GetBuffer<AnimationClipRequest>(rigEntity).Clear(); Evaluate(); Assert.That(X(rigEntity), Is.EqualTo(1));
        }
        [Test]
        public void Pipeline_AppliesRigBoundFractionalMaskAndRejectsAnotherRigsMask()
        {
            AddPoseBuffers(rigEntity);
            foreach (var rigKey in new[] { "testRig", "anotherRig" })
            {
                var mask = default(BlobAssetReference<BoneMaskBlob>);
                using (var builder = new BlobBuilder(Allocator.Temp))
                {
                    ref var root = ref builder.ConstructRoot<BoneMaskBlob>(); root.Version = AnimationBakeVersion.Schema; root.RigKey = new FixedString128Bytes(rigKey);
                    var weights = builder.Allocate(ref root.Weights, 2); weights[0] = 1; weights[1] = 0.5f;
                    mask = builder.CreateBlobAssetReference<BoneMaskBlob>(Allocator.Persistent);
                }
                try
                {
                    world.EntityManager.GetBuffer<AnimationClipRequest>(rigEntity).Clear(); Request(0, 0.2, 0.25f); Request(1, 0.5, 0.75f, mask: mask); Evaluate();
                    Assert.That(X(rigEntity), Is.EqualTo(rigKey == "testRig" ? 10.25f : 1.25f).Within(1e-5));
                }
                finally { mask.Dispose(); }
            }
        }
        [Test]
        public void Pipeline_AdditiveLayerUsesClipsReferencePose()
        {
            AddPoseBuffers(rigEntity); Request(0, 0.5, 1); Request(1, 0.5, 0.2f, 1, PoseBlendMode.Additive); Evaluate();
            Assert.That(X(rigEntity), Is.EqualTo(10).Within(1e-5));
        }
        [Test]
        public void ModelPose_ComposesFullAffineMatricesWithoutDiscardingShear()
        {
            var rootPose = BoneLocalPose.Identity; rootPose.Scale = new float3(2, 1, 1); rootPose.Rotation = quaternion.RotateZ(math.PI / 4); rootPose.Position = new float3(3, 4, 5);
            var childPose = BoneLocalPose.Identity; childPose.Rotation = quaternion.RotateZ(math.PI / 4); childPose.Scale = new float3(1, 3, 1); childPose.Position = new float3(1, 2, 3);
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<RigDefinitionBlob>(); root.Version = AnimationBakeVersion.Schema; root.ContentKey = new FixedString128Bytes("testRig");
                var parents = builder.Allocate(ref root.Parents, 2); parents[0] = -1; parents[1] = 0;
                var rest = builder.Allocate(ref root.RestLocalPoses, 2); rest[0] = rootPose; rest[1] = childPose;
                rig.Dispose(); rig = builder.CreateBlobAssetReference<RigDefinitionBlob>(Allocator.Persistent);
            }
            world.EntityManager.SetComponentData(rigEntity, new AnimationRig { Rig = rig, Library = library });
            AddPoseBuffers(rigEntity); Evaluate();
            var expected = math.mul(float4x4.TRS(rootPose.Position, rootPose.Rotation, rootPose.Scale), float4x4.TRS(childPose.Position, childPose.Rotation, childPose.Scale));
            var actual = world.EntityManager.GetBuffer<BoneModelPose>(rigEntity)[1].Matrix;
            Assert.That(math.distance(actual.c0, expected.c0.xyz) + math.distance(actual.c1, expected.c1.xyz)
                + math.distance(actual.c2, expected.c2.xyz) + math.distance(actual.c3, expected.c3.xyz), Is.LessThan(1e-5));
            Assert.That(math.abs(math.dot(actual.c0, actual.c1)), Is.GreaterThan(1), "Fixture must contain genuine shear.");
        }
        [Test]
        public void SharedLibrary_RigsHaveIndependentMutablePosesAndEntityReuseStartsClean()
        {
            AddPoseBuffers(rigEntity); var other = CreateRig(world); AddPoseBuffers(other);
            Command(ScriptedPlaybackCommand.Play(new ClipId(0), 1, 0.2));
            world.EntityManager.GetBuffer<ScriptedPlaybackCommand>(other).Add(ScriptedPlaybackCommand.Play(new ClipId(0), 1, 0.8)); Step(1, 0); Evaluate();
            Assert.That(X(rigEntity), Is.EqualTo(2).Within(1e-5)); Assert.That(X(other), Is.EqualTo(8).Within(1e-5));
            world.EntityManager.DestroyEntity(other); var replacement = CreateRig(world); AddPoseBuffers(replacement); Evaluate();
            Assert.That(X(replacement), Is.EqualTo(1)); Assert.That(X(rigEntity), Is.EqualTo(2).Within(1e-5));
        }
    }
}
