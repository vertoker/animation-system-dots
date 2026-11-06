using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
namespace AnimationSystemDOTS
{
    [UpdateInGroup(typeof(AnimationSimulationSystemGroup))]
    [UpdateAfter(typeof(AnimationSampleSystem))]
    public partial class AnimationBlendSystem : SystemBase
    {
        protected override void OnCreate() => RequireForUpdate<AnimationStepContext>();
        protected override void OnUpdate() => Dependency = new BlendJob
        { Controllers = GetComponentLookup<ControllerLayerState>(true), Playback = GetComponentLookup<AnimationPlaybackMode>(true) }.ScheduleParallel(Dependency);

        [BurstCompile]
        [WithNone(typeof(PreciseHumanoidPose))]
        partial struct BlendJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<ControllerLayerState> Controllers;
            [ReadOnly] public ComponentLookup<AnimationPlaybackMode> Playback;
            void Execute(Entity entity, in AnimationRig rig, in DynamicBuffer<AnimationClipRequest> requests,
                in DynamicBuffer<SampledBonePose> samples, DynamicBuffer<BoneLocalPose> local)
            {
                if (Controllers.HasComponent(entity) && Playback.HasComponent(entity) && Playback[entity].Value == PlaybackDriver.Controller
                    && Controllers[entity].Result == ControllerEvaluationResult.Success && Controllers[entity].RetainPose != 0 && local.Length > 0) return;
                if (!rig.Rig.IsCreated) { local.Clear(); return; }
                var count = rig.Rig.Value.RestLocalPoses.Length;
                local.ResizeUninitialized(count);
                for (var bone = 0; bone < count; bone++)
                {
                    var result = rig.Rig.Value.RestLocalPoses[bone];
                    var first = true; var previousLayer = 0;
                    // Read requests without reordering the producer's buffer. Layers are
                    // ascending; additive contributions within one layer retain input order.
                    for (var pass = 0; pass < requests.Length && samples.Length == requests.Length * count; pass++)
                    {
                        var found = false; var layer = int.MaxValue;
                        for (var i = 0; i < requests.Length; i++)
                            if ((first || requests[i].Layer > previousLayer) && (!found || requests[i].Layer < layer))
                            { layer = requests[i].Layer; found = true; }
                        if (!found) break;
                        first = false; previousLayer = layer;
                        var average = BoneLocalPose.Identity; double total = 0;
                        for (var i = 0; i < requests.Length; i++)
                        {
                            var request = requests[i]; var sample = samples[i * count + bone];
                            if (request.Layer != layer || request.BlendMode != PoseBlendMode.Override || sample.IsValid == 0) continue;
                            var weight = Weight(request, rig, bone, count);
                            if (weight <= 0) continue;
                            average = PoseBlendMath.Override(average, sample.Pose, (float)(weight / (total + weight)));
                            total += weight;
                        }
                        if (total > 0) result = PoseBlendMath.Override(result, average, (float)math.min(total, 1));
                        for (var i = 0; i < requests.Length; i++)
                        {
                            var request = requests[i]; var sample = samples[i * count + bone];
                            if (request.Layer != layer || request.BlendMode != PoseBlendMode.Additive || sample.IsValid == 0) continue;
                            var weight = Weight(request, rig, bone, count);
                            if (weight <= 0) continue;
                            result = PoseBlendMath.Additive(result, sample.Pose, rig.Library.Value.Clips[request.Clip.Value].ReferencePose[bone], weight);
                        }
                    }
                    local[bone] = result;
                }
            }
            static float Weight(in AnimationClipRequest request, in AnimationRig rig, int bone, int count)
            {
                if (!math.isfinite(request.Weight) || request.Weight <= 0) return 0;
                if (!request.Mask.IsCreated) return request.Weight;
                ref var mask = ref request.Mask.Value;
                if (mask.Version != AnimationBakeVersion.Schema || mask.RigKey != rig.Rig.Value.ContentKey || mask.Weights.Length != count) return 0;
                var value = mask.Weights[bone];
                return math.isfinite(value) ? request.Weight * math.saturate(value) : 0;
            }
        }
    }
}
