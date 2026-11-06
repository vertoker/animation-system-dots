using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace AnimationSystemDOTS
{
    [UpdateInGroup(typeof(AnimationSimulationSystemGroup))]
    [UpdateAfter(typeof(ModelPoseSystem))]
    public partial class BoneExportSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Dependency = new ExportJob
            {
                Models = GetBufferLookup<BoneModelPose>(true),
                Locals = GetComponentLookup<LocalTransform>(true),
                Parents = GetComponentLookup<Parent>(true),
                Scales = GetComponentLookup<PostTransformMatrix>(true)
            }.ScheduleParallel(Dependency);
        }

        [BurstCompile]
        partial struct ExportJob : IJobEntity
        {
            [ReadOnly] public BufferLookup<BoneModelPose> Models;
            [ReadOnly] public ComponentLookup<LocalTransform> Locals;
            [ReadOnly] public ComponentLookup<Parent> Parents;
            [ReadOnly] public ComponentLookup<PostTransformMatrix> Scales;

            void Execute(in BoneExportBinding binding, ref LocalToWorld output)
            {
                if (!Models.TryGetBuffer(binding.Rig, out var models) || binding.BoneIndex < 0
                    || binding.BoneIndex >= models.Length || !Locals.HasComponent(binding.Rig)) return;
                // LocalToWorld can still contain the previous step here. Resolve the
                // rig's current placement from transform inputs before stock transforms.
                TransformHelpers.ComputeWorldTransformMatrix(binding.Rig, out var world, ref Locals, ref Parents, ref Scales);
                var model = models[binding.BoneIndex].Matrix;
                var affine = new float4x4(new float4(model.c0, 0), new float4(model.c1, 0), new float4(model.c2, 0), new float4(model.c3, 1));
                output.Value = math.mul(world, affine);
            }
        }
    }
}
