using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace AnimationSystemDOTS.Humanoid
{
    [UpdateInGroup(typeof(AnimationSimulationSystemGroup))]
    [UpdateAfter(typeof(AnimationBlendSystem))]
    [UpdateBefore(typeof(AnimationPoseModificationSystemGroup))]
    public partial class HumanoidBlendSystem : SystemBase
    {
        protected override void OnCreate() => RequireForUpdate<AnimationStepContext>();
        protected override void OnUpdate() => Dependency = new BlendJob().ScheduleParallel(Dependency);

        [BurstCompile]
        partial struct BlendJob : IJobEntity
        {
            void Execute(in HumanoidRigBinding binding, in AnimationRig rig, in DynamicBuffer<AnimationClipRequest> requests,
                ref HumanoidBodyPose body, DynamicBuffer<HumanoidMuscleValue> muscles)
            {
                body = new HumanoidBodyPose { Rotation = quaternion.identity, Result = HumanoidEvaluationResult.NoRequests };
                muscles.ResizeUninitialized(95);
                for (var m = 0; m < muscles.Length; m++) muscles[m] = default;
                if (requests.Length == 0) return;
                if (!rig.Rig.IsCreated || !rig.Library.IsCreated || rig.Library.Value.Version != AnimationBakeVersion.Schema
                    || rig.Library.Value.RigKey != rig.Rig.Value.ContentKey)
                { body.Result = HumanoidEvaluationResult.InvalidDefinition; return; }
                double total = 0; var rotation = float4.zero; var reference = quaternion.identity; var layer = requests[0].Layer;
                for (var r = 0; r < requests.Length; r++)
                {
                    var request = requests[r];
                    if (!math.isfinite(request.Weight) || request.Weight <= 0) continue;
                    if ((uint)request.Clip.Value >= (uint)rig.Library.Value.Clips.Length || !math.isfinite(request.Time))
                    { body.Result = HumanoidEvaluationResult.InvalidChannels; return; }
                    if (request.BlendMode != PoseBlendMode.Override)
                    { body.Result = HumanoidEvaluationResult.UnsupportedBlendMode; return; }
                    if (request.Mask.IsCreated) { body.Result = HumanoidEvaluationResult.UnsupportedMask; return; }
                    if (request.Layer != layer) { body.Result = HumanoidEvaluationResult.UnsupportedLayer; return; }
                    ref var clip = ref rig.Library.Value.Clips[request.Clip.Value];
                    if (clip.Version != AnimationBakeVersion.Schema || clip.RigKey != rig.Rig.Value.ContentKey
                        || clip.HumanMuscles.Length != 95 || clip.HumanBody.Length != clip.SampleCount || clip.SampleCount < 1
                        || clip.Duration > 0 && (clip.SampleCount < 2 || !math.isfinite(clip.SampleStep) || clip.SampleStep <= 0))
                    { body.Result = HumanoidEvaluationResult.InvalidChannels; return; }
                    Interval(ref clip, request.Time, request.LoopBoundary, out var first, out var alpha);
                    for (var m = 0; m < muscles.Length; m++)
                    {
                        ref var samples = ref clip.HumanMuscles[m].Samples;
                        if (samples.Length != 1 && samples.Length != clip.SampleCount)
                        { body.Result = HumanoidEvaluationResult.InvalidChannels; return; }
                        var value = samples.Length == 1 ? samples[0] : math.lerp(samples[first], samples[first + 1], alpha);
                        if (!math.isfinite(value)) { body.Result = HumanoidEvaluationResult.InvalidChannels; return; }
                        muscles[m] = new HumanoidMuscleValue { Value = muscles[m].Value + value * request.Weight };
                    }
                    var a = clip.HumanBody[first];
                    var b = clip.HumanBody[math.min(first + 1, clip.SampleCount - 1)];
                    var position = math.lerp(a.Position, b.Position, alpha);
                    var q = math.slerp(a.Rotation, b.Rotation, alpha);
                    if (!math.all(math.isfinite(position)) || !math.all(math.isfinite(q.value)) || math.lengthsq(q.value) < .5f)
                    { body.Result = HumanoidEvaluationResult.InvalidChannels; return; }
                    if (total == 0) reference = q;
                    body.Position += position * request.Weight;
                    rotation += q.value * (math.dot(reference.value, q.value) < 0 ? -request.Weight : request.Weight);
                    total += request.Weight;
                }
                if (total <= 0) return;
                var inverse = (float)(1 / total);
                for (var m = 0; m < muscles.Length; m++) muscles[m] = new HumanoidMuscleValue { Value = muscles[m].Value * inverse };
                body.Position *= inverse;
                body.Rotation = math.normalizesafe(new quaternion(rotation), quaternion.identity);
                body.Result = HumanoidEvaluationResult.Success;
            }

            static void Interval(ref AnimationClipBlob clip, double time, ClipLoopBoundary boundary, out int first, out float alpha)
            {
                if (clip.SampleCount == 1 || clip.Duration <= 0) { first = 0; alpha = 0; return; }
                if (clip.IsLooping != 0)
                {
                    var original = time; time %= clip.Duration; if (time < 0) time += clip.Duration;
                    if (time == 0 && (boundary == ClipLoopBoundary.End || boundary == ClipLoopBoundary.AuthoredEndpoint && original > 0)) time = clip.Duration;
                }
                else time = math.clamp(time, 0, clip.Duration);
                var sample = time / clip.SampleStep;
                first = math.min((int)math.floor(sample), clip.SampleCount - 2); alpha = (float)(sample - first);
            }
        }
    }
}
