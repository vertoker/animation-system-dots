using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
namespace AnimationSystemDOTS
{
    [UpdateInGroup(typeof(AnimationSimulationSystemGroup))]
    [UpdateAfter(typeof(AnimationPoseModificationSystemGroup))]
    public partial class ModelPoseSystem : SystemBase
    {
        protected override void OnCreate() => RequireForUpdate<AnimationStepContext>();
        protected override void OnUpdate() => Dependency = new ModelJob().ScheduleParallel(Dependency);

        [BurstCompile]
        partial struct ModelJob : IJobEntity
        {
            void Execute(in AnimationRig rig, in DynamicBuffer<BoneLocalPose> local, DynamicBuffer<BoneModelPose> model)
            {
                if (!rig.Rig.IsCreated || rig.Rig.Value.Parents.Length != local.Length) { model.Clear(); return; }
                for (var i = 0; i < local.Length; i++)
                    if (rig.Rig.Value.Parents[i] >= i) { model.Clear(); return; }
                model.ResizeUninitialized(local.Length);
                // One iteration owns one whole rig. Parent-before-child order is preserved
                // within the job; no dependent bones run as unordered parallel iterations.
                for (var i = 0; i < local.Length; i++)
                {
                    var pose = local[i];
                    var trs = float4x4.TRS(pose.Position, pose.Rotation, pose.Scale);
                    var result = new float3x4(trs.c0.xyz, trs.c1.xyz, trs.c2.xyz, trs.c3.xyz);
                    var parentIndex = rig.Rig.Value.Parents[i];
                    if (parentIndex >= 0)
                    {
                        var parent = model[parentIndex].Matrix;
                        var linear = new float3x3(parent.c0, parent.c1, parent.c2);
                        result = new float3x4(math.mul(linear, result.c0), math.mul(linear, result.c1),
                            math.mul(linear, result.c2), math.mul(linear, result.c3) + parent.c3);
                    }
                    model[i] = new BoneModelPose { Matrix = result };
                }
            }
        }
    }
}
