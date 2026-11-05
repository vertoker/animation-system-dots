using Unity.Burst;
using Unity.Collections;
using Unity.Deformations;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace AnimationSystemDOTS.Rendering
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateBefore(typeof(DeformationsInPresentation))]
    public partial class SkinMatrixPublishSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Dependency = new PublishJob
            {
                Models = GetBufferLookup<BoneModelPose>(true),
                Rigs = GetComponentLookup<AnimationRig>(true),
                Worlds = GetComponentLookup<LocalToWorld>(true)
            }.ScheduleParallel(Dependency);
        }

        [BurstCompile]
        partial struct PublishJob : IJobEntity
        {
            [ReadOnly] public BufferLookup<BoneModelPose> Models;
            [ReadOnly] public ComponentLookup<AnimationRig> Rigs;
            [ReadOnly] public ComponentLookup<LocalToWorld> Worlds;

            void Execute(in SkinnedMeshBinding binding, DynamicBuffer<SkinMatrix> palette, ref SkinMatrixPublication publication)
            {
                if (!binding.Binding.IsCreated || binding.Binding.Value.Version != AnimationBakeVersion.Schema
                    || !Rigs.TryGetComponent(binding.Rig, out var rig) || !rig.Rig.IsCreated
                    || binding.Binding.Value.RigKey != rig.Rig.Value.ContentKey)
                { publication.Result = SkinMatrixPublicationResult.InvalidBinding; return; }
                if (!Models.TryGetBuffer(binding.Rig, out var models)
                    || !Worlds.TryGetComponent(binding.Rig, out var rigWorld)
                    || !Worlds.TryGetComponent(binding.DeformationRoot, out var rootWorld))
                { publication.Result = SkinMatrixPublicationResult.MissingPose; return; }
                ref var blob = ref binding.Binding.Value;
                if (blob.BoneRemap.Length != blob.BindPoses.Length || models.Length != rig.Rig.Value.Parents.Length)
                { publication.Result = SkinMatrixPublicationResult.InvalidBinding; return; }
                for (var i = 0; i < blob.BoneRemap.Length; i++)
                    if ((uint)blob.BoneRemap[i] >= (uint)models.Length)
                    { publication.Result = SkinMatrixPublicationResult.InvalidBinding; return; }
                var determinant = math.determinant(new float3x3(rootWorld.Value.c0.xyz, rootWorld.Value.c1.xyz, rootWorld.Value.c2.xyz));
                if (!math.isfinite(determinant) || determinant == 0)
                { publication.Result = SkinMatrixPublicationResult.SingularRoot; return; }
                var rootFromRig = math.mul(math.inverse(rootWorld.Value), rigWorld.Value);
                palette.ResizeUninitialized(blob.BoneRemap.Length);
                for (var i = 0; i < palette.Length; i++)
                {
                    var model = models[blob.BoneRemap[i]].Matrix;
                    var affine = new float4x4(new float4(model.c0, 0), new float4(model.c1, 0), new float4(model.c2, 0), new float4(model.c3, 1));
                    var skin = math.mul(math.mul(rootFromRig, affine), blob.BindPoses[i]);
                    palette[i] = new SkinMatrix { Value = new float3x4(skin.c0.xyz, skin.c1.xyz, skin.c2.xyz, skin.c3.xyz) };
                }
                publication.Result = SkinMatrixPublicationResult.Success;
            }
        }
    }
}
