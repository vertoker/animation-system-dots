using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace AnimationSystemDOTS.Rendering
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateBefore(typeof(UpdatePresentationSystemGroup))]
    public partial class AnimatedBoundsPublishSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Dependency = new BoundsJob
            {
                Worlds = GetComponentLookup<LocalToWorld>(true), Models = GetBufferLookup<BoneModelPose>(true)
            }.ScheduleParallel(Dependency);
        }

        [BurstCompile]
        partial struct BoundsJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<LocalToWorld> Worlds;
            [ReadOnly] public BufferLookup<BoneModelPose> Models;
            void Execute(in AnimatedRenderBinding binding, in LocalToWorld renderWorld, ref RenderBounds output)
            {
                if (!binding.Binding.IsCreated || !Worlds.TryGetComponent(binding.Rig, out var rigWorld)) return;
                ref var blob = ref binding.Binding.Value;
                var minimum = blob.RigBounds.Center - blob.RigBounds.Extents;
                var maximum = blob.RigBounds.Center + blob.RigBounds.Extents;
                if (Models.TryGetBuffer(binding.Rig, out var models) && blob.BoneBounds.Length == blob.BoneRemap.Length)
                    for (var bone = 0; bone < blob.BoneRemap.Length; bone++)
                    {
                        if ((uint)blob.BoneRemap[bone] >= (uint)models.Length) continue;
                        var model = models[blob.BoneRemap[bone]].Matrix; var bounds = blob.BoneBounds[bone];
                        var linear = new float3x3(model.c0, model.c1, model.c2);
                        var center = math.mul(linear, bounds.Center) + model.c3;
                        var extents = math.mul(new float3x3(math.abs(linear.c0), math.abs(linear.c1), math.abs(linear.c2)), bounds.Extents);
                        minimum = math.min(minimum, center - extents); maximum = math.max(maximum, center + extents);
                    }
                var determinant = math.determinant(new float3x3(renderWorld.Value.c0.xyz, renderWorld.Value.c1.xyz, renderWorld.Value.c2.xyz));
                if (!math.isfinite(determinant) || determinant == 0) return;
                var renderFromRig = math.mul(math.inverse(renderWorld.Value), rigWorld.Value);
                var localCenter = math.transform(renderFromRig, (minimum + maximum) * .5f);
                var localExtents = math.mul(new float3x3(math.abs(renderFromRig.c0.xyz), math.abs(renderFromRig.c1.xyz), math.abs(renderFromRig.c2.xyz)), (maximum - minimum) * .5f);
                // Round outward so floating-point packing never trims an extreme vertex.
                localExtents += math.max(new float3(1), math.abs(localCenter) + localExtents) * 1e-5f;
                output.Value = new AABB { Center = localCenter, Extents = localExtents };
            }
        }
    }
}
